using System.Text.Json;
using HostAgent.Core.Metrics;
using HostAgent.Core.Models.Telemetry;
using Xunit;

namespace HostAgent.Core.Tests;

public sealed class DataTrustworthinessTests
{
    [Theory]
    [InlineData(null, null, null)]
    [InlineData(100L, null, null)]
    [InlineData(0L, 0L, null)]
    [InlineData(-1L, 0L, null)]
    [InlineData(100L, -1L, null)]
    [InlineData(100L, 101L, null)]
    [InlineData(100L, 100L, 0d)]
    [InlineData(100L, 75L, 25d)]
    [InlineData(100L, 0L, 100d)]
    public void Memory_uses_only_valid_byte_pairs(long? total, long? available, double? expected)
    {
        var sample = new CpuMemoryTelemetry(0, total, available, total, available, [], []);
        var metrics = new CpuMemoryMetricProjector().Project(sample, DateTimeOffset.UnixEpoch);
        Assert.Equal(expected, sample.MemoryPressurePercent);
        Assert.Equal(expected, sample.CommitPressurePercent);
        Assert.Equal(0d, Assert.Single(metrics, m => m.MetricName == "cpu.usage.percent").Value);
        if (expected is null)
            Assert.DoesNotContain(metrics, m => m.MetricName.StartsWith("memory.", StringComparison.Ordinal));
        else
            Assert.Equal(expected.Value, Assert.Single(metrics, m => m.MetricName == "memory.used.percent").Value);
    }

    [Fact]
    public void Old_computed_property_does_not_override_missing_raw_memory()
    {
        var telemetry = JsonSerializer.Deserialize<CpuMemoryTelemetry>("""{"TotalPhysicalMemoryBytes":100,"MemoryPressurePercent":0}""");
        Assert.NotNull(telemetry);
        Assert.Null(telemetry.MemoryPressurePercent);
    }
}
