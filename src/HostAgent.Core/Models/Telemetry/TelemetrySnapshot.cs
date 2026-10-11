namespace HostAgent.Core.Models.Telemetry;

public sealed record TelemetrySnapshot(
    Guid SnapshotId,
    string CollectorId,
    int PayloadSchemaVersion,
    DateTimeOffset SampledAtUtc,
    string PayloadJson);

public sealed record MetricSample(
    Guid SampleId,
    string MetricName,
    string Scope,
    DateTimeOffset SampledAtUtc,
    double Value);

public enum AggregateGranularity
{
    Hourly,
    Daily
}

public sealed record MetricAggregate(
    string MetricName,
    string Scope,
    DateTimeOffset BucketStartUtc,
    long SampleCount,
    double Sum,
    double Minimum,
    double Maximum)
{
    public double Average => SampleCount == 0 ? 0 : Sum / SampleCount;
}
