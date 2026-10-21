using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Core.Models.Policy;
using HostAgent.Core.Models.Audit;
using HostAgent.Core.Models.Incidents;
using HostAgent.Core.Orchestration;
using HostAgent.Core.Policy;
using System.Text.Json;
namespace HostAgent.Service;
public sealed class RemediationCoordinator(IHostAgentStore store, IHelperClient helper, ServiceConfiguration configuration, TimeProvider clock,
    DiagnosticCycleProcessor? diagnostics=null):IDisposable
{
    private readonly SemaphoreSlim _gate = new(1,1);
    private readonly DiagnosticCycleProcessor _diagnostics=diagnostics??new(store);
    public void Dispose(){_gate.Dispose();if(diagnostics is null)_diagnostics.Dispose();}
    public async ValueTask<HelperOperationResponse> ProposeAsync(HelperOperationRequest request, OperationApproval? approval, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        var invoked=false;
        HelperOperationResponse Response(HelperResult result,string code)=>new(1,request.RequestId,result,code);
        try
        {
            request=HelperProtocol.ParseRequest(HelperProtocol.SerializeRequest(request));
            var incident=await store.ReadIncidentAsync(request.IncidentId,cancellationToken);
            if (incident is null || incident.LatestDiagnosis.DiagnosisId!=request.DiagnosisId ||
                incident.LatestDiagnosis.Confidence!=request.Confidence ||
                !incident.LatestDiagnosis.Evidence.Select(value=>value.EvidenceId).SequenceEqual(request.EvidenceReferences) ||
                incident.State is IncidentLifecycleState.Resolved or IncidentLifecycleState.Escalated)
                return Response(HelperResult.Denied,"incident_not_actionable");
            var now=clock.GetUtcNow();
            if (configuration.Mode==OperatingMode.Confirmation && !configuration.ReadOnly)
            {
                if (configuration.CollectionIntervalSeconds is <5 or >3600)
                    return Response(HelperResult.Denied,"configuration_invalid");
                // A fresh approval or lifecycle snapshot cannot make an old
                // diagnosis current. Allow two configured collection periods,
                // with three minutes as the minimum sampling allowance.
                var maximumAge=TimeSpan.FromSeconds(Math.Max(180,2*configuration.CollectionIntervalSeconds));
                if (incident.LatestDiagnosis.ObservedAtUtc>now || now-incident.LatestDiagnosis.ObservedAtUtc>maximumAge)
                    return Response(HelperResult.Denied,"incident_evidence_stale");
            }
            var auditToken=cancellationToken;
            var decision=new PolicyEngine().Evaluate(new()
            {
                ReadOnly=configuration.ReadOnly,Shadow=configuration.Mode==OperatingMode.Shadow,
                KillSwitch=configuration.KillSwitch,StorageHealthy=true,ConfigurationAvailable=configuration.ConfigurationAvailable,
                OperationEnabled=Enum.IsDefined(configuration.Mode),Confidence=request.Confidence,IncidentId=request.IncidentId,
                DiagnosisId=request.DiagnosisId,RequestId=request.RequestId,Operation=request.Operation,
                TargetKey=HelperProtocol.TargetKey(request),RequestDigest=HelperProtocol.Digest(request),NowUtc=now,Approval=approval
            });
            if (request.ApprovalId is not null && request.ApprovalId!=approval?.ApprovalId) decision=PolicyDecision.DENIED;
            var code=decision switch
            {
                PolicyDecision.READ_ONLY => configuration.Mode==OperatingMode.Shadow?"shadow_proposal":"read_only",
                PolicyDecision.CONFIRM_REQUIRED=>"confirmation_required",
                PolicyDecision.AUTO_ALLOWED=>"confirmed",
                _=>"policy_denied"
            };
            using var auditRequest=JsonDocument.Parse(HelperProtocol.SerializeRequest(request with
                {ApprovalId=decision==PolicyDecision.AUTO_ALLOWED?approval!.ApprovalId:request.ApprovalId}));
            async ValueTask Audit(string outcome,DateTimeOffset? started=null,DateTimeOffset? ended=null) =>
                await store.AppendAuditEntryAsync(new(Guid.NewGuid().ToString("N"),clock.GetUtcNow(),"HostAgent",request.IncidentId,
                    request.DiagnosisId,request.Confidence,request.EvidenceReferences,JsonSerializer.Serialize(new
                    {Request=auditRequest.RootElement,Approval=approval,RequestDigest=HelperProtocol.Digest(request),
                        TimeoutSeconds=configuration.RequestTimeoutSeconds}),decision,
                    approval is null?ApprovalStatus.Pending:decision==PolicyDecision.AUTO_ALLOWED?ApprovalStatus.Approved:ApprovalStatus.Denied,
                    started,ended,outcome,null),auditToken);
            await Audit(code);
            if (decision!=PolicyDecision.AUTO_ALLOWED) return Response(HelperResult.Denied,code);
            async ValueTask Transition(IncidentLifecycleState state)
            {
                var snapshot=incident.Snapshots[^1] with {SnapshotId=Guid.NewGuid().ToString("N"),CapturedAtUtc=clock.GetUtcNow()};
                incident=await _diagnostics.TransitionAsync(incident.IncidentId,state,snapshot,auditToken);
            }
            if (incident.State==IncidentLifecycleState.Detected) await Transition(IncidentLifecycleState.Active);
            if (incident.State is IncidentLifecycleState.Active or IncidentLifecycleState.Acknowledged or IncidentLifecycleState.Failed)
                await Transition(IncidentLifecycleState.RemediationProposed);
            if (incident.State==IncidentLifecycleState.RemediationProposed) await Transition(IncidentLifecycleState.RemediationPendingApproval);
            await Transition(IncidentLifecycleState.Remediating);
            var started=clock.GetUtcNow();
            await Audit("execution_requested",started);
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(configuration.RequestTimeoutSeconds));
            invoked=true;
            HelperOperationResponse response;
            try
            {
                response=await helper.ExecuteAsync(request with {ApprovalId=approval!.ApprovalId},deadline.Token)
                    .AsTask().WaitAsync(deadline.Token);
            }
            catch (OperationCanceledException) { response=Response(HelperResult.Indeterminate,"helper_interrupted"); }
            catch { response=Response(HelperResult.Indeterminate,"helper_unavailable_or_uncertain"); }
            using var completion=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            auditToken=completion.Token;
            await Audit(response.Code,started,clock.GetUtcNow());
            // Every failed/uncertain attempt requires human review. No service-side mutation retry.
            await Transition(response.Result==HelperResult.Succeeded?IncidentLifecycleState.Resolved:IncidentLifecycleState.Escalated);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !invoked) { throw; }
        catch { return Response(invoked?HelperResult.Indeterminate:HelperResult.Denied,"remediation_storage_or_validation_unavailable"); }
        finally { _gate.Release(); }
    }
}
