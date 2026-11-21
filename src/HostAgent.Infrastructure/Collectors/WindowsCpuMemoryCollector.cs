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
    private readonly Dictionary<int, ProcessCpuObservation> _previousProcesses = [];
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
        var processes = ReadProcessUsage(now, warnings, cancellationToken);
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
            processes.OrderByDescending(static process => process.MemoryBytes)
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
        DateTimeOffset now,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var observations = new List<(ProcessResourceUsage Usage, TimeSpan CpuTime)>();
        var seenProcessIds = new HashSet<int>();
        Process[] runningProcesses;
        try
        {
            runningProcesses = Process.GetProcesses();
        }
        catch (Win32Exception)
        {
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
                    stage=ProcessMetricReadStage.WorkingSet;
                    var memoryBytes = process.WorkingSet64;
                    if (memoryBytes < 0)
                    {
                        warnings.Add("some_process_metrics_unavailable");
                        continue;
                    }
                    stage=ProcessMetricReadStage.CpuTime;
                    var cpuTime = process.TotalProcessorTime;
                    var path = TryGetExecutablePath(process);
                    seenProcessIds.Add(id);
                    var cpuPercent = UpdateProcessCpu(id, cpuTime, now);
                    observations.Add((new ProcessResourceUsage(id, name, cpuPercent, memoryBytes, path), cpuTime));
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

        lock (_sync)
        {
            foreach (var staleId in _previousProcesses.Keys.Where(id => !seenProcessIds.Contains(id)).ToArray())
            {
                _previousProcesses.Remove(staleId);
            }
        }

        return observations.Select(static observation => observation.Usage).ToArray();
    }

    private double? UpdateProcessCpu(int processId, TimeSpan cpuTime, DateTimeOffset now)
    {
        lock (_sync)
        {
            var cpuPercent = (double?)null;
            if (_previousProcesses.TryGetValue(processId, out var previous))
            {
                var elapsed = now - previous.CapturedAtUtc;
                var cpuDelta = cpuTime - previous.CpuTime;
                if (elapsed > TimeSpan.Zero && cpuDelta >= TimeSpan.Zero)
                {
                    cpuPercent = Math.Clamp(
                        100d * cpuDelta.TotalSeconds / (elapsed.TotalSeconds * Math.Max(1, Environment.ProcessorCount)),
                        0,
                        100);
                }
            }

            _previousProcesses[processId] = new ProcessCpuObservation(cpuTime, now);
            return cpuPercent;
        }
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

    private sealed record ProcessCpuObservation(TimeSpan CpuTime, DateTimeOffset CapturedAtUtc);
}
