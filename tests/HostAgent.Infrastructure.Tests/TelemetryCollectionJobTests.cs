using System.Text.Json;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Core.Orchestration;
using HostAgent.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class TelemetryCollectionJobTests
{
    private static readonly DateTimeOffset SampleTime =
        new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Successful_collection_persists_versioned_payload_and_health()
    {
        var databasePath = CreateDatabasePath();
        var collector = new FakeCollector<SamplePayload>(
            "sample",
            _ => ValueTask.FromResult(CollectorResult<SamplePayload>.Success(
                "sample",
                SampleTime,
                new SamplePayload(42))));

        try
        {
            await using var store = new SqliteHostAgentStore(databasePath);
            var job = new TelemetryCollectionJob<SamplePayload>(collector, store, new SampleMetricProjector());

            var status = await job.CollectAndPersistAsync(CancellationToken.None);

            Assert.Equal(CollectorStatus.Success, status);
            var snapshot = Assert.Single(await store.ReadTelemetrySnapshotsAsync(
                "sample",
                SampleTime.AddMinutes(-1),
                SampleTime.AddMinutes(1),
                10,
                CancellationToken.None));
            Assert.Equal(1, snapshot.PayloadSchemaVersion);
            using var payload = JsonDocument.Parse(snapshot.PayloadJson);
            Assert.Equal((int)CollectorStatus.Success, payload.RootElement.GetProperty("Status").GetInt32());
            Assert.Equal(42, payload.RootElement.GetProperty("Value").GetProperty("Value").GetInt32());
            Assert.Equal("sample-checkpoint", await store.ReadCollectorCheckpointAsync(
                "sample",
                "sample-key",
                CancellationToken.None));

            await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT status FROM service_health WHERE component_id = $componentId;";
            command.Parameters.AddWithValue("$componentId", "sample");
            Assert.Equal("Success", await command.ExecuteScalarAsync());

            await using var metricCommand = connection.CreateCommand();
            metricCommand.CommandText = "SELECT value FROM baseline_samples WHERE metric_name = 'sample.value';";
            Assert.Equal(42d, Convert.ToDouble(await metricCommand.ExecuteScalarAsync()));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Unavailable_collection_persists_health_without_fabricating_telemetry()
    {
        var databasePath = CreateDatabasePath();
        var collector = new FakeCollector<SamplePayload>(
            "sample",
            _ => ValueTask.FromResult(CollectorResult<SamplePayload>.Unavailable(
                "sample",
                SampleTime,
                "access_denied")));

        try
        {
            await using var store = new SqliteHostAgentStore(databasePath);
            var job = new TelemetryCollectionJob<SamplePayload>(collector, store);

            var status = await job.CollectAndPersistAsync(CancellationToken.None);

            Assert.Equal(CollectorStatus.Unavailable, status);
            Assert.Empty(await store.ReadTelemetrySnapshotsAsync(
                "sample",
                SampleTime.AddMinutes(-1),
                SampleTime.AddMinutes(1),
                10,
                CancellationToken.None));
            await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload_json FROM service_health WHERE component_id = $componentId;";
            command.Parameters.AddWithValue("$componentId", "sample");
            var healthPayload = Assert.IsType<string>(await command.ExecuteScalarAsync());
            Assert.Contains("access_denied", healthPayload, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Collection_cancellation_is_forwarded_and_does_not_write_a_sample()
    {
        var databasePath = CreateDatabasePath();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var collector = new FakeCollector<SamplePayload>(
            "sample",
            token =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult(CollectorResult<SamplePayload>.Success(
                    "sample",
                    SampleTime,
                    new SamplePayload(42)));
            });

        try
        {
            await using var store = new SqliteHostAgentStore(databasePath);
            var job = new TelemetryCollectionJob<SamplePayload>(collector, store);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => job.CollectAndPersistAsync(cancellation.Token).AsTask());

            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    private static string CreateDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"host-agent-job-{Guid.NewGuid():N}.db");

    private static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed record SamplePayload(int Value) : ICheckpointedTelemetry
    {
        public IReadOnlyList<CollectorCheckpoint> Checkpoints => [new("sample-key", "sample-checkpoint")];
    }

    private sealed class SampleMetricProjector : ITelemetryMetricProjector<SamplePayload>
    {
        public IReadOnlyList<MetricSample> Project(SamplePayload payload, DateTimeOffset sampledAtUtc) =>
            [new MetricSample(Guid.NewGuid(), "sample.value", "host", sampledAtUtc, payload.Value)];
    }

    private sealed class FakeCollector<TPayload>(
        string collectorId,
        Func<CancellationToken, ValueTask<CollectorResult<TPayload>>> collect) : ITelemetryCollector<TPayload>
        where TPayload : class
    {
        public string CollectorId { get; } = collectorId;

        public ValueTask<CollectorResult<TPayload>> CollectAsync(CancellationToken cancellationToken) =>
            collect(cancellationToken);
    }
}
