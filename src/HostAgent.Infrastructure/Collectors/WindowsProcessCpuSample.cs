using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HostAgent.Infrastructure.Collectors;

// Creation and CPU times must come from the same Windows process object, not
// from separate PID lookups that could straddle PID reuse.
internal static class WindowsProcessCpuSample
{
    internal static ProcessTimeSample Read(int pid, ICollection<string> warnings)
    {
        if (pid <= 0) return default;
        const uint queryLimitedInformation = 0x1000;
        using var handle = OpenProcess(queryLimitedInformation, false, pid);
        if (handle.IsInvalid)
            return Unavailable(pid, Marshal.GetLastWin32Error(), warnings);
        if (!GetProcessTimes(handle, out var creation, out var exit, out var kernel, out var user))
            return Unavailable(pid, Marshal.GetLastWin32Error(), warnings);
        return FromTimes(creation, exit, kernel, user);
    }

    internal static ProcessTimeSample FromTimes(long creation, long exit, long kernel, long user)
    {
        if (exit != 0) return new(null, null, true);
        if (creation <= 0 || kernel < 0 || user < 0 || kernel > long.MaxValue - user) return default;
        try { return new(DateTimeOffset.FromFileTime(creation).ToUniversalTime(), TimeSpan.FromTicks(kernel + user), false); }
        catch (ArgumentOutOfRangeException) { return default; }
    }

    private static ProcessTimeSample Unavailable(int pid, int nativeError, ICollection<string> warnings)
    {
        var error = new Win32Exception(nativeError);
        if (ProcessMetricFailure.Classify(error, pid) is { } warning) warnings.Add(warning);
        if (ProcessMetricFailure.ClassifyStage(error, pid, ProcessMetricReadStage.CpuTime) is { } detail)
            warnings.Add(detail);
        warnings.Add("some_process_start_times_unavailable");
        if (ProcessMetricFailure.ClassifyStage(error, pid, ProcessMetricReadStage.StartTime) is { } identity)
            warnings.Add(identity);
        return default;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
}

internal readonly record struct ProcessTimeSample(DateTimeOffset? StartedAt, TimeSpan? CpuTime, bool Exited);

internal sealed class ProcessCpuTracker
{
    private readonly Dictionary<int, Observation> previous = [];
    private readonly record struct Observation(DateTimeOffset Start, TimeSpan Cpu, DateTimeOffset At);

    internal double? Record(int pid, ProcessTimeSample sample, DateTimeOffset at, int processorCount)
    {
        lock (previous)
        {
            if (sample.Exited || sample.StartedAt is not { } start || start > at || sample.CpuTime is not { } cpu)
            {
                previous.Remove(pid);
                return null;
            }
            var hadPrevious = previous.TryGetValue(pid, out var old);
            previous[pid] = new(start, cpu, at);
            if (!hadPrevious || old.Start != start || at <= old.At || cpu < old.Cpu) return null;
            return Math.Clamp(100d * (cpu - old.Cpu).TotalSeconds /
                ((at - old.At).TotalSeconds * Math.Max(1, processorCount)), 0, 100);
        }
    }

    internal void Retain(HashSet<int> seen)
    {
        lock (previous)
            foreach (var pid in previous.Keys.Where(pid => !seen.Contains(pid)).ToArray())
                previous.Remove(pid);
    }
}
