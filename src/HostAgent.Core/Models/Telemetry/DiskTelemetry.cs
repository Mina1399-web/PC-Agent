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
    string? UnavailableReason)
{
    // Fixed PDH call metadata only: no counter paths, values or exception text.
    // Old schema-1 telemetry remains readable with an empty diagnostic list.
    public IReadOnlyList<DiskIoCounterDiagnostic> Diagnostics { get; init; } = [];
}

public enum DiskIoDiagnosticStage
{
    OpenQuery,
    AddCounter,
    FirstSample,
    SecondSample,
    FormatValue,
    CloseQuery
}

public enum DiskIoDiagnosticCounter { None, Read, Write, Latency }

public sealed record DiskIoCounterDiagnostic(
    DiskIoDiagnosticStage Stage,
    DiskIoDiagnosticCounter Counter,
    uint Status,
    uint? DataStatus)
{
    // Only meaningful for API-successful, valid/new formatted native values.
    public bool? ValueFinite { get; init; }
}

public sealed record DiskHealthTelemetry(
    string DeviceId,
    string Status,
    string? FailureCode);
