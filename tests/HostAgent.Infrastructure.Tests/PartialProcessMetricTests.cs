using System.ComponentModel;
using HostAgent.Infrastructure.Collectors;
using HostAgent.Core.Models.Telemetry;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class PartialProcessMetricTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exit_after_cpu_read_drops_the_old_baseline_before_the_next_observation(bool serviceCollector)
    {
        var gone = true;
        IWindowsProcessSample[] Read() => [new Sample { MemoryDenied = gone, Gone = gone }];
        if (serviceCollector)
        {
            var collector = new WindowsProcessServicesCollector(Read, () => []);
            await collector.CollectAsync(default);
            gone = false;
            var result = await collector.CollectAsync(default);
            Assert.Null(Assert.Single(result.Value!.Processes).CpuUsagePercent);
        }
        else
        {
            var collector = new WindowsCpuMemoryCollector(Read);
            await collector.CollectAsync(default);
            gone = false;
            var result = await collector.CollectAsync(default);
            Assert.Null(Assert.Single(result.Value!.TopProcessesByMemory).CpuUsagePercent);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rates_use_the_metric_observation_time_instead_of_pre_query_wall_time(bool serviceCollector)
    {
        var at = new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
        var time = TimeSpan.FromSeconds(10);
        IWindowsProcessSample[] Read() => [new Sample { CpuValue = time, CpuObservedAt = at }];
        if (serviceCollector)
        {
            var collector = new WindowsProcessServicesCollector(Read, () => []);
            await collector.CollectAsync(default);
            at = at.AddSeconds(2); time += TimeSpan.FromSeconds(Environment.ProcessorCount);
            var result = await collector.CollectAsync(default);
            Assert.Equal(50d, Assert.Single(result.Value!.Processes).CpuUsagePercent);
        }
        else
        {
            var collector = new WindowsCpuMemoryCollector(Read);
            await collector.CollectAsync(default);
            at = at.AddSeconds(2); time += TimeSpan.FromSeconds(Environment.ProcessorCount);
            var result = await collector.CollectAsync(default);
            Assert.Equal(50d, Assert.Single(result.Value!.TopProcessesByMemory).CpuUsagePercent);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_generation_identity_is_not_misreported_as_a_cpu_time_denial(bool serviceCollector)
    {
        var process = new Sample { StartDenied = true };
        if (serviceCollector)
        {
            var result = await new WindowsProcessServicesCollector(() => [process], () => []).CollectAsync(default);
            Assert.Contains("process_start_time_access_denied", result.Warnings);
            Assert.DoesNotContain("process_cpu_time_access_denied", result.Warnings);
        }
        else
        {
            var result = await new WindowsCpuMemoryCollector(() => [process]).CollectAsync(default);
            Assert.Contains("process_start_time_access_denied", result.Warnings);
            Assert.DoesNotContain("process_cpu_time_access_denied", result.Warnings);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmed_exited_process_is_skipped_without_a_collector_failure(bool serviceCollector)
    {
        var process = new Sample { CpuFailure = new InvalidOperationException("synthetic process exit"), Gone = true };
        if (serviceCollector)
        {
            var result = await new WindowsProcessServicesCollector(() => [process], () => []).CollectAsync(default);
            Assert.Empty(result.Value!.Processes);
            Assert.DoesNotContain("some_process_metrics_unavailable", result.Warnings);
        }
        else
        {
            var result = await new WindowsCpuMemoryCollector(() => [process]).CollectAsync(default);
            Assert.Empty(result.Value!.TopProcessesByMemory);
            Assert.DoesNotContain("some_process_metrics_unavailable", result.Warnings);
        }
        Assert.True(process.Disposed);
    }
    // A denied CPU handle must not erase memory/name data already readable
    // from the process enumeration snapshot, or fabricate a zero CPU value.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Denied_cpu_keeps_readable_memory_and_unknown_cpu(bool serviceCollector)
    {
        var process = new Sample { CpuDenied = true };
        if (serviceCollector)
        {
            var result = await new WindowsProcessServicesCollector(() => [process], () => []).CollectAsync(default);
            var item = Assert.Single(result.Value!.Processes);
            Assert.Equal(123456789, item.MemoryBytes);
            Assert.Equal("synthetic-test-process", item.ProcessName);
            Assert.Null(item.CpuUsagePercent);
            Assert.Equal(12345, item.ProcessId);
            Assert.Contains("process_cpu_time_access_denied", result.Warnings);
            Assert.Equal(CollectorStatus.Partial, result.Status);
        }
        else
        {
            var result = await new WindowsCpuMemoryCollector(() => [process]).CollectAsync(default);
            var item = Assert.Single(result.Value!.TopProcessesByMemory);
            Assert.Equal(123456789, item.MemoryBytes);
            Assert.Equal("synthetic-test-process", item.ProcessName);
            Assert.Null(item.CpuUsagePercent);
            Assert.Empty(result.Value.TopProcessesByCpu);
            Assert.Contains("process_cpu_time_access_denied", result.Warnings);
            Assert.Equal(CollectorStatus.Partial, result.Status);
        }
        Assert.True(process.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reused_pid_with_new_creation_time_does_not_reuse_cpu_baseline(bool serviceCollector)
    {
        var creation = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        IWindowsProcessSample[] Read() => [new Sample { Creation = creation }];
        if (serviceCollector)
        {
            var collector = new WindowsProcessServicesCollector(Read, () => []);
            await collector.CollectAsync(default);
            creation = creation.AddSeconds(1);
            var result = await collector.CollectAsync(default);
            Assert.Null(Assert.Single(result.Value!.Processes).CpuUsagePercent);
        }
        else
        {
            var collector = new WindowsCpuMemoryCollector(Read);
            await collector.CollectAsync(default);
            creation = creation.AddSeconds(1);
            var result = await collector.CollectAsync(default);
            Assert.Null(Assert.Single(result.Value!.TopProcessesByMemory).CpuUsagePercent);
        }
    }

    // Missing memory cannot be represented by a fabricated zero in these
    // nonnullable-memory records. Keep that failure explicit.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unreadable_memory_is_not_reported_as_zero(bool serviceCollector)
    {
        var process = new Sample { MemoryDenied = true };
        if (serviceCollector)
        {
            var result = await new WindowsProcessServicesCollector(() => [process], () => []).CollectAsync(default);
            Assert.Empty(result.Value!.Processes);
            Assert.Contains("process_working_set_access_denied", result.Warnings);
        }
        else
        {
            var result = await new WindowsCpuMemoryCollector(() => [process]).CollectAsync(default);
            Assert.Empty(result.Value!.TopProcessesByMemory);
            Assert.Contains("process_working_set_access_denied", result.Warnings);
        }
        Assert.True(process.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovered_cpu_starts_a_new_baseline_after_an_unreadable_sample(bool serviceCollector)
    {
        var denied = false;
        IWindowsProcessSample[] Read() => [new Sample { CpuDenied = denied }];
        if (serviceCollector)
        {
            var collector = new WindowsProcessServicesCollector(Read, () => []);
            await collector.CollectAsync(default);
            denied = true;
            await collector.CollectAsync(default);
            denied = false;
            var result = await collector.CollectAsync(default);
            Assert.Null(Assert.Single(result.Value!.Processes).CpuUsagePercent);
        }
        else
        {
            var collector = new WindowsCpuMemoryCollector(Read);
            await collector.CollectAsync(default);
            denied = true;
            await collector.CollectAsync(default);
            denied = false;
            var result = await collector.CollectAsync(default);
            Assert.Null(Assert.Single(result.Value!.TopProcessesByMemory).CpuUsagePercent);
        }
    }

    private sealed class Sample : IWindowsProcessSample
    {
        internal bool CpuDenied { get; init; }
        internal Exception? CpuFailure { get; init; }
        internal bool Gone { get; init; }
        internal bool StartDenied { get; init; }
        internal DateTimeOffset? CpuObservedAt { get; init; }
        internal TimeSpan CpuValue { get; init; } = TimeSpan.FromSeconds(2);
        internal bool MemoryDenied { get; init; }
        internal DateTime Creation { get; init; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        internal bool Disposed { get; private set; }
        public int Id => 12345;
        public string ProcessName => "synthetic-test-process";
        public long WorkingSet64 => MemoryDenied ? throw new Win32Exception(5) : 123456789;
        public bool HasExited => Gone;
        public DateTimeOffset? CpuCapturedAtUtc => CpuObservedAt;
        public TimeSpan TotalProcessorTime => CpuFailure is { } error ? throw error
            : CpuDenied ? throw new Win32Exception(5) : CpuValue;
        public DateTime StartTime => StartDenied ? throw new Win32Exception(5) : Creation;
        public string ExecutablePath => @"C:\synthetic\fixture.exe";
        public void Dispose() => Disposed = true;
    }
}
