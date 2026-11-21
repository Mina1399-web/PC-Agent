using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Infrastructure.Collectors;

public sealed class WindowsCpuMemoryCollector : ITelemetryCollector<CpuMemoryTelemetry>
{
    public const int TopProcessLimit = 10;
    private readonly object _sync = new();
    private readonly ProcessCpuTracker _processCpu = new();
    private CpuTimeObservation? _previousSystemCpu;

    public string CollectorId => "cpu-memory";

    public ValueTask<CollectorResult<CpuMemoryTelemetry>> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var warnings = new List<string>();

        var memory = WindowsNativeTelemetry.MemoryStatusEx.Create();
        var memoryRead = WindowsNativeTelemetry.GlobalMemoryStatusEx(ref memory);
        if (!memoryRead)
        {
            warnings.Add(Marshal.GetLastWin32Error() == 5
                ? "global_memory_status_access_denied" : "global_memory_status_unavailable");
        }

        var cpu = ReadSystemCpu(now, warnings);
        var processes = ReadProcessUsage(warnings, cancellationToken);
        var telemetry = new CpuMemoryTelemetry(
            cpu,
            memoryRead ? AsBytes(memory.TotalPhysical) : null,
            memoryRead ? AsBytes(memory.AvailablePhysical) : null,
            memoryRead ? AsBytes(memory.TotalPageFile) : null,
            memoryRead ? AsBytes(memory.AvailablePageFile) : null,
            processes.Where(static process => process.CpuUsagePercent.HasValue)
                .OrderByDescending(static process => process.CpuUsagePercent)
                .Take(TopProcessLimit)
                .ToArray(),
            processes.Where(static process => process.MemoryBytes.HasValue)
                .OrderByDescending(static process => process.MemoryBytes)
                .Take(TopProcessLimit)
                .ToArray());

        if (memoryRead && telemetry.MemoryPressurePercent is null) warnings.Add("physical_memory_invalid");
        if (memoryRead && telemetry.CommitPressurePercent is null) warnings.Add("commit_memory_invalid");

        return ValueTask.FromResult(warnings.Count == 0
            ? CollectorResult<CpuMemoryTelemetry>.Success(CollectorId, now, telemetry)
            : CollectorResult<CpuMemoryTelemetry>.Partial(CollectorId, now, telemetry, warnings));
    }

    private static long? AsBytes(ulong value) => value <= long.MaxValue ? (long)value : null;

    private double? ReadSystemCpu(DateTimeOffset now, ICollection<string> warnings)
    {
        if (!WindowsNativeTelemetry.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            warnings.Add(Marshal.GetLastWin32Error() == 5
                ? "system_cpu_counter_access_denied" : "system_cpu_counter_unavailable");
            lock (_sync) _previousSystemCpu = null;
            return null;
        }

        var current = new CpuTimeObservation(
            idle.ToUInt64(),
            kernel.ToUInt64(),
            user.ToUInt64(),
            now);

        lock (_sync)
        {
            var previous = _previousSystemCpu;
            _previousSystemCpu = current;
            if (previous is null)
            {
                warnings.Add("cpu_baseline_not_ready");
                return null;
            }

            if (current.CapturedAtUtc <= previous.CapturedAtUtc || current.KernelTicks < previous.KernelTicks
                || current.UserTicks < previous.UserTicks || current.IdleTicks < previous.IdleTicks)
            {
                warnings.Add("system_cpu_counter_invalid");
                return null;
            }

            var kernelDelta = current.KernelTicks - previous.KernelTicks;
            var userDelta = current.UserTicks - previous.UserTicks;
            var idleDelta = current.IdleTicks - previous.IdleTicks;
            // Windows includes idle in kernel time. Inconsistent deltas are not idle CPU.
            if (idleDelta > kernelDelta || (kernelDelta == 0 && userDelta == 0))
            {
                warnings.Add("system_cpu_counter_invalid");
                return null;
            }

            return 100d * ((double)(kernelDelta - idleDelta) + userDelta) / ((double)kernelDelta + userDelta);
        }
    }

    private IReadOnlyList<ProcessResourceUsage> ReadProcessUsage(
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var observations = new List<ProcessResourceUsage>();
        var seenProcessIds = new HashSet<int>();
        Process[] runningProcesses;
        try
        {
            runningProcesses = Process.GetProcesses();
        }
        catch (Win32Exception)
        {
            _processCpu.Retain(seenProcessIds);
            warnings.Add("process_list_unavailable");
            return Array.Empty<ProcessResourceUsage>();
        }

        foreach (var process in runningProcesses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (process)
            {
                int? knownProcessId = null;
                var stage=ProcessMetricReadStage.Identity;
                try
                {
                    var id = process.Id;
                    knownProcessId = id;
                    stage=ProcessMetricReadStage.Name;
                    var name = process.ProcessName;
                    if (id <= 0) continue;
                    var sample = WindowsProcessCpuSample.Read(id, warnings);
                    var cpuPercent = _processCpu.Record(id, sample, DateTimeOffset.UtcNow, Environment.ProcessorCount);
                    if (sample.Exited) continue;
                    seenProcessIds.Add(id);
                    var memory = ProcessMetricFailure.WorkingSet(process, id, warnings);
                    if (memory is null && cpuPercent is null) continue;
                    var path = TryGetExecutablePath(process);
                    observations.Add(new(id, name, cpuPercent, memory, path));
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    if (ProcessMetricFailure.Classify(exception, knownProcessId) is { } warning)
                        warnings.Add(warning);
                    if (ProcessMetricFailure.ClassifyStage(exception,knownProcessId,stage) is { } detail)
                        warnings.Add(detail);
                }
            }
        }

        _processCpu.Retain(seenProcessIds);
        return observations;
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record CpuTimeObservation(
        ulong IdleTicks,
        ulong KernelTicks,
        ulong UserTicks,
        DateTimeOffset CapturedAtUtc);

}
