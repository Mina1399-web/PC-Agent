using System.Reflection;
using HostAgent.Core.Contracts;
using HostAgent.Core.Metrics;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Core.Orchestration;
using HostAgent.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class CollectionCoordinatorTests
{
    [Fact]
    public async Task Coordinator_runs_diagnostics_after_collection_and_a_failed_peer_does_not_prevent_incident()
    {
        var type = Assembly.Load("HostAgent.Service").GetType("HostAgent.Service.CollectionCoordinator")!;
        var run = type.GetMethod("RunCycleAsync");
        Assert.NotNull(run);
        var now = DateTimeOffset.Parse("2026-10-11T12:00:00Z");
        var path = Path.Combine(Path.GetTempPath(), "HostAgentTests", Guid.NewGuid().ToString("N"), "coordinator.db");
        await using var store = new SqliteHostAgentStore(path);
        using var processor = new DiagnosticCycleProcessor(store);
        var logger = Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(type))!;
        ITelemetryCollectionJob[] jobs = [new TelemetryCollectionJob<NetworkTelemetry>(new NetworkCollector(now), store, new NetworkTelemetryMetricProjector()), new FailedJob()];
        using var coordinator = (IDisposable)Activator.CreateInstance(type, store, jobs, processor, logger)!;
        await (ValueTask)run!.Invoke(coordinator, [now, CancellationToken.None])!;
        var incident = Assert.Single(await store.ReadIncidentsAsync(CancellationToken.None));
        Assert.Equal("gateway_failure", incident.LatestDiagnosis.ProbableRootCause.Code);
        Assert.Equal(90, incident.LatestDiagnosis.Confidence);
        Assert.NotNull(incident.Snapshots[0].Network);
        await (ValueTask)run.Invoke(coordinator, [now, CancellationToken.None])!;
        var repeated = Assert.Single(await store.ReadIncidentsAsync(CancellationToken.None));
        Assert.Equal(incident.IncidentId, repeated.IncidentId);
        Assert.Equal(1, repeated.RecurrenceCount);
    }

    private sealed class NetworkCollector(DateTimeOffset now) : ITelemetryCollector<NetworkTelemetry>
    {
        public string CollectorId => "network";
        public ValueTask<CollectorResult<NetworkTelemetry>> CollectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(CollectorResult<NetworkTelemetry>.Success(CollectorId, now,
                new([], [new(NetworkProbeKind.Gateway, "192.0.2.1", NetworkProbeStatus.Failed, TimeSpan.Zero, null, null, "fixture")])));
    }
    private sealed class FailedJob : ITelemetryCollectionJob
    {
        public string CollectorId => "failed-peer";
        public ValueTask<CollectorStatus> CollectAndPersistAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("fixture");
    }
}
