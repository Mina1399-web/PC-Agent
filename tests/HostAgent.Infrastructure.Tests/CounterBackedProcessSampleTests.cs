using System.ComponentModel;
using HostAgent.Infrastructure.Collectors;
using HostAgent.Core.Orchestration;
using HostAgent.Service;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class CounterBackedProcessSampleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static ProcessCounterSample Counter() => new(12345, TimeSpan.FromSeconds(79), 654321, 987654)
    { InstanceName = "counter-fixture:12345", CapturedAtUtc = Now };

    // A valid same-query performance row can recover an access-denied CPU
    // metric without granting process access or inventing exact UTC identity.
    [Fact]
    public void Valid_counter_recovers_denied_cpu_with_one_coherent_counter_row()
    {
        var source = new Sample { Error = 5 };
        using (var sample = new CounterBackedProcessSample(source, Counter(), new Clock()))
        {
            Assert.Equal(TimeSpan.FromSeconds(79), sample.TotalProcessorTime);
            Assert.True(sample.CounterCpuRecovered);
            Assert.Equal(654321, sample.WorkingSet64);
            Assert.Equal("counter-fixture:12345", sample.ProcessName);
            Assert.Null(sample.ExecutablePath);
            Assert.Equal(new ProcessCpuGeneration(ProcessCpuClock.PerformanceLifetime, 987654), sample.CpuGeneration);
            Assert.Throws<InvalidOperationException>(() => sample.StartTime);
        }
        Assert.True(source.Disposed);
    }

    [Fact]
    public void Available_native_metrics_keep_their_original_exact_identity_and_values()
    {
        using var sample = new CounterBackedProcessSample(new Sample(), Counter(), new Clock());
        Assert.Equal(TimeSpan.FromSeconds(2), sample.TotalProcessorTime);
        Assert.False(sample.CounterCpuRecovered);
        Assert.Equal(123456789, sample.WorkingSet64);
        Assert.Equal("native-fixture", sample.ProcessName);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), sample.StartTime);
        Assert.Equal(@"C:\fixture.exe", sample.ExecutablePath);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("stale")]
    [InlineData("future")]
    [InlineData("other_pid")]
    [InlineData("negative_cpu")]
    [InlineData("negative_memory")]
    [InlineData("missing_instance")]
    public void Untrusted_or_missing_counter_does_not_hide_access_denial(string defect)
    {
        ProcessCounterSample? counter = Counter();
        counter = defect switch
        {
            "absent" => null,
            "stale" => counter with { CapturedAtUtc = Now.AddSeconds(-16) },
            "future" => counter with { CapturedAtUtc = Now.AddSeconds(1) },
            "other_pid" => counter with { ProcessId = 4444 },
            "negative_cpu" => counter with { CpuTime = TimeSpan.FromTicks(-1) },
            "negative_memory" => counter with { MemoryBytes = -1 },
            "missing_instance" => counter with { InstanceName = "" },
            _ => throw new InvalidOperationException()
        };
        using var sample = new CounterBackedProcessSample(new Sample { Error = 5 }, counter, new Clock());
        Assert.Equal(5, Assert.Throws<Win32Exception>(() => sample.TotalProcessorTime).NativeErrorCode);
        Assert.False(sample.CounterCpuRecovered);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(87)]
    public void Other_native_errors_are_not_reclassified_as_recovered(int error)
    {
        using var sample = new CounterBackedProcessSample(new Sample { Error = error }, Counter(), new Clock());
        Assert.Equal(error, Assert.Throws<Win32Exception>(() => sample.TotalProcessorTime).NativeErrorCode);
        Assert.False(sample.CounterCpuRecovered);
    }

    [Fact]
    public async Task Actual_collector_preserves_counter_row_and_blocks_unverified_process_identity()
    {
        var collector = new WindowsProcessServicesCollector(
            () => [new CounterBackedProcessSample(new Sample { Error = 5 }, Counter(), new Clock())], () => []);
        var result = await collector.CollectAsync(default);
        var item = Assert.Single(result.Value!.Processes);
        Assert.Equal("counter-fixture:12345", item.ProcessName);
        Assert.Equal(654321, item.MemoryBytes);
        Assert.Null(item.StartedAtUtc);
        Assert.Null(item.ExecutablePath);
        Assert.Null(item.CpuUsagePercent); // first generation sample, never invented zero
        Assert.Equal(new[] { "process_identity_unavailable", "some_executable_paths_unavailable" }, result.Warnings);
        var health = new AgentRuntimeHealth();
        health.SetCollectors([new("process-services", CollectorCycleStatus.Partial, null, 0, null)
        { WarningCodes = result.Warnings }]);
        Assert.False(health.Degraded);
        Assert.Equal("optional_process_identity_unavailable", Assert.Single(health.Snapshot(), x => x.Component == "process-services").Code);
    }

    [Fact]
    public async Task Actual_cpu_collector_uses_counter_memory_after_denied_cpu_without_cross_source_mixing()
    {
        var collector = new WindowsCpuMemoryCollector(
            () => [new CounterBackedProcessSample(new Sample { Error = 5 }, Counter(), new Clock())]);
        var result = await collector.CollectAsync(default);
        var item = Assert.Single(result.Value!.TopProcessesByMemory);
        Assert.Equal("counter-fixture:12345", item.ProcessName);
        Assert.Equal(654321, item.MemoryBytes);
        Assert.Null(item.CpuUsagePercent);
        Assert.DoesNotContain("some_process_metrics_access_denied", result.Warnings);
        Assert.DoesNotContain("process_cpu_time_access_denied", result.Warnings);
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Sample : IWindowsProcessSample
    {
        internal int? Error { get; init; }
        internal bool Disposed { get; private set; }
        public int Id => 12345;
        public string ProcessName => "native-fixture";
        public long WorkingSet64 => 123456789;
        public TimeSpan TotalProcessorTime => Error is { } error ? throw new Win32Exception(error) : TimeSpan.FromSeconds(2);
        public DateTime StartTime => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public string ExecutablePath => @"C:\fixture.exe";
        public void Dispose() => Disposed = true;
    }
}
