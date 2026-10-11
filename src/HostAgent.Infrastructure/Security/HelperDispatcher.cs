using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Storage;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Core.Models.Policy;
using HostAgent.Core.Models.Audit;
namespace HostAgent.Infrastructure.Security;
public interface IOperationExecutor
{
    ValueTask ExecuteAsync(HelperOperationRequest request, OperationBinding binding, CancellationToken cancellationToken);
}
public sealed record HelperSettings(bool ReadOnly = true, bool Shadow = true, bool KillSwitch = false,
    bool ConfigurationAvailable = true, int ExecutionTimeoutSeconds = 15);
public sealed class HelperDispatcher
{
    private readonly SqliteHelperJournal _journal;
    private readonly HelperAllowlist _allowlist;
    private readonly IOperationExecutor _executor;
    private readonly IPolicyEngine _policy;
    private readonly Func<HelperSettings> _settings;
    private readonly GuardLimits _limits;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _auditFailed;
    public HelperDispatcher(SqliteHelperJournal journal, HelperAllowlist allowlist, IOperationExecutor executor,
        IPolicyEngine policy, Func<HelperSettings> settings, GuardLimits? limits = null, TimeProvider? timeProvider = null)
    { _journal = journal; _allowlist = allowlist; _executor = executor; _policy = policy; _settings = settings;
        _limits = limits ?? new(); _clock = timeProvider ?? TimeProvider.System; }
    public async ValueTask<HelperOperationResponse> ExecuteAsync(HelperOperationRequest request, string authenticatedSid,
        CancellationToken cancellationToken)
    {
        var entered = false; var executionStarted = false;
        HelperOperationResponse Response(HelperResult result, string code) => new(1, request.RequestId, result, code);
        try
        {
            // Freeze mutable evidence and enforce exactly the same contract as the pipe.
            request = HelperProtocol.ParseRequest(HelperProtocol.SerializeRequest(request));
            if (string.IsNullOrWhiteSpace(authenticatedSid)) return Response(HelperResult.Denied, "unauthorized");
            await _gate.WaitAsync(cancellationToken); entered = true;
            if (_auditFailed) return Response(HelperResult.Denied, "audit_unavailable");
            OperationApproval? approval = null;
            var timeoutSeconds = 0;
            using var requestDocument = System.Text.Json.JsonDocument.Parse(HelperProtocol.SerializeRequest(request));
            var parametersJson = requestDocument.RootElement.GetProperty("parameters").GetRawText();
            AuditRecord Record(AuditEvent kind, PolicyDecision decision, string code) => new(Guid.NewGuid(), _clock.GetUtcNow(), kind,
                request.RequestId, request.IncidentId, request.DiagnosisId, request.Confidence, request.EvidenceReferences,
                request.Operation, HelperProtocol.Digest(request), authenticatedSid, decision, request.ApprovalId,
                approval?.ApproverSid, approval?.ApprovedAtUtc, code) { ParametersJson = parametersJson, TimeoutSeconds = timeoutSeconds };
            await _journal.AppendAsync(Record(AuditEvent.Proposal, PolicyDecision.CONFIRM_REQUIRED, "proposed"), cancellationToken);
            approval = request.ApprovalId is { } id ? await _journal.ReadApprovalAsync(id, cancellationToken) : null;
            var binding = _allowlist.Resolve(request);
            if (approval is not null && (approval.Target is null || binding is null ||
                approval.Target != new ApprovedTarget(binding.ServiceName, binding.ExecutablePath, binding.Sha256)))
            {
                await _journal.AppendAsync(Record(AuditEvent.Decision, PolicyDecision.DENIED, "approval_target_mismatch"), cancellationToken);
                return Response(HelperResult.Denied, "approval_target_mismatch");
            }
            HelperSettings settings;
            PolicyDecision decision;
            try
            {
                settings = _settings();
                timeoutSeconds = settings.ExecutionTimeoutSeconds;
                decision = _policy.Evaluate(new PolicyContext { ReadOnly = settings.ReadOnly, Shadow = settings.Shadow,
                    KillSwitch = settings.KillSwitch, ConfigurationAvailable = settings.ConfigurationAvailable && settings.ExecutionTimeoutSeconds is >= 1 and <= 30,
                    StorageHealthy = true, OperationEnabled = binding is not null, Confidence = request.Confidence,
                    RequestId = request.RequestId, IncidentId = request.IncidentId, DiagnosisId = request.DiagnosisId,
                    Operation = request.Operation, TargetKey = HelperProtocol.TargetKey(request), RequestDigest = HelperProtocol.Digest(request),
                    Approval = approval, NowUtc = _clock.GetUtcNow() });
                if (request.ApprovalId is not null && approval is null) decision = PolicyDecision.DENIED;
            }
            catch { settings = new(); decision = PolicyDecision.DENIED; }
            await _journal.AppendAsync(Record(AuditEvent.Decision, decision, decision.ToString()), cancellationToken);
            if (decision != PolicyDecision.AUTO_ALLOWED || approval is null || binding is null)
                return Response(HelperResult.Denied, decision.ToString());
            var denial = await _journal.ReserveAsync(request, approval, _limits, _clock.GetUtcNow(), cancellationToken);
            if (denial is not null)
            {
                await _journal.AppendAsync(Record(AuditEvent.Decision, PolicyDecision.DENIED, denial), cancellationToken);
                return Response(HelperResult.Denied, denial);
            }
            // A kill switch changed during persistence must still prevent invocation.
            var current = _settings();
            if (current.KillSwitch || current.ReadOnly || current.Shadow || !current.ConfigurationAvailable)
            {
                await _journal.AppendAsync(Record(AuditEvent.Decision, PolicyDecision.DENIED, "disabled_before_execution"), cancellationToken);
                return Response(HelperResult.Denied, "disabled_before_execution");
            }
            await _journal.AppendAsync(Record(AuditEvent.ExecutionStarted, decision, "started"), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(settings.ExecutionTimeoutSeconds));
            HelperResult result; string code;
            executionStarted = true;
            try
            {
                // A synchronous Windows API may block before returning its ValueTask.
                // Bound the caller's wait; an interrupted attempt remains durably unresolved.
                await Task.Run(async () => await _executor.ExecuteAsync(request, binding, deadline.Token),
                    CancellationToken.None).WaitAsync(deadline.Token);
                result = HelperResult.Succeeded; code = "succeeded";
            }
            catch (OperationCanceledException) { result = HelperResult.Indeterminate; code = "execution_interrupted"; }
            catch { result = HelperResult.Failed; code = "execution_failed"; }
            // Preserve an outcome after caller cancellation, with a separate bounded audit deadline.
            using var auditDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _journal.AppendAsync(Record(AuditEvent.ExecutionEnded, decision, code), auditDeadline.Token);
            return Response(result, code);
        }
        catch (OperationCanceledException) { return Response(executionStarted ? HelperResult.Indeterminate : HelperResult.Denied, "cancelled"); }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException or ArgumentException)
        { return Response(HelperResult.Denied, "invalid_request"); }
        catch
        {
            _auditFailed = true;
            return Response(executionStarted ? HelperResult.Indeterminate : HelperResult.Denied, "audit_unavailable");
        }
        finally { if (entered) _gate.Release(); }
    }
}
