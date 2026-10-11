using System.Text.Json;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Storage;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Core.Orchestration;

public sealed class TelemetryCollectionJob<TPayload> : ITelemetryCollectionJob where TPayload : class
{
    public const int PayloadSchemaVersion = 1;

    private readonly ITelemetryCollector<TPayload> _collector;
    private readonly IHostAgentStore _store;
    private readonly ITelemetryMetricProjector<TPayload>? _metricProjector;
    private readonly JsonSerializerOptions? _serializerOptions;

    public TelemetryCollectionJob(
        ITelemetryCollector<TPayload> collector,
        IHostAgentStore store,
        ITelemetryMetricProjector<TPayload>? metricProjector = null,
        JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(collector.CollectorId);

        _collector = collector;
        _store = store;
        _metricProjector = metricProjector;
        _serializerOptions = serializerOptions;
    }

    public string CollectorId => _collector.CollectorId;

    public async ValueTask<CollectorStatus> CollectAndPersistAsync(CancellationToken cancellationToken)
    {
        var result = await _collector.CollectAsync(cancellationToken);
        if (!string.Equals(result.CollectorId, CollectorId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The collector returned a result for a different collector ID.");
        }

        TelemetrySnapshot? snapshot = null;
        IReadOnlyList<MetricSample> metrics = Array.Empty<MetricSample>();
        IReadOnlyList<CollectorCheckpoint> checkpoints = Array.Empty<CollectorCheckpoint>();
        if (result.Status is CollectorStatus.Success or CollectorStatus.Partial)
        {
            if (result.Value is null)
            {
                throw new InvalidOperationException("An available collector result must include its typed payload.");
            }

            metrics = _metricProjector?.Project(result.Value, result.SampledAtUtc)
                ?? Array.Empty<MetricSample>();
            if (result.Value is ICheckpointedTelemetry checkpointed)
            {
                checkpoints = checkpointed.Checkpoints
                    ?? throw new InvalidOperationException("Checkpointed telemetry returned a null checkpoint list.");
            }

            snapshot = new TelemetrySnapshot(
                Guid.NewGuid(),
                CollectorId,
                PayloadSchemaVersion,
                result.SampledAtUtc,
                JsonSerializer.Serialize(result, _serializerOptions));
            foreach (var metric in metrics)
            {
                ArgumentNullException.ThrowIfNull(metric);
                if (metric.SampledAtUtc.ToUniversalTime() != result.SampledAtUtc
                    || metric.SampleId == Guid.Empty
                    || !double.IsFinite(metric.Value))
                {
                    throw new InvalidOperationException("A metric projector returned an invalid or mismatched sample.");
                }
            }
        }

        var health = new ServiceHealthSnapshot(
            CollectorId,
            result.SampledAtUtc,
            result.Status.ToString(),
            JsonSerializer.Serialize(
                new CollectorHealthPayload(result.ErrorCode, result.Warnings),
                _serializerOptions));
        await _store.SaveTelemetryCollectionAsync(
            snapshot,
            metrics,
            checkpoints,
            health,
            cancellationToken);

        return result.Status;
    }

    private sealed record CollectorHealthPayload(
        string? ErrorCode,
        IReadOnlyList<string> Warnings);
}
