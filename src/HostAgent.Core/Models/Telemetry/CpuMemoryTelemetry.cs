namespace HostAgent.Core.Models.Telemetry;

public sealed record CpuMemoryTelemetry(
    double? CpuUsagePercent,
    long TotalPhysicalMemoryBytes,
    long AvailablePhysicalMemoryBytes,
    long? CommitLimitBytes,
    long? CommitAvailableBytes,
    IReadOnlyList<ProcessResourceUsage> TopProcessesByCpu,
    IReadOnlyList<ProcessResourceUsage> TopProcessesByMemory)
{
    public double MemoryPressurePercent =>
        TotalPhysicalMemoryBytes <= 0
            ? 0
            : 100d * (TotalPhysicalMemoryBytes - AvailablePhysicalMemoryBytes) / TotalPhysicalMemoryBytes;
}

public sealed record ProcessResourceUsage(
    int ProcessId,
    string ProcessName,
    double? CpuUsagePercent,
    long MemoryBytes,
    string? ExecutablePath);
