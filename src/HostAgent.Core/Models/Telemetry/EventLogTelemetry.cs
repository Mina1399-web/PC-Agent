namespace HostAgent.Core.Models.Telemetry;

public sealed record EventLogTelemetry(
    DateTimeOffset CollectedAtUtc,
    IReadOnlyList<EventLogEntryMetadata> Events,
    IReadOnlyList<CollectorCheckpoint> Checkpoints) : ICheckpointedTelemetry;

public sealed record EventLogEntryMetadata(
    string LogName,
    string Provider,
    long? RecordId,
    int EventId,
    string Level,
    DateTimeOffset? OccurredAtUtc,
    string MachineName)
{
    public string? AffectedServiceName { get; init; }
    public string? EventFingerprint { get; init; }
}
