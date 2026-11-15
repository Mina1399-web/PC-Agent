using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HostAgent.Core.Models.Audit;
using HostAgent.Core.Models.Policy;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Infrastructure.Security;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class GuidedServiceRecoveryTests
{
    [Fact]
    public async Task Preview_requires_fresh_corroborated_evidence_and_an_allowlisted_stopped_service()
    {
        var harness = new Harness();
        var preview = await harness.Workflow.PrepareAsync(harness.IncidentId, Harness.ServiceName, default);
        Assert.Equal(ServiceControllerStatus.Stopped, preview.StatusAtPreview);
        Assert.Equal(new[] { "eventlog.service-crash", "service.state_change" }, preview.Evidence.Select(item => item.Source));
        Assert.Equal("START SERVICE " + Harness.ServiceName + " WITH NO DEPENDENCIES", preview.ConfirmationPhrase);
        Assert.Equal(1, harness.ProposalCalls);

        harness = new Harness { MissingEvidence = true };
        var noEvidence = await Assert.ThrowsAsync<GuidedServiceRecoveryException>(async () =>
            await harness.Workflow.PrepareAsync(harness.IncidentId, Harness.ServiceName, default));
        Assert.Equal("service_crash_not_corroborated", noEvidence.Code);
        Assert.Equal(0, harness.ProposalCalls);

        harness = new Harness { AuthorityReadOnly = true };
        var readOnly = await Assert.ThrowsAsync<GuidedServiceRecoveryException>(async () =>
            await harness.Workflow.PrepareAsync(harness.IncidentId, Harness.ServiceName, default));
        Assert.Equal("approval_disabled", readOnly.Code);
        Assert.Equal(0, harness.ProposalCalls);
    }

    [Fact]
    public async Task Exact_service_action_phrase_is_required_and_request_id_alone_never_approves()
    {
        var harness = new Harness();
        var preview = await harness.Workflow.PrepareAsync(harness.IncidentId, Harness.ServiceName, default);
        var wrong = await Assert.ThrowsAsync<GuidedServiceRecoveryException>(async () =>
            await harness.Workflow.ExecuteAsync(preview, preview.Request.RequestId.ToString("D"), default));
        Assert.Equal("confirmation_phrase_mismatch", wrong.Code);
        Assert.Equal(0, harness.Authority.CreateApprovalCount);
        Assert.Equal(0, harness.ExecuteCalls);

        var caseMismatch = await Assert.ThrowsAsync<GuidedServiceRecoveryException>(async () =>
            await harness.Workflow.ExecuteAsync(preview, preview.ConfirmationPhrase.ToLowerInvariant(), default));
        Assert.Equal("confirmation_phrase_mismatch", caseMismatch.Code);
        Assert.Equal(0, harness.ExecuteCalls);
    }

    [Fact]
    public async Task Success_requires_correlated_helper_receipt_audit_and_fresh_running_readback()
    {
        var harness = new Harness();
        var preview = await harness.Workflow.PrepareAsync(harness.IncidentId, Harness.ServiceName, default);
        var receipt = await harness.Workflow.ExecuteAsync(preview, preview.ConfirmationPhrase, default);
        Assert.Equal(GuidedServiceRecoveryOutcome.Succeeded, receipt.Outcome);
        Assert.Equal("Succeeded", receipt.HelperResult);
        Assert.Equal("succeeded", receipt.HelperCode);
        Assert.True(receipt.AuditRecorded);
        Assert.Equal(ServiceControllerStatus.Running, receipt.ServiceReadback);
        Assert.NotNull(receipt.ServiceReadbackAtUtc);
        Assert.Equal(1, harness.ExecuteCalls);
    }

    [Fact]
    public async Task Missing_execution_audit_keeps_result_unknown_even_when_helper_and_scm_say_running()
    {
        var harness = new Harness { OmitAudit = true };
        var preview = await harness.Workflow.PrepareAsync(harness.IncidentId, Harness.ServiceName, default);
        var receipt = await harness.Workflow.ExecuteAsync(preview, preview.ConfirmationPhrase, default);
        Assert.Equal(GuidedServiceRecoveryOutcome.Unknown, receipt.Outcome);
        Assert.Equal("helper_audit_unverified", receipt.Code);
        Assert.False(receipt.AuditRecorded);
        Assert.Equal(ServiceControllerStatus.Running, receipt.ServiceReadback);
    }

    [Fact]
    public async Task Helper_success_without_running_readback_remains_unknown()
    {
        var harness = new Harness { LeaveStopped = true };
        var preview = await harness.Workflow.PrepareAsync(harness.IncidentId, Harness.ServiceName, default);
        var receipt = await harness.Workflow.ExecuteAsync(preview, preview.ConfirmationPhrase, default);
        Assert.Equal(GuidedServiceRecoveryOutcome.Unknown, receipt.Outcome);
        Assert.Equal("service_running_not_verified", receipt.Code);
        Assert.True(receipt.AuditRecorded);
        Assert.Equal(ServiceControllerStatus.Stopped, receipt.ServiceReadback);
    }

    [Fact]
    public async Task Changed_incident_after_approval_blocks_execute()
    {
        var harness = new Harness();
        var preview = await harness.Workflow.PrepareAsync(harness.IncidentId, Harness.ServiceName, default);
        harness.Authority.AfterApproval = () => harness.DiagnosisId = "changed-diagnosis";
        var stale = await Assert.ThrowsAsync<GuidedServiceRecoveryException>(async () =>
            await harness.Workflow.ExecuteAsync(preview, preview.ConfirmationPhrase, default));
        Assert.Equal("service_recovery_review_stale", stale.Code);
        Assert.Equal(0, harness.ExecuteCalls);
    }

    [Fact]
    public async Task Dependency_scope_is_displayed_and_bound_to_the_approved_request()
    {
        var harness = new Harness { DependenciesOverride = [new("ReviewDependency", ServiceControllerStatus.Running, DateTimeOffset.UtcNow)] };
        var preview = await harness.Workflow.PrepareAsync(harness.IncidentId, Harness.ServiceName, default);
        Assert.Equal("review-dependency", preview.DependenciesAtPreview.Single().ServiceId);
        Assert.Equal("ReviewDependency", preview.DependenciesAtPreview.Single().ServiceName);
        Assert.Contains("WITH DEPENDENCIES ReviewDependency", preview.ConfirmationPhrase, StringComparison.Ordinal);
        var parameters = Assert.IsType<ServiceParameters>(preview.Request.Parameters);
        Assert.Equal(new[] { "review-dependency" }, parameters.ExpectedDependencyIds);
    }

    [Fact]
    public async Task Unregistered_dependency_is_rejected_before_a_helper_proposal()
    {
        var harness = new Harness
        {
            DependenciesOverride = [new("UnregisteredDependency", ServiceControllerStatus.Stopped, DateTimeOffset.UtcNow)],
            AuthorityServices = [new(Harness.ServiceId, Harness.ServiceName, [HelperOperation.StartService])]
        };
        var error = await Assert.ThrowsAsync<GuidedServiceRecoveryException>(async () =>
            await harness.Workflow.PrepareAsync(harness.IncidentId, Harness.ServiceName, default));
        Assert.Equal("service_dependency_not_registered", error.Code);
        Assert.Equal(0, harness.ProposalCalls);
    }

    [Fact]
    public async Task Changed_dependency_status_after_approval_blocks_execute()
    {
        var harness = new Harness { DependenciesOverride = [new("ReviewDependency", ServiceControllerStatus.Running, DateTimeOffset.UtcNow)] };
        var preview = await harness.Workflow.PrepareAsync(harness.IncidentId, Harness.ServiceName, default);
        harness.Authority.AfterApproval = () => harness.Authority.DependenciesOverride =
            [new("ReviewDependency", ServiceControllerStatus.Stopped, DateTimeOffset.UtcNow)];
        var error = await Assert.ThrowsAsync<GuidedServiceRecoveryException>(async () =>
            await harness.Workflow.ExecuteAsync(preview, preview.ConfirmationPhrase, default));
        Assert.Equal("service_recovery_review_stale", error.Code);
        Assert.Equal(0, harness.ExecuteCalls);
    }

    [Fact]
    public async Task Unsupported_target_and_control_unavailable_fail_closed()
    {
        var unsupported = new Harness { AuthorityServices = [] };
        var error = await Assert.ThrowsAsync<GuidedServiceRecoveryException>(async () =>
            await unsupported.Workflow.PrepareAsync(unsupported.IncidentId, Harness.ServiceName, default));
        Assert.Equal("target_not_registered", error.Code);
        Assert.Equal(0, unsupported.ProposalCalls);

        var unavailable = new Harness { ControlUnavailable = true };
        var preview = await unavailable.Workflow.PrepareAsync(unavailable.IncidentId, Harness.ServiceName, default);
        var receipt = await unavailable.Workflow.ExecuteAsync(preview, preview.ConfirmationPhrase, default);
        Assert.Equal(GuidedServiceRecoveryOutcome.Rejected, receipt.Outcome);
        Assert.Equal("control_unavailable", receipt.Code);
        Assert.Equal(1, unavailable.ExecuteCalls);
        Assert.False(receipt.AuditRecorded);
    }

    [Fact]
    public async Task Lost_transport_reply_remains_unknown_without_retry_even_if_audit_and_status_show_success()
    {
        var harness = new Harness { LoseReplyAfterExecution = true };
        var preview = await harness.Workflow.PrepareAsync(harness.IncidentId, Harness.ServiceName, default);
        var receipt = await harness.Workflow.ExecuteAsync(preview, preview.ConfirmationPhrase, default);
        Assert.Equal(GuidedServiceRecoveryOutcome.Unknown, receipt.Outcome);
        Assert.Equal("execute_outcome_uncertain", receipt.Code);
        Assert.True(receipt.AuditRecorded);
        Assert.Equal(ServiceControllerStatus.Running, receipt.ServiceReadback);
        Assert.Equal(1, harness.ExecuteCalls);
    }

    private sealed class Harness
    {
        public const string ServiceName = "ReviewTestService";
        public const string ServiceId = "review-test-service";
        public const string Approver = "S-1-5-21-111-222-333-1001";
        public readonly string IncidentId = Guid.NewGuid().ToString("N");
        public string DiagnosisId = "diagnosis-review-test";
        public readonly DateTimeOffset ObservedAt = DateTimeOffset.UtcNow;
        public int ProposalCalls;
        public int ExecuteCalls;
        public bool MissingEvidence;
        public bool AuthorityReadOnly;
        public bool OmitAudit;
        public bool LeaveStopped;
        public bool ControlUnavailable;
        public bool LoseReplyAfterExecution;
        public ServiceRegistration[]? AuthorityServices;
        public ServiceDependencySnapshot[]? DependenciesOverride;
        public readonly FakeAuthority Authority = new();
        public GuidedServiceRecoveryWorkflow Workflow
        {
            get
            {
                Authority.ReadOnlyOverride = AuthorityReadOnly;
                Authority.DependenciesOverride = DependenciesOverride ?? [];
                Authority.ServicesOverride = AuthorityServices ?? BuildServices(DependenciesOverride ?? []);
                return new(SendAsync, Authority, readbackTimeout: TimeSpan.FromMilliseconds(35),
                    readbackInterval: TimeSpan.FromMilliseconds(5));
            }
        }

        public Harness() => Authority.AfterApproval = null;

        private static ServiceRegistration[] BuildServices(ServiceDependencySnapshot[] dependencies)
        {
            var services = new List<ServiceRegistration> { new(ServiceId, ServiceName, [HelperOperation.StartService]) };
            foreach (var dependency in dependencies)
                services.Add(new("review-dependency", dependency.ServiceName, [HelperOperation.StartService]));
            return services.ToArray();
        }

        private ValueTask<byte[]> SendAsync(byte[] payload, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var message = AdminProtocol.Parse(payload);
            if (message.Command == "incident")
            {
                var evidence = MissingEvidence
                    ? new[] { new { source = "eventlog.service-crash", observedAtUtc = ObservedAt } }
                    : new[]
                    {
                        new { source = "eventlog.service-crash", observedAtUtc = ObservedAt },
                        new { source = "service.state_change", observedAtUtc = ObservedAt.AddSeconds(-1) }
                    };
                return ValueTask.FromResult(AdminProtocol.Reply(true, "incident", new
                {
                    incidentId = IncidentId,
                    state = "Active",
                    diagnosisId = DiagnosisId,
                    confidence = 92,
                    observedAtUtc = ObservedAt,
                    rootCause = new { code = "service_crash", subject = ServiceName },
                    evidence
                }));
            }
            if (message.Command == "propose")
            {
                ProposalCalls++;
                var request = AdminProtocol.Proposal(message.Body, DiagnosisId, 92, ["evidence-crash", "evidence-state"]);
                using var requestDocument = JsonDocument.Parse(HelperProtocol.SerializeRequest(request));
                return ValueTask.FromResult(AdminProtocol.Reply(true, "confirmation_required", requestDocument.RootElement));
            }
            if (message.Command == "execute")
            {
                ExecuteCalls++;
                var execution = AdminProtocol.Execution(message.Body);
                if (ControlUnavailable) return ValueTask.FromResult(AdminProtocol.Reply(false, "control_unavailable"));
                if (!OmitAudit) Authority.SetVerifiedAudit(execution.Request, execution.Approval, "succeeded");
                if (!LeaveStopped) Authority.Status = ServiceControllerStatus.Running;
                if (LoseReplyAfterExecution) throw new IOException("simulated lost reply after dispatch");
                return ValueTask.FromResult(AdminProtocol.Reply(true, "succeeded", new
                {
                    result = "Succeeded",
                    RequestId = execution.Request.RequestId
                }));
            }
            throw new InvalidDataException("unexpected_admin_command");
        }

        public sealed class FakeAuthority : IGuidedServiceRecoveryAuthority
        {
            private IReadOnlyList<AuditRecord> _audit = [];
            public string ApproverSid => Approver;
            public ServiceControllerStatus Status = ServiceControllerStatus.Stopped;
            public int CreateApprovalCount;
            public Action? AfterApproval;
            public ServiceRegistration[]? ServicesOverride;
            public ServiceDependencySnapshot[] DependenciesOverride = [];
            public bool ReadOnlyOverride;

            public ValueTask<ServiceControllerStatus> ReadServiceStatusAsync(string serviceName, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (serviceName != ServiceName) throw new InvalidOperationException();
                return ValueTask.FromResult(Status);
            }
            public ValueTask<IReadOnlyList<ServiceDependencySnapshot>> ReadServiceDependenciesAsync(string serviceName, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (serviceName != ServiceName) throw new InvalidOperationException();
                return ValueTask.FromResult<IReadOnlyList<ServiceDependencySnapshot>>(DependenciesOverride);
            }

            public HelperConfiguration LoadConfiguration() => new()
            {
                ReadOnly = ReadOnlyOverride,
                Shadow = false,
                KillSwitch = false,
                ConfigurationAvailable = true,
                ApproverSids = [Approver],
                Services = ServicesOverride ?? [new(ServiceId, ServiceName, [HelperOperation.StartService])]
            };

            public bool RemediationDisabled => false;
            public ValueTask<IReadOnlyList<AuditRecord>> ReadAuditAsync(Guid requestId, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult<IReadOnlyList<AuditRecord>>(_audit.Where(item => item.RequestId == requestId).ToArray());
            }

            public ValueTask<OperationApproval> CreateApprovalAsync(HelperOperationRequest request,
                ApprovedTarget displayedTarget, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                CreateApprovalCount++;
                var now = DateTimeOffset.UtcNow;
                var approval = new OperationApproval(Guid.NewGuid(), request.RequestId, request.IncidentId,
                    request.DiagnosisId, request.Operation, HelperProtocol.Digest(request), Approver, now,
                    now.AddMinutes(5)) { Target = displayedTarget };
                AfterApproval?.Invoke();
                return ValueTask.FromResult(approval);
            }

            public void SetVerifiedAudit(HelperOperationRequest request, OperationApproval approval, string terminalCode)
            {
                var now = DateTimeOffset.UtcNow;
                _audit =
                [
                    new AuditRecord(Guid.NewGuid(), now, AuditEvent.Approval, request.RequestId, request.IncidentId,
                        request.DiagnosisId, 0, [], request.Operation, approval.RequestDigest, Approver,
                        PolicyDecision.CONFIRM_REQUIRED, approval.ApprovalId, Approver, approval.ApprovedAtUtc, "approved"),
                    new AuditRecord(Guid.NewGuid(), now, AuditEvent.ExecutionEnded, request.RequestId, request.IncidentId,
                        request.DiagnosisId, request.Confidence, request.EvidenceReferences, request.Operation,
                        HelperProtocol.Digest(request), Approver, PolicyDecision.AUTO_ALLOWED, approval.ApprovalId,
                        Approver, approval.ApprovedAtUtc, terminalCode)
                ];
            }
        }
    }
}
