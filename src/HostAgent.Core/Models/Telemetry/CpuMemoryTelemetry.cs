namespace HostAgent.Core.Models.Telemetry;

public sealed record CpuMemoryTelemetry(
    double? CpuUsagePercent,
    long? TotalPhysicalMemoryBytes,
    long? AvailablePhysicalMemoryBytes,
    long? CommitLimitBytes,
    long? CommitAvailableBytes,
    IReadOnlyList<ProcessResourceUsage> TopProcessesByCpu,
    IReadOnlyList<ProcessResourceUsage> TopProcessesByMemory)
{
    public double? MemoryPressurePercent =>
        UsedPercent(TotalPhysicalMemoryBytes, AvailablePhysicalMemoryBytes);

    public double? CommitPressurePercent => UsedPercent(CommitLimitBytes, CommitAvailableBytes);

    private static double? UsedPercent(long? total, long? available) =>
        total is > 0 && available is >= 0 && available.Value <= total.Value
            ? 100d * (total.Value - available.Value) / total.Value
            : null;
}

public sealed record ProcessResourceUsage(
    int ProcessId,
    string ProcessName,
    double? CpuUsagePercent,
    long MemoryBytes,
    string? ExecutablePath);
