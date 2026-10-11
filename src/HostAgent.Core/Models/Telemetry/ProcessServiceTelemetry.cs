namespace HostAgent.Core.Models.Telemetry;

public sealed record ProcessServiceTelemetry(
    IReadOnlyList<ProcessTelemetry> Processes,
    IReadOnlyList<ServiceTelemetry> Services,
    IReadOnlyList<ServiceStateChange> ServiceChanges);

public sealed record ProcessTelemetry(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    double? CpuUsagePercent,
    long MemoryBytes,
    DateTimeOffset? StartedAtUtc);

public enum HostServiceStatus
{
    Unknown,
    Stopped,
    StartPending,
    StopPending,
    Running,
    ContinuePending,
    PausePending,
    Paused
}

public sealed record ServiceTelemetry(
    string ServiceName,
    string DisplayName,
    HostServiceStatus Status);

public sealed record ServiceStateChange(
    string ServiceName,
    HostServiceStatus PreviousStatus,
    HostServiceStatus CurrentStatus,
    DateTimeOffset ObservedAtUtc);
