using HostAgent.Core.Models.Policy;
using HostAgent.Core.Models.Remediation;
namespace HostAgent.Core.Models.Audit;
public enum AuditEvent { Proposal, Decision, Approval, ExecutionStarted, ExecutionEnded }
public sealed record AuditRecord(Guid AuditId, DateTimeOffset RecordedAtUtc, AuditEvent Event,
    Guid RequestId, string IncidentId, string DiagnosisId, int Confidence, string[] EvidenceReferences,
    HelperOperation Operation, string RequestDigest, string InitiatorSid, PolicyDecision Decision,
    Guid? ApprovalId, string? ApproverSid, DateTimeOffset? ApprovedAtUtc, string Code)
{
    public string ParametersJson { get; init; } = "{}";
    public int TimeoutSeconds { get; init; }
}
