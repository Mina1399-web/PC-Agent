using System.Reflection;
using HostAgent.Core.Contracts;
using HostAgent.Core.Metrics;
using HostAgent.Core.Models.Incidents;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Core.Orchestration;
using HostAgent.Infrastructure.Storage;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class DiagnosticCycleTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-11T12:00:00Z");

    [Fact]
    public async Task Persisted_collection_feeds_baselines_diagnostics_incidents_and_restart_deduplication()
    {
        var type = typeof(TelemetryCollectionJob<>).Assembly.GetType("HostAgent.Core.Orchestration.DiagnosticCycleProcessor");
        Assert.NotNull(type);
        var path = Path.Combine(Path.GetTempPath(), "HostAgentTests", Guid.NewGuid().ToString("N"), "cycle.db");
        await using var store = new SqliteHostAgentStore(path);
        for (var minute = -4; minute <= 0; minute++)
        {
            var collector = new CpuCollector(Now.AddMinutes(minute));
            await new TelemetryCollectionJob<CpuMemoryTelemetry>(collector, store, new CpuMemoryMetricProjector()).CollectAndPersistAsync(CancellationToken.None);
        }
        var processor = Activator.CreateInstance(type!, store)!;
        var first = Assert.Single(await Process(processor, Now));
        Assert.Equal(85, first.LatestDiagnosis.Confidence);
        Assert.Equal("cpu_process_saturation", first.LatestDiagnosis.ProbableRootCause.Code);
        Assert.Equal(3, first.Snapshots[0].Baselines.Count);
        await new TelemetryCollectionJob<CpuMemoryTelemetry>(new CpuCollector(Now.AddMinutes(1)), store, new CpuMemoryMetricProjector()).CollectAndPersistAsync(CancellationToken.None);
        var restarted = Activator.CreateInstance(type!, store)!;
        var second = Assert.Single(await Process(restarted, Now.AddMinutes(1)));
        Assert.Equal(first.IncidentId, second.IncidentId);
        Assert.Equal(2, second.RecurrenceCount);
        Assert.Single(await store.ReadIncidentsAsync(CancellationToken.None));
        Assert.Empty(await Process(restarted, Now.AddMinutes(10)));
    }

    [Fact]
    public async Task Failed_incident_persistence_is_retried_without_losing_the_detected_snapshot()
    {
        var type = typeof(TelemetryCollectionJob<>).Assembly.GetType("HostAgent.Core.Orchestration.DiagnosticCycleProcessor");
        Assert.NotNull(type);
        var path = Path.Combine(Path.GetTempPath(), "HostAgentTests", Guid.NewGuid().ToString("N"), "retry.db");
        await using var store = new SqliteHostAgentStore(path);
        await new TelemetryCollectionJob<CpuMemoryTelemetry>(new CpuCollector(Now, 10), store, new CpuMemoryMetricProjector()).CollectAndPersistAsync(CancellationToken.None);
        // A fixed memory diagnosis is sufficient here; CPU must not diagnose a lone high sample.
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER reject_incident BEFORE INSERT ON incident_snapshots BEGIN SELECT RAISE(ABORT, 'fixture'); END;";
        command.ExecuteNonQuery();
        var processor = Activator.CreateInstance(type!, store)!;
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => Process(processor, Now));
        Assert.Empty(await store.ReadIncidentsAsync(CancellationToken.None));
        command.CommandText = "DROP TRIGGER reject_incident;";
        command.ExecuteNonQuery();
        var retry = Assert.Single(await Process(processor, Now));
        Assert.Single(retry.Snapshots);
        Assert.Equal(1, retry.RecurrenceCount);
        Assert.Equal(96, retry.LatestDiagnosis.Confidence);
    }

    private static async Task<IReadOnlyList<Incident>> Process(object processor, DateTimeOffset at) =>
        await (ValueTask<IReadOnlyList<Incident>>)processor.GetType().GetMethod("ProcessAsync")!.Invoke(processor, [at, CancellationToken.None])!;

    [Fact]
    public async Task Fixed_critical_rules_survive_global_restore_capacity_and_adaptive_failure()
    {
        var path = Path.Combine(Path.GetTempPath(), "HostAgentTests", Guid.NewGuid().ToString("N"), "capacity.db");
        await using var store = new SqliteHostAgentStore(path);
        await new TelemetryCollectionJob<CpuMemoryTelemetry>(new CpuCollector(Now, 10), store, new CpuMemoryMetricProjector()).CollectAndPersistAsync(CancellationToken.None);
        var proxy = DispatchProxy.Create<IHostAgentStore, CapacityStoreProxy>();
        ((CapacityStoreProxy)(object)proxy).Inner = store;
        using var processor = new DiagnosticCycleProcessor(proxy);
        var incident = Assert.Single(await processor.ProcessAsync(Now, CancellationToken.None));
        Assert.Equal("memory_pressure", incident.LatestDiagnosis.ProbableRootCause.Code);
        Assert.Equal(96, incident.LatestDiagnosis.Confidence);
        ((CapacityStoreProxy)(object)proxy).FailAdaptive = true;
        using var degraded = new DiagnosticCycleProcessor(proxy);
        Assert.Equal(96, Assert.Single(await degraded.ProcessAsync(Now, CancellationToken.None)).LatestDiagnosis.Confidence);
    }

    public class CapacityStoreProxy : DispatchProxy
    {
        public IHostAgentStore Inner { get; set; } = null!;
        public bool FailAdaptive { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == "ReadMetricSamplesAsync" && (DateTimeOffset)args![1]! - (DateTimeOffset)args[0]! > TimeSpan.FromMinutes(5))
                throw new InvalidOperationException("Metric restore exceeded its bounded read capacity.");
            if (FailAdaptive && targetMethod.Name == "ReadMetricSeriesAsync" && (string)args![0]! != "cpu.usage.percent")
                throw new InvalidOperationException("Adaptive restore capacity fixture.");
            return targetMethod.Invoke(Inner, args);
        }
    }

    private sealed class CpuCollector(DateTimeOffset at, long available = 500) : ITelemetryCollector<CpuMemoryTelemetry>
    {
        public string CollectorId => "cpu-memory";
        public ValueTask<CollectorResult<CpuMemoryTelemetry>> CollectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(CollectorResult<CpuMemoryTelemetry>.Success(CollectorId, at,
                new(97, 1000, available, null, null, [new(17, "Indexer", 83, 500, null)], [])));
    }
}
