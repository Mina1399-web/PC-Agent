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
        if (!WindowsNativeTelemetry.GlobalMemoryStatusEx(ref memory))
        {
            return ValueTask.FromResult(CollectorResult<CpuMemoryTelemetry>.Unavailable(
                CollectorId,
                now,
                "global_memory_status_unavailable"));
        }

        var cpu = ReadSystemCpu(now, warnings);
        var processes = ReadProcessUsage(now, warnings, cancellationToken);
        var telemetry = new CpuMemoryTelemetry(
            cpu,
            checked((long)memory.TotalPhysical),
            checked((long)memory.AvailablePhysical),
            checked((long)memory.TotalPageFile),
            checked((long)memory.AvailablePageFile),
            processes.Where(static process => process.CpuUsagePercent.HasValue)
                .OrderByDescending(static process => process.CpuUsagePercent)
                .Take(TopProcessLimit)
                .ToArray(),
            processes.OrderByDescending(static process => process.MemoryBytes)
                .Take(TopProcessLimit)
                .ToArray());

        return ValueTask.FromResult(warnings.Count == 0
            ? CollectorResult<CpuMemoryTelemetry>.Success(CollectorId, now, telemetry)
            : CollectorResult<CpuMemoryTelemetry>.Partial(CollectorId, now, telemetry, warnings));
    }

    private double? ReadSystemCpu(DateTimeOffset now, ICollection<string> warnings)
    {
        if (!WindowsNativeTelemetry.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            warnings.Add("system_cpu_counter_unavailable");
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

            var totalDelta = unchecked((kernel.ToUInt64() - previous.KernelTicks)
                + (user.ToUInt64() - previous.UserTicks));
            var idleDelta = unchecked(idle.ToUInt64() - previous.IdleTicks);
            if (totalDelta == 0 || current.CapturedAtUtc <= previous.CapturedAtUtc)
            {
                return null;
            }

            return Math.Clamp(100d * (totalDelta - Math.Min(idleDelta, totalDelta)) / totalDelta, 0, 100);
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
                try
                {
                    var id = process.Id;
                    knownProcessId = id;
                    var name = process.ProcessName;
                    var memoryBytes = Math.Max(0, process.WorkingSet64);
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
