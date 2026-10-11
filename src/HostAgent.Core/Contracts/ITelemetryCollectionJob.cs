using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Core.Contracts;

public interface ITelemetryCollectionJob
{
    string CollectorId { get; }

    ValueTask<CollectorStatus> CollectAndPersistAsync(CancellationToken cancellationToken);
}
