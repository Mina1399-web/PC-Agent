using HostAgent.Core.Dashboard;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Service;

public sealed partial class DashboardDispatcher
{
    private const int DiskBucketMinutes = 15;
    private const int DiskBucketCount = 96;
    private const int DiskReadLimit = 20_000;
    private const string DiskHistorySource = "disk";
    private const string DiskCounterSource = "PDH";

    private sealed record DiskIoEnvelope(DiskIoTelemetry? IoCounters);

    private async ValueTask<ViewDiskHistory> ReadDiskHistory(string group, DateTimeOffset now, CancellationToken token)
    {
        var from = now.AddHours(-24);
        var current = await Latest<DiskIoEnvelope>("disk", now, token);
        var series = new List<ViewDiskHistorySeries>(2);
        if (group == "throughput")
        {
            series.Add(await DiskSeries("read", "disk.read.bytes_per_second", "B/s",
                current.Value?.IoCounters?.ReadBytesPerSecond, current, from, now, token));
            series.Add(await DiskSeries("write", "disk.write.bytes_per_second", "B/s",
                current.Value?.IoCounters?.WriteBytesPerSecond, current, from, now, token));
        }
        else
        {
            series.Add(await DiskSeries("latency", "disk.latency.milliseconds", "ms",
                current.Value?.IoCounters?.AverageLatencyMilliseconds, current, from, now, token));
        }
        return new(from, now, DiskBucketMinutes, series);
    }

    private async ValueTask<ViewDiskHistorySeries> DiskSeries(string key, string name, string unit,
        double? currentValue, SourceSample<DiskIoEnvelope> sample, DateTimeOffset from, DateTimeOffset through,
        CancellationToken token)
    {
        var rows = await store.ReadMetricSeriesAsync(name, "host", from, through, DiskReadLimit, token);
        var counts = new int[DiskBucketCount];
        var values = new double?[DiskBucketCount];
        var capped = rows.Count >= DiskReadLimit;
        if (!capped)
        {
            var bucketTicks = TimeSpan.FromMinutes(DiskBucketMinutes).Ticks;
            foreach (var row in rows)
            {
                if (row.SampledAtUtc < from || row.SampledAtUtc > through ||
                    row.Value is not (>= 0 and < double.PositiveInfinity)) continue;
                var index = (int)Math.Min(DiskBucketCount - 1, (row.SampledAtUtc - from).Ticks / bucketTicks);
                var previous = values[index].GetValueOrDefault();
                values[index] = previous + (row.Value - previous) / ++counts[index];
            }
        }
        var observed = counts.Count(count => count > 0);
        var coverage = capped ? "unavailable" : observed == 0 ? "unknown" :
            observed == DiskBucketCount ? "available" : "partial";
        var coverageReason = capped ? "history_capacity" : observed == 0 ? "missing_sample" :
            observed == DiskBucketCount ? null : "incomplete_sample";
        var state = sample.Describe(DiskCounterSource);
        ViewMetric? point = null;
        if (sample.Value is { } envelope)
        {
            if (envelope.IoCounters is { IsAvailable: true } && currentValue is >= 0 and < double.PositiveInfinity
                && sample.At is { } at)
            {
                point = new(at, currentValue.Value);
                state = new(DiskCounterSource, at, "available", null);
            }
            else
            {
                var reason = envelope.IoCounters?.UnavailableReason switch
                {
                    "disk_io_query_unavailable" or "disk_io_counters_unavailable" or "disk_io_sample_unavailable"
                        or "disk_io_values_unavailable" or "disk_io_api_unavailable" or "some_disk_io_counters_unavailable"
                        => envelope.IoCounters.UnavailableReason,
                    _ => currentValue.HasValue ? "invalid_sample" : "missing_sample"
                };
                state = new(DiskCounterSource, sample.At, envelope.IoCounters is null ? "unknown" : "unavailable", reason);
            }
        }
        return new(key, unit, DiskHistorySource, coverage, coverageReason, values, point, state);
    }
}
