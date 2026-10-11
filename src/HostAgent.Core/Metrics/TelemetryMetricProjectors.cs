using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Core.Metrics;

public sealed class SystemTelemetryMetricProjector : ITelemetryMetricProjector<SystemTelemetry>
{
    public IReadOnlyList<MetricSample> Project(SystemTelemetry payload, DateTimeOffset sampledAtUtc) =>
        [Metric("system.uptime.seconds", "host", sampledAtUtc, payload.Uptime.TotalSeconds)];

    internal static MetricSample Metric(string name, string scope, DateTimeOffset sampledAtUtc, double value) =>
        TelemetryMetricProjectors.Create(name, scope, sampledAtUtc, value);
}

public sealed class CpuMemoryMetricProjector : ITelemetryMetricProjector<CpuMemoryTelemetry>
{
    public IReadOnlyList<MetricSample> Project(CpuMemoryTelemetry payload, DateTimeOffset sampledAtUtc)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var samples = new List<MetricSample>
        {
            SystemTelemetryMetricProjector.Metric(
                "memory.available.bytes",
                "host",
                sampledAtUtc,
                payload.AvailablePhysicalMemoryBytes),
            SystemTelemetryMetricProjector.Metric(
                "memory.used.percent",
                "host",
                sampledAtUtc,
                payload.MemoryPressurePercent)
        };

        if (payload.CpuUsagePercent is { } cpuUsage)
        {
            samples.Add(SystemTelemetryMetricProjector.Metric("cpu.usage.percent", "host", sampledAtUtc, cpuUsage));
        }

        if (payload.CommitLimitBytes is > 0 && payload.CommitAvailableBytes is { } commitAvailable)
        {
            var usedPercent = 100d * Math.Clamp(payload.CommitLimitBytes.Value - commitAvailable, 0, payload.CommitLimitBytes.Value)
                / payload.CommitLimitBytes.Value;
            samples.Add(SystemTelemetryMetricProjector.Metric("memory.commit.used.percent", "host", sampledAtUtc, usedPercent));
        }

        return samples.AsReadOnly();
    }
}

public sealed class DiskTelemetryMetricProjector : ITelemetryMetricProjector<DiskTelemetry>
{
    public IReadOnlyList<MetricSample> Project(DiskTelemetry payload, DateTimeOffset sampledAtUtc)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var samples = new List<MetricSample>();
        foreach (var volume in payload.Volumes)
        {
            samples.Add(SystemTelemetryMetricProjector.Metric(
                "disk.used.percent",
                volume.RootPath,
                sampledAtUtc,
                volume.UsedPercent));
            samples.Add(SystemTelemetryMetricProjector.Metric(
                "disk.free.bytes",
                volume.RootPath,
                sampledAtUtc,
                volume.FreeBytes));
        }

        if (payload.IoCounters is { IsAvailable: true } io)
        {
            AddIfPresent(samples, "disk.read.bytes_per_second", io.ReadBytesPerSecond, sampledAtUtc);
            AddIfPresent(samples, "disk.write.bytes_per_second", io.WriteBytesPerSecond, sampledAtUtc);
            AddIfPresent(samples, "disk.latency.milliseconds", io.AverageLatencyMilliseconds, sampledAtUtc);
        }

        return samples.AsReadOnly();
    }

    private static void AddIfPresent(
        ICollection<MetricSample> samples,
        string name,
        double? value,
        DateTimeOffset sampledAtUtc)
    {
        if (value is { } actual)
        {
            samples.Add(SystemTelemetryMetricProjector.Metric(name, "host", sampledAtUtc, actual));
        }
    }
}

public sealed class ProcessServiceMetricProjector : ITelemetryMetricProjector<ProcessServiceTelemetry>
{
    public IReadOnlyList<MetricSample> Project(ProcessServiceTelemetry payload, DateTimeOffset sampledAtUtc)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return
        [
            SystemTelemetryMetricProjector.Metric("process.count", "host", sampledAtUtc, payload.Processes.Count),
            SystemTelemetryMetricProjector.Metric("service.count", "host", sampledAtUtc, payload.Services.Count),
            SystemTelemetryMetricProjector.Metric(
                "service.running.count",
                "host",
                sampledAtUtc,
                payload.Services.Count(static service => service.Status == HostServiceStatus.Running)),
            SystemTelemetryMetricProjector.Metric(
                "service.state_change.count",
                "host",
                sampledAtUtc,
                payload.ServiceChanges.Count)
        ];
    }
}

public sealed class NetworkTelemetryMetricProjector : ITelemetryMetricProjector<NetworkTelemetry>
{
    public IReadOnlyList<MetricSample> Project(NetworkTelemetry payload, DateTimeOffset sampledAtUtc)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var samples = new List<MetricSample>();
        foreach (var probe in payload.Probes)
        {
            if (probe.Status == NetworkProbeStatus.Unavailable)
            {
                continue;
            }

            var scope = probe.Kind.ToString();
            samples.Add(SystemTelemetryMetricProjector.Metric(
                "network.probe.success",
                scope,
                sampledAtUtc,
                probe.Status == NetworkProbeStatus.Succeeded ? 1 : 0));
            AddIfPresent(samples, "network.probe.latency.milliseconds", scope, probe.LatencyMilliseconds, sampledAtUtc);
            AddIfPresent(samples, "network.probe.packet_loss.percent", scope, probe.PacketLossPercent, sampledAtUtc);
        }

        return samples.AsReadOnly();
    }

    private static void AddIfPresent(
        ICollection<MetricSample> samples,
        string name,
        string scope,
        double? value,
        DateTimeOffset sampledAtUtc)
    {
        if (value is { } actual)
        {
            samples.Add(SystemTelemetryMetricProjector.Metric(name, scope, sampledAtUtc, actual));
        }
    }
}

public sealed class EventLogMetricProjector : ITelemetryMetricProjector<EventLogTelemetry>
{
    private static readonly string[] LogNames = ["System", "Application"];
    private static readonly string[] Levels = ["Critical", "Error", "Warning"];

    public IReadOnlyList<MetricSample> Project(EventLogTelemetry payload, DateTimeOffset sampledAtUtc)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var counts = payload.Events
            .GroupBy(static entry => (entry.LogName, entry.Level))
            .ToDictionary(static group => group.Key, static group => group.Count());

        var samples = new List<MetricSample>(LogNames.Length * Levels.Length);
        foreach (var logName in LogNames)
        {
            foreach (var level in Levels)
            {
                var count = counts.GetValueOrDefault((logName, level));
                samples.Add(SystemTelemetryMetricProjector.Metric(
                    "eventlog.event.count",
                    $"{logName}:{level}",
                    sampledAtUtc,
                    count));
            }
        }

        return samples.AsReadOnly();
    }
}

internal static class TelemetryMetricProjectors
{
    public static MetricSample Create(string name, string scope, DateTimeOffset sampledAtUtc, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Metric values must be finite.");
        }

        return new MetricSample(Guid.NewGuid(), name, scope, sampledAtUtc.ToUniversalTime(), value);
    }
}
