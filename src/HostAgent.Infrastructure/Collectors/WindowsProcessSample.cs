using System.Diagnostics;
using System.ComponentModel;

namespace HostAgent.Infrastructure.Collectors;

internal enum ProcessCpuClock { NativeCreationTime, PerformanceLifetime }
internal readonly record struct ProcessCpuGeneration(ProcessCpuClock Clock, long Marker);

// One read-only OS process sample. Keep the field boundary separate so an
// unavailable metric need not discard unrelated readable fields.
internal interface IWindowsProcessSample : IDisposable
{
    int Id { get; }
    string ProcessName { get; }
    long WorkingSet64 { get; }
    TimeSpan TotalProcessorTime { get; }
    DateTime StartTime { get; }
    string? ExecutablePath { get; }
    ProcessCpuGeneration CpuGeneration => new(ProcessCpuClock.NativeCreationTime,
        StartTime.ToUniversalTime().ToFileTimeUtc());
    bool CounterCpuRecovered => false;
    DateTimeOffset? CpuCapturedAtUtc => null;
    bool HasExited => false;
}

internal sealed class WindowsProcessSample(Process process) : IWindowsProcessSample
{
    private DateTimeOffset? _cpuCapturedAtUtc;
    public int Id => process.Id;
    public string ProcessName => process.ProcessName;
    public long WorkingSet64 => process.WorkingSet64;
    public TimeSpan TotalProcessorTime
    {
        get
        {
            var value = process.TotalProcessorTime;
            _cpuCapturedAtUtc = DateTimeOffset.UtcNow;
            return value;
        }
    }
    public DateTimeOffset? CpuCapturedAtUtc => _cpuCapturedAtUtc;
    public bool HasExited
    {
        get
        {
            try { return process.HasExited; }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException)
            { return false; } // Unknown/denied identity is never proof of exit.
        }
    }
    public DateTime StartTime => process.StartTime;
    public string? ExecutablePath => process.MainModule?.FileName;
    public void Dispose() => process.Dispose();

    internal static IWindowsProcessSample[] ReadAll(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processes = Process.GetProcesses();
        try
        {
            var counters = new WindowsProcessCounterReader().Read(cancellationToken);
            return processes.Select(process => (IWindowsProcessSample)new CounterBackedProcessSample(
                new WindowsProcessSample(process), counters.GetValueOrDefault(process.Id))).ToArray();
        }
        catch
        {
            foreach (var process in processes) process.Dispose();
            throw;
        }
    }
}

internal sealed class CounterBackedProcessSample : IWindowsProcessSample
{
    private readonly IWindowsProcessSample _source;
    private readonly ProcessCounterSample? _counter;
    private readonly TimeProvider _clock;
    private bool _recovered;
    internal CounterBackedProcessSample(IWindowsProcessSample source, ProcessCounterSample? counter, TimeProvider? clock = null)
    {
        _source = source;
        _counter = counter;
        _clock = clock ?? TimeProvider.System;
    }
    public int Id => _source.Id;
    public string ProcessName => _recovered ? _counter!.InstanceName : _source.ProcessName;
    public long WorkingSet64 => _recovered ? _counter!.MemoryBytes : _source.WorkingSet64;
    public TimeSpan TotalProcessorTime
    {
        get
        {
            if (_recovered) return _counter!.CpuTime;
            try { return _source.TotalProcessorTime; }
            catch (Win32Exception error) when (error.NativeErrorCode == 5 && CounterIsUsable())
            {
                _recovered = true;
                return _counter!.CpuTime;
            }
        }
    }
    // A performance-clock lifetime is useful for CPU deltas, never an exact
    // UTC creation timestamp for authorizing a process operation.
    public DateTime StartTime => _recovered
        ? throw new InvalidOperationException("counter_identity_is_not_utc") : _source.StartTime;
    public string? ExecutablePath => _recovered ? null : _source.ExecutablePath;
    public ProcessCpuGeneration CpuGeneration => _recovered
        ? new(ProcessCpuClock.PerformanceLifetime, _counter!.LifetimeMarker) : _source.CpuGeneration;
    public bool CounterCpuRecovered => _recovered;
    public DateTimeOffset? CpuCapturedAtUtc => _recovered ? _counter!.CapturedAtUtc : _source.CpuCapturedAtUtc;
    public bool HasExited => _source.HasExited;
    public void Dispose() => _source.Dispose();

    private bool CounterIsUsable()
    {
        var now = _clock.GetUtcNow();
        return _counter is { ProcessId: > 0, MemoryBytes: >= 0, LifetimeMarker: >= 0 }
            && _counter.ProcessId == _source.Id && _counter.CpuTime >= TimeSpan.Zero
            && !string.IsNullOrWhiteSpace(_counter.InstanceName) && _counter.InstanceName.Length <= 256
            && _counter.CapturedAtUtc <= now && now - _counter.CapturedAtUtc <= TimeSpan.FromSeconds(15);
    }
}
