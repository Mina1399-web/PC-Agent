using System.Reflection;
using System.Text.Json;
using HostAgent.Core.Contracts;
using HostAgent.Core.Metrics;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Core.Orchestration;
using HostAgent.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using HostAgent.Service;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Notifications;

namespace HostAgent.Infrastructure.Tests;

public sealed class CollectionCoordinatorTests
{
    [Fact]
    public async Task Failed_outbound_transport_does_not_stop_next_collection_and_incident_is_published()
    {
        var now=DateTimeOffset.UtcNow;
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        var noticePath=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        try
        {
            await using var store=new SqliteHostAgentStore(path);
            var notices=new SqliteNotificationStore(noticePath);
            using var processor=new DiagnosticCycleProcessor(store);
            var type=Assembly.Load("HostAgent.Service").GetType("HostAgent.Service.CollectionCoordinator")!;
            var logger=Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(type))!;
            var health=new AgentRuntimeHealth();
            var collector=new NetworkCollector(now);
            using var coordinator=(IDisposable)Activator.CreateInstance(type,store,
                new ITelemetryCollectionJob[]{new TelemetryCollectionJob<NetworkTelemetry>(collector,store,new NetworkTelemetryMetricProjector())},
                processor,logger,new IncidentPublisher(store,notices,new()),health,new ServiceConfiguration())!;
            var run=type.GetMethod("RunCycleAsync")!;
            await (ValueTask)run.Invoke(coordinator,[now,CancellationToken.None])!;
            Assert.Single(await notices.ReadDueAsync(now,default));
            using var http=new HttpClient(new OutboundTransportTests.FakeHttp{Status=System.Net.HttpStatusCode.ServiceUnavailable});
            var outbound=new OutboundCoordinator(notices,new PushoverNotifier(http,new OutboundTransportTests.TestSecrets(),TimeSpan.FromSeconds(1)),new NotificationOutboxTests.FakeHeartbeat());
            await outbound.RunCycleAsync(now,false,default);
            Assert.True(outbound.Degraded);
            collector.Now=now.AddSeconds(1);
            await (ValueTask)run.Invoke(coordinator,[collector.Now,CancellationToken.None])!;
            Assert.Single(await store.ReadIncidentHeadsAsync(default));
            Assert.False(health.Degraded);
        }
        finally{ChaosFixtureRetention.DeleteFile(path);ChaosFixtureRetention.DeleteFile(noticePath);}
    }
    [Fact]
    public void Collection_coordinator_accepts_notification_publisher_and_runtime_health_wiring()
    {
        var type=Assembly.Load("HostAgent.Service").GetType("HostAgent.Service.CollectionCoordinator")!;
        Assert.Contains(type.GetConstructors(),constructor=>constructor.GetParameters().Any(parameter=>parameter.ParameterType.Name=="IncidentPublisher"));
    }
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

    [Theory]
    [InlineData("disk", 1, "{}", "telemetry_payload_invalid")]
    [InlineData("disk", 1, "{\"Value\":{\"Volumes\":null,\"Health\":null}}", "telemetry_payload_invalid")]
    [InlineData("disk", 2, "{\"Value\":{\"Volumes\":[],\"Health\":[]}}", "telemetry_schema_unsupported")]
    [InlineData("disk", 1, "{\"Value\":{\"Volumes\":[null],\"Health\":[]}}", "telemetry_payload_invalid")]
    [InlineData("disk", 1, "{\"Value\":{\"Volumes\":[],\"Health\":[null]}}", "telemetry_payload_invalid")]
    [InlineData("process-services", 1, "{\"Value\":{\"Processes\":[],\"Services\":[null],\"ServiceChanges\":[]}}", "telemetry_payload_invalid")]
    [InlineData("network", 1, "{\"Value\":{\"Adapters\":[],\"Probes\":[null]}}", "telemetry_payload_invalid")]
    [InlineData("event-log", 1, "{\"Value\":{\"Events\":[null],\"Checkpoints\":[]}}", "telemetry_payload_invalid")]
    public async Task Malformed_optional_telemetry_snapshot_does_not_suppress_valid_cpu_diagnosis(
        string collectorId, int schemaVersion, string payload, string expectedFailureCode)
    {
        var now = DateTimeOffset.UtcNow;
        var directory = Path.Combine(Path.GetTempPath(), "HostAgentTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "malformed-telemetry.db");
        try
        {
            await using var store = new SqliteHostAgentStore(path);
            await store.InitializeAsync(default);
            for (var index = 0; index < 5; index++)
            {
                await store.SaveMetricSampleAsync(new(Guid.NewGuid(), "cpu.usage.percent", "host",
                    now.AddMinutes(-4 + index), 95), default);
            }

            var system = new SystemTelemetry("Windows", "test", "test", null, TimeSpan.FromDays(1), null);
            var cpu = new CpuMemoryTelemetry(95, 16L * 1024 * 1024 * 1024, 2L * 1024 * 1024 * 1024,
                null, null, [new(42, "SyntheticSaturator", 95, 1024, null)], []);
            await store.SaveTelemetrySnapshotAsync(new(Guid.NewGuid(), "system", 1, now,
                JsonSerializer.Serialize(new { Value = system })), default);
            await store.SaveTelemetrySnapshotAsync(new(Guid.NewGuid(), "cpu-memory", 1, now,
                JsonSerializer.Serialize(new { Value = cpu })), default);
            await store.SaveTelemetrySnapshotAsync(new(Guid.NewGuid(), collectorId, schemaVersion, now, payload), default);

            using var processor = new DiagnosticCycleProcessor(store);
            var incidents = await processor.ProcessAsync(now, default);

            var cpuIncident = Assert.Single(incidents, incident =>
                incident.LatestDiagnosis.ProbableRootCause.Code == "cpu_process_saturation");
            Assert.Equal(85, cpuIncident.LatestDiagnosis.Confidence);
            Assert.Equal(expectedFailureCode, processor.TelemetryFailureCode);
        }
        finally
        {
            if (Directory.Exists(directory)) ChaosFixtureRetention.DeleteDirectory(directory);
        }
    }

    private sealed class NetworkCollector(DateTimeOffset now) : ITelemetryCollector<NetworkTelemetry>
    {
        public DateTimeOffset Now {get;set;}=now;
        public string CollectorId => "network";
        public ValueTask<CollectorResult<NetworkTelemetry>> CollectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(CollectorResult<NetworkTelemetry>.Success(CollectorId, Now,
                new([], [new(NetworkProbeKind.Gateway, "192.0.2.1", NetworkProbeStatus.Failed, TimeSpan.Zero, null, null, "fixture")])));
    }
    private sealed class FailedJob : ITelemetryCollectionJob
    {
        public string CollectorId => "failed-peer";
        public ValueTask<CollectorStatus> CollectAndPersistAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("fixture");
    }
}
