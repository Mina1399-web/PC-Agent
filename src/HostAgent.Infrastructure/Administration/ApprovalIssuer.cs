using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Storage;
using HostAgent.Infrastructure.Pipes;
namespace HostAgent.Infrastructure.Administration;
// Trusted local provisioning only: caller must hold access to the helper-owned journal.
public sealed class ApprovalIssuer(SqliteHelperJournal journal,string approverSid,TimeProvider clock)
{
    public async ValueTask<OperationApproval> CreateAsync(HelperOperationRequest request,ApprovedTarget displayedTarget,ApprovedTarget currentTarget,CancellationToken token)
    {
        request=HelperProtocol.ParseRequest(HelperProtocol.SerializeRequest(request));
        if(request.ApprovalId is not null||request.Confidence<80)throw new InvalidDataException("approval_request_not_actionable");
        if(displayedTarget is null||displayedTarget!=currentTarget)throw new InvalidDataException("approval_target_changed");
        var now=clock.GetUtcNow();
        var approval=new OperationApproval(Guid.NewGuid(),request.RequestId,request.IncidentId,request.DiagnosisId,
            request.Operation,HelperProtocol.Digest(request),approverSid,now,now.AddMinutes(5)){Target=displayedTarget};
        await journal.RecordApprovalAsync(approval,token);return approval;
    }
}
