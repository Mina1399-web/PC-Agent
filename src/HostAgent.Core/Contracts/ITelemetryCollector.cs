using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Core.Contracts;

public interface ITelemetryCollector<TPayload> where TPayload : class
{
    string CollectorId { get; }

    ValueTask<CollectorResult<TPayload>> CollectAsync(CancellationToken cancellationToken);
}
