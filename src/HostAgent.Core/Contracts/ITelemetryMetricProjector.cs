using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Core.Contracts;

public interface ITelemetryMetricProjector<TPayload> where TPayload : class
{
    IReadOnlyList<MetricSample> Project(TPayload payload, DateTimeOffset sampledAtUtc);
}
