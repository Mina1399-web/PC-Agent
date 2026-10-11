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
    private readonly BaselineEngine _baseline = new();
    private readonly Dictionary<(string Metric, string Scope), (DateTimeOffset At, Guid Id)> _lastSamples = [];
    private readonly Dictionary<(string Metric, string Scope), BaselineAssessment> _assessments = [];
    public string? BaselineFailureCode { get; private set; }

    public async ValueTask<IReadOnlyList<Incident>> ProcessAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            BaselineFailureCode = null;
            try
            {
                var origins = await store.ReadBaselineOriginsAsync(cancellationToken);
                if (origins.Count > 4096) BaselineFailureCode = "baseline_series_capacity";
                foreach (var origin in origins.Take(4096))
                {
                    var key = (origin.MetricName, origin.Scope);
                    var initialized = _lastSamples.TryGetValue(key, out var last);
                    var series = await store.ReadMetricSeriesAsync(origin.MetricName, origin.Scope,
                        initialized ? last.At : nowUtc.AddDays(-30), nowUtc, 50_000, cancellationToken);
                    if (series.Count == 0) continue;
                    if (!initialized)
                    {
                        await _baseline.RestoreSeriesAsync(series.Take(series.Count - 1).ToArray(), origin, cancellationToken);
                        series = [series[^1]];
                    }
                    foreach (var sample in series.Where(sample => !initialized || sample.SampledAtUtc > last.At
                        || (sample.SampledAtUtc == last.At && sample.SampleId != last.Id)))
                    {
                        _assessments[key] = await _baseline.ObserveAsync(sample.MetricName, sample.Scope, sample.SampledAtUtc, sample.Value, cancellationToken);
                        _lastSamples[key] = (sample.SampledAtUtc, sample.SampleId);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // Adaptive availability never gates fixed critical rules. Expose a bounded health code to the coordinator.
                BaselineFailureCode = "baseline_restore_failed";
            }
            var assessments = _assessments.Values.Where(value => value.SampledAtUtc >= nowUtc.AddMinutes(-2)
                && value.SampledAtUtc <= nowUtc).ToArray();
            var samples = await store.ReadMetricSeriesAsync("cpu.usage.percent", "host", nowUtc.AddMinutes(-5), nowUtc, 500, cancellationToken);

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
            incidents.Restore(await store.ReadIncidentHeadsAsync(cancellationToken));
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
            engine.Restore(await store.ReadIncidentHeadsAsync(cancellationToken));
            var updated = await engine.TransitionAsync(incidentId, state, snapshot, cancellationToken);
            await store.SaveIncidentBundleAsync(updated, cancellationToken);
            return updated;
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();
}
