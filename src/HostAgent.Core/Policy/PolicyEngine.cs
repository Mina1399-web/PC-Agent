using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Policy;

namespace HostAgent.Core.Policy;

public sealed class PolicyEngine : IPolicyEngine
{
    public PolicyDecision Evaluate(PolicyContext? context)
    {
        if (context is null) return PolicyDecision.DENIED;
        if (context.ReadOnly || context.Shadow) return PolicyDecision.READ_ONLY;
        if (context.KillSwitch || !context.StorageHealthy || !context.ConfigurationAvailable ||
            !context.OperationEnabled || context.Confidence is < 80 or > 100 ||
            context.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(context.IncidentId) ||
            string.IsNullOrWhiteSpace(context.DiagnosisId) || !Enum.IsDefined(context.Operation))
            return PolicyDecision.DENIED;
        if (context.Approval is not { } approval) return PolicyDecision.CONFIRM_REQUIRED;
        if (approval.ApprovalId == Guid.Empty || approval.RequestId != context.RequestId ||
            approval.IncidentId != context.IncidentId || approval.DiagnosisId != context.DiagnosisId ||
            approval.Operation != context.Operation || string.IsNullOrWhiteSpace(context.RequestDigest) ||
            approval.RequestDigest != context.RequestDigest || string.IsNullOrWhiteSpace(approval.ApproverSid) ||
            approval.ApprovedAtUtc > context.NowUtc || approval.ApprovedAtUtc < context.NowUtc.AddMinutes(-10) ||
            approval.ExpiresAtUtc <= context.NowUtc || approval.ExpiresAtUtc <= approval.ApprovedAtUtc ||
            approval.ExpiresAtUtc > approval.ApprovedAtUtc.AddMinutes(10))
            return PolicyDecision.DENIED;
        // Execution allowed only after explicit confirmation, never by autonomous inference.
        return PolicyDecision.AUTO_ALLOWED;
    }
}
