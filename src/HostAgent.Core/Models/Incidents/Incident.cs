using HostAgent.Core.Models.Diagnosis;
using HostAgent.Core.Models.Baseline;
using HostAgent.Core.Models.Telemetry;
using DiagnosisModel = HostAgent.Core.Models.Diagnosis.Diagnosis;

namespace HostAgent.Core.Models.Incidents;

public sealed record IncidentHistoryCursor(long CreatedAtUtcTicks, string IncidentId)
{
    public const int PageSize = 20;

    public bool IsValid() => CreatedAtUtcTicks > 0 && CreatedAtUtcTicks <= DateTimeOffset.MaxValue.UtcTicks
        && IncidentId is { Length: 32 }
        && IncidentId.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
        && Guid.TryParseExact(IncidentId, "N", out var id) && id != Guid.Empty;
}

public enum IncidentLifecycleState
{
    Detected,
    Active,
    Acknowledged,
    RemediationProposed,
    RemediationPendingApproval,
    Remediating,
    Resolved,
    Failed,
    Escalated
}

public sealed record Incident(
    string IncidentId,
    string CorrelationKey,
    IncidentLifecycleState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int RecurrenceCount,
    DiagnosisModel LatestDiagnosis,
    IReadOnlyList<IncidentSnapshot> Snapshots)
{
    // Optional for existing schema-3 JSON; old incidents fall back to CreatedAtUtc until reopening.
    public DateTimeOffset? OccurrenceStartedAtUtc { get; init; }
}

public sealed record IncidentSnapshot(
    string SnapshotId,
    string IncidentId,
    DateTimeOffset CapturedAtUtc,
    SystemTelemetry? System,
    CpuMemoryTelemetry? CpuMemory,
    DiskTelemetry? Disk,
    ProcessServiceTelemetry? ProcessServices,
    NetworkTelemetry? Network,
    EventLogTelemetry? EventLog,
    IReadOnlyList<BaselineAssessment> Baselines,
    IReadOnlyList<DiagnosticEvidence> DiagnosticEvidence);
