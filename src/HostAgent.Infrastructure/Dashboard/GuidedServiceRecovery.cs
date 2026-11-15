using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HostAgent.Core.Dashboard;
using HostAgent.Core.Models.Audit;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Storage;

namespace HostAgent.Infrastructure.Dashboard;

public sealed record GuidedServiceRecoveryEvidence(string Source, DateTimeOffset ObservedAtUtc);
public sealed record GuidedServiceDependency(string ServiceId, string ServiceName,
    ServiceControllerStatus Status, DateTimeOffset ObservedAtUtc);
public sealed record GuidedServiceRecoveryPreview(string IncidentId, string ServiceId, string ServiceName,
    string DiagnosisId, int Confidence, DateTimeOffset IncidentObservedAtUtc,
    IReadOnlyList<GuidedServiceRecoveryEvidence> Evidence, HelperOperationRequest Request,
    ApprovedTarget Target, ServiceControllerStatus StatusAtPreview, DateTimeOffset StatusObservedAtUtc,
    IReadOnlyList<GuidedServiceDependency> DependenciesAtPreview, DateTimeOffset DependenciesObservedAtUtc,
    DateTimeOffset PreparedAtUtc, DateTimeOffset ExpiresAtUtc)
{
    public string ConfirmationPhrase => DependenciesAtPreview.Count == 0
        ? $"START SERVICE {ServiceName} WITH NO DEPENDENCIES"
        : $"START SERVICE {ServiceName} WITH DEPENDENCIES {string.Join(", ", DependenciesAtPreview.Select(item => item.ServiceName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))}";
}
public enum GuidedServiceRecoveryOutcome { Succeeded, Rejected, Unknown }
public sealed record GuidedServiceRecoveryReceipt(Guid RequestId, GuidedServiceRecoveryOutcome Outcome,
    string Code, DateTimeOffset AtUtc, string? HelperResult, string? HelperCode, bool AuditRecorded,
    ServiceControllerStatus? ServiceReadback, DateTimeOffset? ServiceReadbackAtUtc);
public sealed class GuidedServiceRecoveryException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}
public interface IGuidedServiceRecoveryAuthority
{
    string ApproverSid { get; }
    ValueTask<ServiceControllerStatus> ReadServiceStatusAsync(string serviceName, CancellationToken token);
    ValueTask<IReadOnlyList<ServiceDependencySnapshot>> ReadServiceDependenciesAsync(string serviceName, CancellationToken token);
    HelperConfiguration LoadConfiguration();
    bool RemediationDisabled { get; }
    ValueTask<IReadOnlyList<AuditRecord>> ReadAuditAsync(Guid requestId, CancellationToken token);
    ValueTask<OperationApproval> CreateApprovalAsync(HelperOperationRequest request,
        ApprovedTarget displayedTarget, CancellationToken token);
}
public sealed record ServiceDependencySnapshot(string ServiceName, ServiceControllerStatus Status, DateTimeOffset ObservedAtUtc);

public sealed class GuidedServiceRecoveryWorkflow(
    Func<byte[], CancellationToken, ValueTask<byte[]>> send,
    IGuidedServiceRecoveryAuthority authority, TimeProvider? clock = null,
    TimeSpan? readbackTimeout = null, TimeSpan? readbackInterval = null)
{
    private static readonly string[] RequiredSources = ["eventlog.service-crash", "service.state_change"];
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly TimeSpan _readbackTimeout = ValidateReadbackTimeout(readbackTimeout ?? TimeSpan.FromSeconds(10));
    private readonly TimeSpan _readbackInterval = ValidateReadbackInterval(readbackInterval ?? TimeSpan.FromMilliseconds(250));
    private sealed record CurrentIncident(string DiagnosisId, int Confidence, string State,
        DateTimeOffset ObservedAtUtc, IReadOnlyList<GuidedServiceRecoveryEvidence> Evidence);

    public async ValueTask<GuidedServiceRecoveryPreview> PrepareAsync(string incidentId, string serviceName, CancellationToken token)
    {
        if (!Guid.TryParseExact(incidentId, "N", out var id) || id == Guid.Empty || !SafeServiceName(serviceName))
            throw new GuidedServiceRecoveryException("invalid_service_recovery_target");
        incidentId = id.ToString("N");
        var current = await ReadCurrentIncidentAsync(incidentId, serviceName, token);
        var statusAtPreview = await EnsureStoppedAsync(serviceName, token);
        var statusObservedAtUtc = _clock.GetUtcNow();
        var config = LoadEnabledConfiguration();
        var registration = FindRegistration(config, serviceName);
        var dependencySnapshot = await ReadAuthorizedDependenciesAsync(serviceName, config, token);
        var dependencyIds = dependencySnapshot.Select(item => item.ServiceId).ToArray();
        var bytes = AdminProtocol.Request("propose", new
        {
            incidentId,
            operation = HelperOperation.StartService.ToString(),
            parameters = new { serviceId = registration.Id, expectedDependencyIds = dependencyIds }
        });
        using var proposal = JsonDocument.Parse(await send(bytes, token));
        var root = proposal.RootElement;
        if (!root.GetProperty("ok").GetBoolean() || root.GetProperty("code").GetString() != "confirmation_required")
            throw new GuidedServiceRecoveryException("service_recovery_proposal_unavailable");
        var request = HelperProtocol.ParseRequest(JsonSerializer.SerializeToUtf8Bytes(root.GetProperty("data")));
        if (request.RequestId == Guid.Empty || request.ApprovalId is not null || request.IncidentId != incidentId ||
            request.DiagnosisId != current.DiagnosisId || request.Confidence != current.Confidence ||
            request.Operation != HelperOperation.StartService ||
            request.Parameters is not ServiceParameters parameters || parameters.ServiceId != registration.Id ||
            parameters.ExpectedDependencyIds is null || !parameters.ExpectedDependencyIds.SequenceEqual(dependencyIds))
            throw new GuidedServiceRecoveryException("service_recovery_proposal_invalid");
        var binding = new HelperAllowlist(config.Services, config.Processes).Resolve(request);
        if (binding is null || !string.Equals(binding.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
            throw new GuidedServiceRecoveryException("target_not_registered");
        if (binding.ApprovedDependencyNames is null || !binding.ApprovedDependencyNames.SequenceEqual(
                dependencySnapshot.Select(item => item.ServiceName), StringComparer.OrdinalIgnoreCase))
            throw new GuidedServiceRecoveryException("service_dependency_policy_mismatch");
        var now = _clock.GetUtcNow();
        return new(incidentId, registration.Id, binding.ServiceName!, current.DiagnosisId, current.Confidence,
            current.ObservedAtUtc, current.Evidence.ToArray(),
            request with { EvidenceReferences = request.EvidenceReferences.ToArray() },
            new(binding.ServiceName, binding.ExecutablePath, binding.Sha256), statusAtPreview,
            statusObservedAtUtc, dependencySnapshot, dependencySnapshot.Length == 0
                ? now : dependencySnapshot.Max(item => item.ObservedAtUtc), now, now.AddMinutes(2));
    }

    public async ValueTask<GuidedServiceRecoveryReceipt> ExecuteAsync(
        GuidedServiceRecoveryPreview preview, string typedConfirmation, CancellationToken token)
    {
        if (preview is null) throw new ArgumentNullException(nameof(preview));
        ValidatePreview(preview, typedConfirmation, _clock.GetUtcNow());
        var current = await ReadCurrentIncidentAsync(preview.IncidentId, preview.ServiceName, token);
        if (current.DiagnosisId != preview.DiagnosisId || current.Confidence != preview.Confidence ||
            current.ObservedAtUtc != preview.IncidentObservedAtUtc || !current.Evidence.SequenceEqual(preview.Evidence))
            throw new GuidedServiceRecoveryException("service_recovery_review_stale");
        _ = await EnsureStoppedAsync(preview.ServiceName, token);
        var config = LoadEnabledConfiguration();
        await EnsureDependenciesMatchAsync(preview, config, token);
        var registration = FindRegistration(config, preview.ServiceName);
        if (registration.Id != preview.ServiceId)
            throw new GuidedServiceRecoveryException("service_recovery_review_stale");
        var binding = new HelperAllowlist(config.Services, config.Processes).Resolve(preview.Request);
        if (binding is null || !string.Equals(binding.ServiceName, preview.ServiceName, StringComparison.OrdinalIgnoreCase) ||
            binding.ApprovedDependencyNames is null || !binding.ApprovedDependencyNames.SequenceEqual(
                preview.DependenciesAtPreview.Select(item => item.ServiceName), StringComparer.OrdinalIgnoreCase))
            throw new GuidedServiceRecoveryException("target_not_registered");
        var target = new ApprovedTarget(binding.ServiceName, binding.ExecutablePath, binding.Sha256);
        if (target != preview.Target) throw new GuidedServiceRecoveryException("service_recovery_review_stale");
        token.ThrowIfCancellationRequested();
        var approval = await authority.CreateApprovalAsync(preview.Request, preview.Target, token);
        ValidateApproval(preview, approval, _clock.GetUtcNow());

        // Re-read every displayed precondition after the local approval has been
        // journaled and immediately before the helper receives execute.
        var justInTimeIncident = await ReadCurrentIncidentAsync(preview.IncidentId, preview.ServiceName, token);
        if (justInTimeIncident.DiagnosisId != preview.DiagnosisId ||
            justInTimeIncident.Confidence != preview.Confidence ||
            justInTimeIncident.ObservedAtUtc != preview.IncidentObservedAtUtc ||
            !justInTimeIncident.Evidence.SequenceEqual(preview.Evidence))
            throw new GuidedServiceRecoveryException("service_recovery_review_stale");
        _ = await EnsureStoppedAsync(preview.ServiceName, token);
        var justInTimeConfig = LoadEnabledConfiguration();
        await EnsureDependenciesMatchAsync(preview, justInTimeConfig, token);
        var justInTimeRegistration = FindRegistration(justInTimeConfig, preview.ServiceName);
        var justInTimeBinding = new HelperAllowlist(justInTimeConfig.Services, justInTimeConfig.Processes).Resolve(preview.Request);
        if (justInTimeRegistration.Id != preview.ServiceId || justInTimeBinding is null ||
            justInTimeBinding.ApprovedDependencyNames is null || !justInTimeBinding.ApprovedDependencyNames.SequenceEqual(
                preview.DependenciesAtPreview.Select(item => item.ServiceName), StringComparer.OrdinalIgnoreCase) ||
            new ApprovedTarget(justInTimeBinding.ServiceName, justInTimeBinding.ExecutablePath, justInTimeBinding.Sha256) != preview.Target)
            throw new GuidedServiceRecoveryException("service_recovery_review_stale");

        var approvedRequest = preview.Request with { ApprovalId = approval.ApprovalId };
        ParsedCommand command;
        try
        {
            using var requestDoc = JsonDocument.Parse(HelperProtocol.SerializeRequest(approvedRequest));
            var response = await send(AdminProtocol.Request("execute",
                new { request = requestDoc.RootElement, approval }), token);
            command = ParseCommand(response, preview.Request.RequestId);
        }
        catch
        {
            command = new(null, "execute_outcome_uncertain", false, false);
        }

        var auditRecorded = false;
        try
        {
            var records = await authority.ReadAuditAsync(preview.Request.RequestId, token);
            auditRecorded = VerifyExecutionAudit(records, preview, approval, approvedRequest, command);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch { }

        ServiceReadback readback;
        try
        {
            readback = await ReadAfterExecuteAsync(preview.ServiceName,
                pollUntilRunning: !command.Rejected && command.Result != HelperResult.Denied.ToString(), token);
        }
        catch { readback = new(null, _clock.GetUtcNow()); }
        var outcome = command.Rejected || command.Valid && command.Result == HelperResult.Denied.ToString()
            ? GuidedServiceRecoveryOutcome.Rejected
            : command.Valid && command.Result == HelperResult.Succeeded.ToString() && command.Code == "succeeded" &&
              auditRecorded && readback.Status == ServiceControllerStatus.Running
                ? GuidedServiceRecoveryOutcome.Succeeded
                : GuidedServiceRecoveryOutcome.Unknown;
        var code = outcome switch
        {
            GuidedServiceRecoveryOutcome.Succeeded => "service_recovery_verified",
            GuidedServiceRecoveryOutcome.Rejected => command.Code,
            _ when !command.Valid => command.Code,
            _ when command.Result != HelperResult.Succeeded.ToString() => command.Code,
            _ when !auditRecorded => "helper_audit_unverified",
            _ when readback.Status != ServiceControllerStatus.Running => "service_running_not_verified",
            _ => "service_recovery_outcome_unverified"
        };
        return new(preview.Request.RequestId, outcome, code, _clock.GetUtcNow(), command.Result,
            command.Code, auditRecorded, readback.Status, readback.ObservedAtUtc);
    }

    private sealed record ParsedCommand(string? Result, string Code, bool Valid, bool Rejected);
    private sealed record ServiceReadback(ServiceControllerStatus? Status, DateTimeOffset? ObservedAtUtc);

    private static string ConfirmationPhraseFor(GuidedServiceRecoveryPreview preview)
    {
        var names = preview.DependenciesAtPreview.Select(item => item.ServiceName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        return names.Length == 0
            ? $"START SERVICE {preview.ServiceName} WITH NO DEPENDENCIES"
            : $"START SERVICE {preview.ServiceName} WITH DEPENDENCIES {string.Join(", ", names)}";
    }

    private void ValidatePreview(GuidedServiceRecoveryPreview preview, string typedConfirmation, DateTimeOffset now)
    {
        if (!string.Equals(typedConfirmation, ConfirmationPhraseFor(preview), StringComparison.Ordinal))
            throw new GuidedServiceRecoveryException("confirmation_phrase_mismatch");
        if (!SafeServiceName(preview.ServiceName) || !Guid.TryParseExact(preview.IncidentId, "N", out var id) ||
            id == Guid.Empty || preview.Request.RequestId == Guid.Empty ||
            preview.Request.Operation != HelperOperation.StartService ||
            preview.Request.Parameters is not ServiceParameters parameters || parameters.ServiceId != preview.ServiceId ||
            parameters.ExpectedDependencyIds is null ||
            !parameters.ExpectedDependencyIds.SequenceEqual(preview.DependenciesAtPreview.Select(item => item.ServiceId)) ||
            preview.DependenciesAtPreview.Count > 32 ||
            preview.DependenciesAtPreview.Select(item => item.ServiceId).Distinct(StringComparer.Ordinal).Count() != preview.DependenciesAtPreview.Count ||
            preview.DependenciesAtPreview.Select(item => item.ServiceName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != preview.DependenciesAtPreview.Count ||
            preview.DependenciesAtPreview.Any(item => !SafeServiceName(item.ServiceName) || item.ServiceId == preview.ServiceId ||
                !Enum.IsDefined(item.Status) || item.ObservedAtUtc > now || !ViewFreshness.IsFresh(item.ObservedAtUtc, now)) ||
            preview.DependenciesObservedAtUtc > now || !ViewFreshness.IsFresh(preview.DependenciesObservedAtUtc, now) ||
            preview.Request.IncidentId != preview.IncidentId || preview.Request.DiagnosisId != preview.DiagnosisId ||
            preview.Request.Confidence != preview.Confidence || preview.Target.ServiceName is null ||
            !string.Equals(preview.Target.ServiceName, preview.ServiceName, StringComparison.OrdinalIgnoreCase) ||
            preview.StatusAtPreview != ServiceControllerStatus.Stopped ||
            preview.StatusObservedAtUtc > now || !ViewFreshness.IsFresh(preview.StatusObservedAtUtc, now) ||
            preview.PreparedAtUtc > now || preview.ExpiresAtUtc <= now ||
            preview.ExpiresAtUtc > preview.PreparedAtUtc.AddMinutes(2))
            throw new GuidedServiceRecoveryException("service_recovery_review_stale");
    }

    private void ValidateApproval(GuidedServiceRecoveryPreview preview, OperationApproval approval, DateTimeOffset now)
    {
        if (approval.ApprovalId == Guid.Empty || approval.RequestId != preview.Request.RequestId ||
            approval.IncidentId != preview.IncidentId || approval.DiagnosisId != preview.DiagnosisId ||
            approval.Operation != HelperOperation.StartService ||
            approval.RequestDigest != HelperProtocol.Digest(preview.Request) ||
            approval.ApproverSid != authority.ApproverSid || approval.Target != preview.Target ||
            approval.ApprovedAtUtc > now || approval.ExpiresAtUtc <= now ||
            approval.ExpiresAtUtc > approval.ApprovedAtUtc.AddMinutes(5))
            throw new GuidedServiceRecoveryException("service_recovery_approval_invalid");
    }

    private async ValueTask<CurrentIncident> ReadCurrentIncidentAsync(string incidentId, string serviceName, CancellationToken token)
    {
        byte[] bytes;
        try { bytes = await send(AdminProtocol.Request("incident", new { incidentId }), token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { throw new GuidedServiceRecoveryException("incident_not_actionable"); }
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (!root.GetProperty("ok").GetBoolean()) throw new GuidedServiceRecoveryException("incident_not_actionable");
            var data = root.GetProperty("data");
            var cause = data.GetProperty("rootCause");
            var diagnosisId = data.GetProperty("diagnosisId").GetString();
            var state = data.GetProperty("state").GetString();
            var confidence = data.GetProperty("confidence").GetInt32();
            var observedAt = data.GetProperty("observedAtUtc").GetDateTimeOffset();
            var now = _clock.GetUtcNow();
            if (cause.GetProperty("code").GetString() != "service_crash" ||
                !string.Equals(cause.GetProperty("subject").GetString(), serviceName, StringComparison.OrdinalIgnoreCase) ||
                state is not ("Detected" or "Active" or "Acknowledged") || confidence is < 80 or > 100 ||
                !ViewFreshness.IsFresh(observedAt, now) || string.IsNullOrWhiteSpace(diagnosisId) || diagnosisId.Length > 128)
                throw new GuidedServiceRecoveryException("incident_not_actionable");
            var bySource = new Dictionary<string, GuidedServiceRecoveryEvidence>(StringComparer.Ordinal);
            foreach (var item in data.GetProperty("evidence").EnumerateArray())
            {
                var source = item.GetProperty("source").GetString();
                if (source is null || !RequiredSources.Contains(source, StringComparer.Ordinal)) continue;
                var at = item.GetProperty("observedAtUtc").GetDateTimeOffset();
                if (!ViewFreshness.IsFresh(at, now)) continue;
                if (!bySource.TryGetValue(source, out var prior) || prior.ObservedAtUtc < at)
                    bySource[source] = new(source, at);
            }
            var selected = RequiredSources.Select(source => bySource.TryGetValue(source, out var value) ? value : null).ToArray();
            if (selected.Any(item => item is null))
                throw new GuidedServiceRecoveryException("service_crash_not_corroborated");
            return new(diagnosisId!, confidence, state!, observedAt, selected.Select(item => item!).ToArray());
        }
        catch (GuidedServiceRecoveryException) { throw; }
        catch { throw new GuidedServiceRecoveryException("incident_not_actionable"); }
    }

    private async ValueTask<ServiceControllerStatus> EnsureStoppedAsync(string serviceName, CancellationToken token)
    {
        ServiceControllerStatus status;
        try { status = await authority.ReadServiceStatusAsync(serviceName, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { throw new GuidedServiceRecoveryException("service_status_unavailable"); }
        if (status != ServiceControllerStatus.Stopped)
            throw new GuidedServiceRecoveryException("service_is_no_longer_stopped");
        return status;
    }

    private async ValueTask<GuidedServiceDependency[]> ReadAuthorizedDependenciesAsync(string serviceName,
        HelperConfiguration config, CancellationToken token)
    {
        IReadOnlyList<ServiceDependencySnapshot> observed;
        try { observed = await authority.ReadServiceDependenciesAsync(serviceName, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { throw new GuidedServiceRecoveryException("service_dependencies_unavailable"); }
        var result = new List<GuidedServiceDependency>(observed.Count);
        foreach (var dependency in observed.OrderBy(item => item.ServiceName, StringComparer.OrdinalIgnoreCase))
        {
            if (!SafeServiceName(dependency.ServiceName) || dependency.ObservedAtUtc > _clock.GetUtcNow() ||
                !ViewFreshness.IsFresh(dependency.ObservedAtUtc, _clock.GetUtcNow()) || !Enum.IsDefined(dependency.Status))
                throw new GuidedServiceRecoveryException("service_dependencies_unavailable");
            var matches = config.Services.Where(item =>
                string.Equals(item.ServiceName, dependency.ServiceName, StringComparison.OrdinalIgnoreCase) &&
                item.EnabledOperations.Contains(HelperOperation.StartService)).ToArray();
            if (matches.Length != 1) throw new GuidedServiceRecoveryException("service_dependency_not_registered");
            result.Add(new(matches[0].Id, matches[0].ServiceName, dependency.Status, dependency.ObservedAtUtc));
        }
        if (result.Count > 32) throw new GuidedServiceRecoveryException("service_dependency_limit");
        return result.ToArray();
    }

    private async ValueTask EnsureDependenciesMatchAsync(GuidedServiceRecoveryPreview preview,
        HelperConfiguration config, CancellationToken token)
    {
        var current = await ReadAuthorizedDependenciesAsync(preview.ServiceName, config, token);
        if (current.Length != preview.DependenciesAtPreview.Count || current.Where((item, index) =>
                item.ServiceId != preview.DependenciesAtPreview[index].ServiceId ||
                !string.Equals(item.ServiceName, preview.DependenciesAtPreview[index].ServiceName, StringComparison.OrdinalIgnoreCase) ||
                item.Status != preview.DependenciesAtPreview[index].Status).Any())
            throw new GuidedServiceRecoveryException("service_recovery_review_stale");
    }

    private async ValueTask<ServiceReadback> ReadAfterExecuteAsync(string serviceName, bool pollUntilRunning,
        CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        ServiceControllerStatus? status = null;
        DateTimeOffset? observedAt = null;
        while (true)
        {
            try
            {
                status = await authority.ReadServiceStatusAsync(serviceName, token);
                observedAt = _clock.GetUtcNow();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return new(status, observedAt);
            }
            catch
            {
                return new(null, _clock.GetUtcNow());
            }

            if (status == ServiceControllerStatus.Running || !pollUntilRunning || stopwatch.Elapsed >= _readbackTimeout)
                return new(status, observedAt);

            var remaining = _readbackTimeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero) return new(status, observedAt);
            await Task.Delay(remaining < _readbackInterval ? remaining : _readbackInterval, token);
        }
    }

    private bool VerifyExecutionAudit(IReadOnlyList<AuditRecord> records, GuidedServiceRecoveryPreview preview,
        OperationApproval approval, HelperOperationRequest approvedRequest, ParsedCommand command)
    {
        var expectedEndCode = command.Result switch
        {
            "Succeeded" when command.Code == "succeeded" => "succeeded",
            "Failed" when command.Code == "execution_failed" => "execution_failed",
            "Indeterminate" when command.Code == "execution_interrupted" => "execution_interrupted",
            _ => null
        };
        if (command.Valid && command.Result is not null && expectedEndCode is null) return false;

        var matchingApproval = records.Where(record => record.Event == AuditEvent.Approval).ToArray();
        var matchingEnd = records.Where(record => record.Event == AuditEvent.ExecutionEnded).ToArray();
        if (records.Any(record => record.RequestId != preview.Request.RequestId) ||
            matchingApproval.Length != 1 || matchingEnd.Length != 1) return false;

        var approved = matchingApproval[0];
        var ended = matchingEnd[0];
        return approved.Code == "approved" && approved.ApprovalId == approval.ApprovalId &&
            approved.RequestDigest == HelperProtocol.Digest(preview.Request) &&
            approved.RequestId == preview.Request.RequestId && approved.IncidentId == preview.IncidentId &&
            approved.DiagnosisId == preview.DiagnosisId && approved.Operation == HelperOperation.StartService &&
            approved.InitiatorSid == authority.ApproverSid && approved.ApproverSid == authority.ApproverSid &&
            (expectedEndCode is null
                ? ended.Code is "succeeded" or "execution_failed" or "execution_interrupted"
                : ended.Code == expectedEndCode) && ended.ApprovalId == approval.ApprovalId &&
            ended.RequestDigest == HelperProtocol.Digest(approvedRequest) &&
            ended.RequestId == preview.Request.RequestId && ended.IncidentId == preview.IncidentId &&
            ended.DiagnosisId == preview.DiagnosisId && ended.Confidence == preview.Confidence &&
            ended.Operation == HelperOperation.StartService &&
            ended.EvidenceReferences.SequenceEqual(preview.Request.EvidenceReferences) &&
            ended.InitiatorSid == authority.ApproverSid && ended.ApproverSid == authority.ApproverSid;
    }

    private HelperConfiguration LoadEnabledConfiguration()
    {
        HelperConfiguration config;
        try { config = authority.LoadConfiguration(); }
        catch { throw new GuidedServiceRecoveryException("helper_configuration_unavailable"); }
        bool disabled;
        try { disabled = authority.RemediationDisabled; }
        catch { throw new GuidedServiceRecoveryException("approval_disabled"); }
        if (!config.ConfigurationAvailable || config.ReadOnly || config.Shadow || config.KillSwitch || disabled)
            throw new GuidedServiceRecoveryException("approval_disabled");
        return config;
    }

    private static ServiceRegistration FindRegistration(HelperConfiguration config, string serviceName)
    {
        var matches = config.Services.Where(item =>
            string.Equals(item.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase) &&
            item.EnabledOperations.Contains(HelperOperation.StartService)).ToArray();
        if (matches.Length != 1) throw new GuidedServiceRecoveryException("target_not_registered");
        return matches[0];
    }

    private static bool SafeServiceName(string value) =>
        value is { Length: > 0 and <= 128 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    private static ParsedCommand ParseCommand(byte[] bytes, Guid requestId)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.GetProperty("version").GetInt32() != 1) return new(null, "execute_receipt_invalid", false, false);
            var ok = root.GetProperty("ok").GetBoolean();
            var code = root.GetProperty("code").GetString() ?? "unknown";
            if (code == "control_unavailable")
                return new(null, code, false, true);
            if (code == "control_outcome_uncertain")
                return new(null, code, false, false);
            var data = root.GetProperty("data");
            if (data.ValueKind != JsonValueKind.Object) return new(null, "execute_receipt_invalid", false, false);
            var result = data.GetProperty("result").GetString();
            var actualId = data.GetProperty("RequestId").GetGuid();
            if (actualId != requestId)
                return new(null, "execute_receipt_mismatch", false, false);
            if (result == HelperResult.Denied.ToString() && !ok)
                return new(result, code, true, true);
            if (result == HelperResult.Succeeded.ToString() && ok && code == "succeeded")
                return new(result, code, true, false);
            if (result is "Failed" or "Indeterminate" or "TimedOut" or "Unavailable" && !ok)
                return new(result, code, true, false);
            return new(null, "execute_receipt_invalid", false, false);
        }
        catch
        {
            return new(null, "execute_receipt_unavailable", false, false);
        }
    }

    private static TimeSpan ValidateReadbackTimeout(TimeSpan value) =>
        value < TimeSpan.Zero || value > TimeSpan.FromSeconds(30)
            ? throw new ArgumentOutOfRangeException(nameof(value)) : value;

    private static TimeSpan ValidateReadbackInterval(TimeSpan value) =>
        value <= TimeSpan.Zero || value > TimeSpan.FromSeconds(5)
            ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
}

public sealed class WindowsGuidedServiceRecoveryAuthority : IGuidedServiceRecoveryAuthority
{
    private readonly string _programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private readonly SecurityIdentifier _approverSid;
    private readonly SecurityIdentifier _systemSid = new(WellKnownSidType.LocalSystemSid, null);
    private readonly TimeProvider _clock;
    public WindowsGuidedServiceRecoveryAuthority(TimeProvider? clock = null)
    {
        using var identity = WindowsIdentity.GetCurrent();
        _approverSid = identity.User ?? throw new UnauthorizedAccessException("approver_identity_unavailable");
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        if (!new WindowsPrincipal(identity).IsInRole(administrators))
            throw new UnauthorizedAccessException("elevated_approver_required");
        _clock = clock ?? TimeProvider.System;
    }
    public string ApproverSid => _approverSid.Value;
    private string Root => Path.Combine(_programData, "HostAgent.Helper");
    private string ConfigPath => Path.Combine(Root, "helper.json");
    private string JournalPath => Path.Combine(Root, "helper-journal.db");
    private string DisabledPath => Path.Combine(Root, "disable-remediation");
    private bool ValidateRoot()
    {
        if (!Directory.Exists(Root)) return false;
        HostAgentDataDirectory.ValidateSecureExisting(Root, _systemSid);
        return true;
    }
    public async ValueTask<ServiceControllerStatus> ReadServiceStatusAsync(string serviceName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var query = Task.Run(() =>
        {
            using var service = new ServiceController(serviceName);
            service.Refresh();
            return service.Status;
        }, CancellationToken.None);
        return await query.WaitAsync(TimeSpan.FromSeconds(2), token);
    }
    public async ValueTask<IReadOnlyList<ServiceDependencySnapshot>> ReadServiceDependenciesAsync(
        string serviceName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var query = Task.Run(() => ReadDependencyClosure(serviceName), CancellationToken.None);
        return await query.WaitAsync(TimeSpan.FromSeconds(2), token);
    }
    private IReadOnlyList<ServiceDependencySnapshot> ReadDependencyClosure(string rootServiceName)
    {
        return WindowsServiceDependencyReader.ReadClosure(rootServiceName).Select(item =>
            new ServiceDependencySnapshot(item.ServiceName, item.Status, _clock.GetUtcNow())).ToArray();
    }
    public HelperConfiguration LoadConfiguration()
    {
        if (!ValidateRoot()) return new();
        HelperProtectedFiles.ValidateExisting(ConfigPath, _systemSid);
        return HelperConfiguration.LoadSafe(ConfigPath);
    }
    public bool RemediationDisabled
    {
        get
        {
            if (!ValidateRoot() || !File.Exists(DisabledPath)) return false;
            HelperProtectedFiles.ValidateExisting(DisabledPath, _systemSid);
            return true;
        }
    }
    public async ValueTask<IReadOnlyList<AuditRecord>> ReadAuditAsync(Guid requestId, CancellationToken token)
    {
        if (!ValidateRoot() || !File.Exists(JournalPath))
            throw new FileNotFoundException("helper_audit_unavailable", JournalPath);
        foreach (var path in new[] { ConfigPath, JournalPath, JournalPath + "-wal", JournalPath + "-shm", JournalPath + "-journal" })
            HelperProtectedFiles.ValidateExisting(path, _systemSid);
        var config = HelperConfiguration.LoadSafe(ConfigPath);
        if (!config.ConfigurationAvailable) throw new InvalidDataException("helper_configuration_unavailable");
        await using var journal = new SqliteHelperJournal(JournalPath, config.ApproverSids, readOnly: true);
        return await journal.ReadAuditAsync(requestId, token);
    }
    public async ValueTask<OperationApproval> CreateApprovalAsync(
        HelperOperationRequest request, ApprovedTarget displayedTarget, CancellationToken token)
    {
        if (!ValidateRoot()) throw new GuidedServiceRecoveryException("helper_configuration_unavailable");
        foreach (var path in new[] { ConfigPath, JournalPath, JournalPath + "-wal", JournalPath + "-shm",
                     JournalPath + "-journal", DisabledPath })
            HelperProtectedFiles.ValidateExisting(path, _systemSid);
        var config = HelperConfiguration.LoadSafe(ConfigPath);
        if (!config.ConfigurationAvailable || config.ReadOnly || config.Shadow || config.KillSwitch || File.Exists(DisabledPath))
            throw new GuidedServiceRecoveryException("approval_disabled");
        if (!config.ApproverSids.Contains(ApproverSid, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("untrusted_approver");
        var binding = new HelperAllowlist(config.Services, config.Processes).Resolve(request);
        if (binding is null) throw new GuidedServiceRecoveryException("target_not_registered");
        var currentTarget = new ApprovedTarget(binding.ServiceName, binding.ExecutablePath, binding.Sha256);
        if (currentTarget != displayedTarget) throw new GuidedServiceRecoveryException("service_recovery_review_stale");
        await using var journal = new SqliteHelperJournal(JournalPath, config.ApproverSids);
        var issuer = new ApprovalIssuer(journal, ApproverSid, _clock);
        return await issuer.CreateAsync(request, displayedTarget, currentTarget, token);
    }
}
