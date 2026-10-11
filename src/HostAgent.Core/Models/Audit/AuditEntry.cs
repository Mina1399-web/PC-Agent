using HostAgent.Core.Models.Policy;

namespace HostAgent.Core.Models.Audit;

public sealed record AuditEntry(
    string AuditId,
    DateTimeOffset RecordedAtUtc,
    string Initiator,
    string? IncidentId,
    string Diagnosis,
    int Confidence,
    IReadOnlyList<string> EvidenceReferences,
    string RequestedAction,
    PolicyDecision PolicyDecision,
    ApprovalStatus ApprovalStatus,
    DateTimeOffset? ExecutionStartedAtUtc,
    DateTimeOffset? ExecutionEndedAtUtc,
    string? Result,
    string? Error);
