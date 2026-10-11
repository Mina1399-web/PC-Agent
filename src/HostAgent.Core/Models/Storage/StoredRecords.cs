namespace HostAgent.Core.Models.Storage;

public sealed record IncidentStorageRecord(
    string IncidentId,
    string CorrelationKey,
    string State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string PayloadJson);

public sealed record IncidentSnapshotStorageRecord(
    string SnapshotId,
    string IncidentId,
    DateTimeOffset CapturedAtUtc,
    string PayloadJson);

public sealed record DiagnosticEvidenceStorageRecord(
    string EvidenceId,
    string IncidentId,
    DateTimeOffset RecordedAtUtc,
    string EvidenceType,
    string PayloadJson);

public sealed record RemediationAttemptStorageRecord(
    string AttemptId,
    string IncidentId,
    DateTimeOffset RequestedAtUtc,
    string OperationId,
    string Status,
    string PayloadJson);

public sealed record ServiceHealthSnapshot(
    string ComponentId,
    DateTimeOffset ObservedAtUtc,
    string Status,
    string PayloadJson);
