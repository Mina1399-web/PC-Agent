namespace HostAgent.Core.Contracts;

public interface ICollectorCheckpointStore
{
    ValueTask<string?> ReadCollectorCheckpointAsync(
        string collectorId,
        string checkpointKey,
        CancellationToken cancellationToken);

    ValueTask SaveCollectorCheckpointAsync(
        string collectorId,
        string checkpointKey,
        string checkpointValue,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken);
}
