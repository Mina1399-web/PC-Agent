using System.Text.Json;
using HostAgent.Core.Contracts;
using HostAgent.Core.Diagnostics;
using HostAgent.Core.Models.Baseline;
using HostAgent.Core.Models.Diagnosis;
using HostAgent.Core.Models.Incidents;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Core.Orchestration;

public sealed class DiagnosticCycleProcessor(IHostAgentStore store) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<IReadOnlyList<Incident>> ProcessAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var samples = await store.ReadMetricSamplesAsync(nowUtc.AddDays(-30), nowUtc, 1_000_000, cancellationToken);
            var currentSamples = samples.GroupBy(sample => (sample.MetricName, sample.Scope))
                .Select(group => group.MaxBy(sample => sample.SampledAtUtc)!)
                .Where(sample => sample.SampledAtUtc >= nowUtc.AddMinutes(-2)).ToArray();
            var ids = currentSamples.Select(sample => sample.SampleId).ToHashSet();
            var baseline = new BaselineEngine();
            await baseline.RestoreAsync(samples.Where(sample => !ids.Contains(sample.SampleId)).ToArray(),
                await store.ReadBaselineOriginsAsync(cancellationToken), cancellationToken);
            var assessments = new List<BaselineAssessment>();
            foreach (var sample in currentSamples)
                assessments.Add(await baseline.ObserveAsync(sample.MetricName, sample.Scope, sample.SampledAtUtc, sample.Value, cancellationToken));

            var timestamps = new List<DateTimeOffset>();
            async ValueTask<T?> Latest<T>(string collectorId) where T : class
            {
                var records = await store.ReadTelemetrySnapshotsAsync(collectorId, nowUtc.AddMinutes(-2), nowUtc, 1, cancellationToken);
                if (records.Count == 0) return null;
                var record = records[0];
                if (record.PayloadSchemaVersion != 1) throw new NotSupportedException("Unsupported telemetry schema.");
                using var document = JsonDocument.Parse(record.PayloadJson);
                var value = document.RootElement.GetProperty("Value").Deserialize<T>();
                if (value is not null) timestamps.Add(record.SampledAtUtc);
                return value;
            }

            var system = await Latest<SystemTelemetry>("system");
            var cpu = await Latest<CpuMemoryTelemetry>("cpu-memory");
            var disk = await Latest<DiskTelemetry>("disk");
            var services = await Latest<ProcessServiceTelemetry>("process-services");
            var network = await Latest<NetworkTelemetry>("network");
            var events = await Latest<EventLogTelemetry>("event-log");
            if (timestamps.Count == 0) return [];
            var observedAt = timestamps.Max();
            var evidence = new HostEvidence(observedAt, system, cpu, disk, services, network, events, assessments,
                samples.Where(sample => sample.SampledAtUtc >= observedAt.AddMinutes(-5)).ToArray());
            var diagnoses = await new DiagnosticEngine().EvaluateAsync(evidence, cancellationToken);
            var incidents = new IncidentEngine();
            incidents.Restore(await store.ReadIncidentsAsync(cancellationToken));
            var updated = new List<Incident>();
            foreach (var diagnosis in diagnoses)
            {
                var snapshot = new IncidentSnapshot(Guid.NewGuid().ToString("N"), "", observedAt,
                    system, cpu, disk, services, network, events, assessments, diagnosis.Evidence);
                var incident = await incidents.ApplyAsync(diagnosis, snapshot, cancellationToken);
                await store.SaveIncidentBundleAsync(incident, cancellationToken);
                updated.Add(incident);
            }
            return updated;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<Incident> TransitionAsync(string incidentId, IncidentLifecycleState state,
        IncidentSnapshot snapshot, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var engine = new IncidentEngine();
            engine.Restore(await store.ReadIncidentsAsync(cancellationToken));
            var updated = await engine.TransitionAsync(incidentId, state, snapshot, cancellationToken);
            await store.SaveIncidentBundleAsync(updated, cancellationToken);
            return updated;
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();
}
