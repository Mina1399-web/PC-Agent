namespace HostAgent.Core.Models.Telemetry;

public sealed record DiskTelemetry(
    IReadOnlyList<DiskVolumeTelemetry> Volumes,
    DiskIoTelemetry? IoCounters,
    IReadOnlyList<DiskHealthTelemetry> Health);

public sealed record DiskVolumeTelemetry(
    string RootPath,
    string VolumeLabel,
    string FileSystem,
    long CapacityBytes,
    long UsedBytes,
    long FreeBytes,
    double UsedPercent);

public sealed record DiskIoTelemetry(
    bool IsAvailable,
    double? ReadBytesPerSecond,
    double? WriteBytesPerSecond,
    double? AverageLatencyMilliseconds,
    string? UnavailableReason);

public sealed record DiskHealthTelemetry(
    string DeviceId,
    string Status,
    string? FailureCode);
