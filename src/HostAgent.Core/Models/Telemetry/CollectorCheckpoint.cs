namespace HostAgent.Core.Models.Telemetry;

public interface ICheckpointedTelemetry
{
    IReadOnlyList<CollectorCheckpoint> Checkpoints { get; }
}

public sealed record CollectorCheckpoint(string Key, string Value);
