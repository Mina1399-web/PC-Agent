using HostAgent.Core.Models.Audit;
using HostAgent.Core.Models.Storage;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Core.Models.Baseline;
using HostAgent.Core.Models.Incidents;

namespace HostAgent.Core.Contracts;

public interface IHostAgentStore : ICollectorCheckpointStore, IAsyncDisposable
{
    ValueTask<IReadOnlyList<MetricSample>> ReadMetricSamplesAsync(DateTimeOffset fromUtc, DateTimeOffset throughUtc, int limit, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<BaselineOrigin>> ReadBaselineOriginsAsync(CancellationToken cancellationToken);
    ValueTask SaveIncidentBundleAsync(Incident incident, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<Incident>> ReadIncidentsAsync(CancellationToken cancellationToken);

    ValueTask InitializeAsync(CancellationToken cancellationToken);

    ValueTask<int> GetSchemaVersionAsync(CancellationToken cancellationToken);

    ValueTask SaveTelemetrySnapshotAsync(
        TelemetrySnapshot snapshot,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<TelemetrySnapshot>> ReadTelemetrySnapshotsAsync(
        string collectorId,
        DateTimeOffset fromUtc,
        DateTimeOffset throughUtc,
        int limit,
        CancellationToken cancellationToken);

    ValueTask SaveMetricSampleAsync(
        MetricSample sample,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<MetricAggregate>> ReadMetricAggregatesAsync(
        AggregateGranularity granularity,
        string metricName,
        string scope,
        DateTimeOffset fromUtc,
        DateTimeOffset throughUtc,
        CancellationToken cancellationToken);

    ValueTask SaveIncidentAsync(
        IncidentStorageRecord incident,
        CancellationToken cancellationToken);

    ValueTask SaveIncidentSnapshotAsync(
        IncidentSnapshotStorageRecord snapshot,
        CancellationToken cancellationToken);

    ValueTask SaveDiagnosticEvidenceAsync(
        DiagnosticEvidenceStorageRecord evidence,
        CancellationToken cancellationToken);

    ValueTask AppendAuditEntryAsync(
        AuditEntry entry,
        CancellationToken cancellationToken);

    ValueTask AppendRemediationAttemptAsync(
        RemediationAttemptStorageRecord attempt,
        CancellationToken cancellationToken);

    ValueTask SaveServiceHealthAsync(
        ServiceHealthSnapshot snapshot,
        CancellationToken cancellationToken);

    ValueTask SaveTelemetryCollectionAsync(
        TelemetrySnapshot? snapshot,
        IReadOnlyList<MetricSample> metricSamples,
        IReadOnlyList<CollectorCheckpoint> checkpoints,
        ServiceHealthSnapshot health,
        CancellationToken cancellationToken);

    ValueTask<int> ApplyRetentionAsync(
        DateTimeOffset nowUtc,
        TimeSpan detailedRetention,
        CancellationToken cancellationToken);
}
