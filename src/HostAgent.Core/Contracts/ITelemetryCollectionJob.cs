using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Core.Contracts;

public interface ITelemetryCollectionJob
{
    string CollectorId { get; }

    IReadOnlyList<string> WarningCodes => Array.Empty<string>();

    ValueTask<CollectorStatus> CollectAndPersistAsync(CancellationToken cancellationToken);
}
