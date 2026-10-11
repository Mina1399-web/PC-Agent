using System.Reflection;
using System.Text.Json;
using HostAgent.Core.Diagnostics;
using HostAgent.Core.Models.Baseline;
using HostAgent.Core.Models.Diagnosis;
using HostAgent.Core.Models.Incidents;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class Task2PersistenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-11T12:00:00Z");

    [Fact]
    public async Task Incident_bundle_is_atomic_survives_reopen_and_retention_and_deduplicates_after_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "HostAgentTests", Guid.NewGuid().ToString("N"), "task2.db");
        await using var store = new SqliteHostAgentStore(path);
        var save = Method("SaveIncidentBundleAsync");
        var read = Method("ReadIncidentsAsync");
        var engine = new IncidentEngine();
        var diagnosis = new Diagnosis("diagnosis", "service:SPOOLER", Now, DiagnosisSeverity.High, 78,
            [new("ev", "scm", "crash", null, Now)], new("service_crash", "crash", "Spooler"), [], new("inspect", "inspect"), IncidentLifecycleState.Detected);
        var original = await engine.ApplyAsync(diagnosis, Snapshot(), CancellationToken.None);
        await Save(save, store, original);
        await using (var reopened = new SqliteHostAgentStore(path))
        {
            var restored = await Read(read, reopened);
            var actual = Assert.Single(restored);
            Assert.Equal("Windows 11", actual.Snapshots[0].System!.ProductName);
            Assert.Equal(78, actual.LatestDiagnosis.Confidence);
            Assert.Equal("Spooler", actual.Snapshots[0].ProcessServices!.Services[0].ServiceName);
            Assert.Single(actual.Snapshots[0].CpuMemory!.TopProcessesByCpu);
            Assert.Single(actual.Snapshots[0].Network!.Probes);
            Assert.Single(actual.Snapshots[0].EventLog!.Events);
            Assert.Single(actual.Snapshots[0].Baselines);
            var restartedEngine = new IncidentEngine();
            restartedEngine.Restore(restored);
            var updated = await restartedEngine.ApplyAsync(diagnosis with { ObservedAtUtc = Now.AddMinutes(1), Evidence = [new("ev2", "scm", "crash", null, Now.AddMinutes(1))] }, Snapshot() with { SnapshotId = "snapshot2", CapturedAtUtc = Now.AddMinutes(1) }, CancellationToken.None);
            Assert.Equal(original.IncidentId, updated.IncidentId);
            Assert.Equal(2, updated.RecurrenceCount);
            await Save(save, reopened, updated);
            await reopened.ApplyRetentionAsync(Now.AddDays(40), TimeSpan.FromDays(30), CancellationToken.None);
            Assert.Equal(2, Assert.Single(await Read(read, reopened)).Snapshots.Count);
        }
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using (var trigger = connection.CreateCommand())
        {
            trigger.CommandText = "CREATE TRIGGER fail_bundle BEFORE INSERT ON diagnostic_evidence BEGIN SELECT RAISE(ABORT, 'fixture'); END;";
            await trigger.ExecuteNonQueryAsync();
        }
        var bad = original with { IncidentId = "must-rollback", Snapshots = [Snapshot() with { SnapshotId = "bad-snapshot", IncidentId = "must-rollback", DiagnosticEvidence = [new("bad-ev", "scm", "crash", null, Now)] }] };
        await Assert.ThrowsAsync<SqliteException>(() => Save(save, store, bad));
        Assert.Single(await Read(read, store));
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM incident_snapshots WHERE incident_id = 'must-rollback';";
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Baseline_origin_and_recent_samples_survive_retention_and_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "HostAgentTests", Guid.NewGuid().ToString("N"), "baseline.db");
        await using var store = new SqliteHostAgentStore(path);
        var readSamples = Method("ReadMetricSamplesAsync");
        var readOrigins = Method("ReadBaselineOriginsAsync");
        var restore = typeof(BaselineEngine).GetMethod("RestoreAsync");
        Assert.NotNull(restore);
        for (var day = -40; day <= 0; day++)
            await store.SaveMetricSampleAsync(new(Guid.NewGuid(), "test", "host", Now.AddDays(day), 10), CancellationToken.None);
        await store.ApplyRetentionAsync(Now, TimeSpan.FromDays(30), CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ApplyRetentionAsync(Now, TimeSpan.FromDays(29), CancellationToken.None).AsTask());
        Assert.Equal(0, await store.ApplyRetentionAsync(Now, TimeSpan.FromDays(30), CancellationToken.None));
        foreach (var granularity in new[] { AggregateGranularity.Hourly, AggregateGranularity.Daily })
        {
            var aggregates = await store.ReadMetricAggregatesAsync(granularity, "test", "host", Now.AddDays(-41), Now, CancellationToken.None);
            Assert.Equal(10, aggregates.Sum(aggregate => aggregate.SampleCount));
            Assert.Equal(100, aggregates.Sum(aggregate => aggregate.Sum));
            Assert.All(aggregates, aggregate => { Assert.Equal(10, aggregate.Minimum); Assert.Equal(10, aggregate.Maximum); });
        }
        var samples = await (ValueTask<IReadOnlyList<MetricSample>>)readSamples.Invoke(store, [Now.AddDays(-30), Now, 1000, CancellationToken.None])!;
        Assert.Equal(31, samples.Count);
        var originsTask = readOrigins.Invoke(store, [CancellationToken.None])!;
        var origins = await AwaitResult(originsTask);
        var engine = new BaselineEngine();
        await (ValueTask)restore!.Invoke(engine, [samples, origins!, CancellationToken.None])!;
        var assessment = await engine.ObserveAsync("test", "host", Now.AddMinutes(1), 80);
        Assert.Equal(BaselineLearningMode.Full, assessment.LearningMode);
        Assert.Equal(30, assessment.SampleCount);
        Assert.Equal(100, assessment.Confidence);
        Assert.Equal(10, assessment.Median);
    }

    private static MethodInfo Method(string name)
    {
        var method = typeof(SqliteHostAgentStore).GetMethod(name);
        Assert.NotNull(method);
        return method!;
    }

    [Fact]
    public async Task Operational_restore_is_bounded_while_full_snapshot_archive_remains_durable()
    {
        var readHeads = Method("ReadIncidentHeadsAsync");
        var path = Path.Combine(Path.GetTempPath(), "HostAgentTests", Guid.NewGuid().ToString("N"), "archive.db");
        await using var store = new SqliteHostAgentStore(path);
        var diagnosis = new Diagnosis("diag", "disk:C", Now, DiagnosisSeverity.Critical, 82, [], new("disk_capacity", "full", "C"), [], new("inspect", "inspect"), IncidentLifecycleState.Active);
        var snapshots = Enumerable.Range(0, 70).Select(index => Snapshot() with { SnapshotId = "archive-" + index, IncidentId = "archive", CapturedAtUtc = Now.AddMinutes(index) }).ToArray();
        await store.SaveIncidentBundleAsync(new("archive", "disk:C", IncidentLifecycleState.Active, Now, Now.AddMinutes(69), 70, diagnosis, snapshots), CancellationToken.None);
        var head = Assert.Single(await Read(readHeads, store));
        Assert.Equal(64, head.Snapshots.Count);
        Assert.Equal(Now.AddMinutes(6), head.Snapshots[0].CapturedAtUtc);
        Assert.Equal(Now.AddMinutes(69), head.Snapshots[^1].CapturedAtUtc);
        Assert.Equal(70, Assert.Single(await store.ReadIncidentsAsync(CancellationToken.None)).Snapshots.Count);
    }
    private static async Task Save(MethodInfo method, SqliteHostAgentStore store, Incident incident)
    {
        try { await (ValueTask)method.Invoke(store, [incident, CancellationToken.None])!; }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
    }
    private static async Task<IReadOnlyList<Incident>> Read(MethodInfo method, SqliteHostAgentStore store) =>
        await (ValueTask<IReadOnlyList<Incident>>)method.Invoke(store, [CancellationToken.None])!;
    private static async Task<object?> AwaitResult(object valueTask)
    {
        var task = (Task)valueTask.GetType().GetMethod("AsTask")!.Invoke(valueTask, null)!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }
    private static IncidentSnapshot Snapshot() => new("snapshot1", "", Now,
        new("Windows 11", "10", "test", Now.AddHours(-1), TimeSpan.FromHours(1), null),
        new(50, 1000, 500, null, null, [new(17, "Worker", 50, 10, null)], []),
        new([new("C:\\", "System", "NTFS", 100, 98, 2, 98)], null, []),
        new([], [new("Spooler", "Print Spooler", HostServiceStatus.Stopped)], []),
        new([], [new(NetworkProbeKind.Gateway, "192.0.2.1", NetworkProbeStatus.Succeeded, TimeSpan.Zero, 1, null, null)]),
        new(Now, [new("System", "Service Control Manager", null, 7031, "Error", Now, "host") { AffectedServiceName = "Spooler" }], []),
        [new("test", "host", Now, 98, 5, 10, 10, 0, 0.5, 10, 10, 10, 0, TimeSpan.Zero, 1, BaselineLearningMode.Full, AdaptiveSignal.Anomaly, 80)], []);
}
