using HostAgent.Core.Models.Telemetry;
using HostAgent.Core.Models.Audit;
using HostAgent.Core.Models.Storage;
using HostAgent.Core.Models.Policy;
using HostAgent.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class SqliteHostAgentStoreTests
{
    [Fact]
    public async Task Migration_and_parameterized_telemetry_round_trip_survive_reopen()
    {
        var databasePath = CreateDatabasePath();
        var timestamp = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(2));
        var payload = """{"label":"O'Connor; DROP TABLE telemetry_snapshots;--","city":"Žatec"}""";
        var expected = new TelemetrySnapshot(
            Guid.Parse("9d160ac3-8e1d-4117-a73a-0fe79f30e091"),
            "system",
            1,
            timestamp,
            payload);

        try
        {
            await using (var store = new SqliteHostAgentStore(databasePath))
            {
                await store.InitializeAsync(CancellationToken.None);

                Assert.Equal(2, await store.GetSchemaVersionAsync(CancellationToken.None));
                await store.SaveTelemetrySnapshotAsync(expected, CancellationToken.None);
                await store.SaveCollectorCheckpointAsync(
                    "event-log",
                    "System",
                    "<Bookmark />",
                    timestamp,
                    CancellationToken.None);
                Assert.Equal("<Bookmark />", await store.ReadCollectorCheckpointAsync(
                    "event-log",
                    "System",
                    CancellationToken.None));

                var stored = await store.ReadTelemetrySnapshotsAsync(
                    "system",
                    timestamp.AddHours(-1),
                    timestamp.AddHours(1),
                    10,
                    CancellationToken.None);

                var actual = Assert.Single(stored);
                Assert.Equal(expected.SnapshotId, actual.SnapshotId);
                Assert.Equal(expected.CollectorId, actual.CollectorId);
                Assert.Equal(expected.PayloadSchemaVersion, actual.PayloadSchemaVersion);
                Assert.Equal(timestamp.ToUniversalTime(), actual.SampledAtUtc);
                Assert.Equal(payload, actual.PayloadJson);
            }

            await using (var reopened = new SqliteHostAgentStore(databasePath))
            {
                await reopened.InitializeAsync(CancellationToken.None);
                Assert.Equal(2, await reopened.GetSchemaVersionAsync(CancellationToken.None));
                Assert.Equal("<Bookmark />", await reopened.ReadCollectorCheckpointAsync(
                    "event-log",
                    "System",
                    CancellationToken.None));
                Assert.Single(await reopened.ReadTelemetrySnapshotsAsync(
                    "system",
                    timestamp.AddHours(-1),
                    timestamp.AddHours(1),
                    10,
                    CancellationToken.None));
            }
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Schema_v1_migrates_to_v2_and_adds_checkpoint_storage()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using (var store = new SqliteHostAgentStore(databasePath))
            {
                await store.InitializeAsync(CancellationToken.None);
            }

            await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE collector_checkpoints; DELETE FROM schema_migrations WHERE version = 2;";
                await command.ExecuteNonQueryAsync();
            }

            await using var migrated = new SqliteHostAgentStore(databasePath);
            Assert.Equal(2, await migrated.GetSchemaVersionAsync(CancellationToken.None));
            await migrated.SaveCollectorCheckpointAsync(
                "event-log",
                "Application",
                "<Bookmark />",
                DateTimeOffset.UtcNow,
                CancellationToken.None);
            Assert.Equal("<Bookmark />", await migrated.ReadCollectorCheckpointAsync(
                "event-log",
                "Application",
                CancellationToken.None));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Collection_batch_rolls_back_snapshots_metrics_and_checkpoints_when_health_insert_fails()
    {
        var databasePath = CreateDatabasePath();
        var sampledAtUtc = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var health = new ServiceHealthSnapshot("event-log", sampledAtUtc, "Success", "{}");
        var snapshot = new TelemetrySnapshot(
            Guid.NewGuid(),
            "event-log",
            1,
            sampledAtUtc,
            """{"events":[]}""");
        var metric = new MetricSample(Guid.NewGuid(), "eventlog.event.count", "System:Warning", sampledAtUtc, 0);

        try
        {
            await using var store = new SqliteHostAgentStore(databasePath);
            await store.SaveServiceHealthAsync(health, CancellationToken.None);

            await Assert.ThrowsAsync<SqliteException>(() => store.SaveTelemetryCollectionAsync(
                snapshot,
                [metric],
                [new CollectorCheckpoint("System", "<Bookmark />")],
                health,
                CancellationToken.None).AsTask());

            Assert.Empty(await store.ReadTelemetrySnapshotsAsync(
                "event-log",
                sampledAtUtc.AddMinutes(-1),
                sampledAtUtc.AddMinutes(1),
                10,
                CancellationToken.None));
            Assert.Null(await store.ReadCollectorCheckpointAsync("event-log", "System", CancellationToken.None));

            await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            Assert.Equal(0L, await ReadInt64Async(connection, "SELECT COUNT(*) FROM baseline_samples;"));
            Assert.Equal(1L, await ReadInt64Async(connection, "SELECT COUNT(*) FROM service_health;"));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Initial_schema_contains_all_required_history_tables_and_audit_is_immutable()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using (var store = new SqliteHostAgentStore(databasePath))
            {
                await store.InitializeAsync(CancellationToken.None);
                await store.AppendAuditEntryAsync(
                    CreateAuditEntry("audit-1", DateTimeOffset.Parse("2026-10-06T12:00:00Z"), "proposal"),
                    CancellationToken.None);
            }

            await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync();
                var tables = new HashSet<string>(StringComparer.Ordinal);
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        tables.Add(reader.GetString(0));
                    }
                }

                var requiredTables = new[]
                {
                    "schema_migrations",
                    "telemetry_snapshots",
                    "baseline_samples",
                    "hourly_metric_aggregates",
                    "daily_metric_aggregates",
                    "incidents",
                    "incident_snapshots",
                    "diagnostic_evidence",
                    "audit_entries",
                    "remediation_attempts",
                    "service_health",
                    "collector_checkpoints"
                };
                Assert.All(requiredTables, table => Assert.Contains(table, tables));

                await using var update = connection.CreateCommand();
                update.CommandText = "UPDATE audit_entries SET requested_action = 'changed' WHERE audit_id = 'audit-1';";
                await Assert.ThrowsAsync<SqliteException>(() => update.ExecuteNonQueryAsync());

                await using var delete = connection.CreateCommand();
                delete.CommandText = "DELETE FROM audit_entries WHERE audit_id = 'audit-1';";
                await Assert.ThrowsAsync<SqliteException>(() => delete.ExecuteNonQueryAsync());
            }
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task Retention_aggregates_old_metrics_but_keeps_recent_telemetry_and_audit()
    {
        var databasePath = CreateDatabasePath();
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var oldSampleTime = now.AddDays(-31);
        var recentSampleTime = now.AddDays(-29);
        try
        {
            await using (var store = new SqliteHostAgentStore(databasePath))
            {
                await store.InitializeAsync(CancellationToken.None);
                await store.SaveMetricSampleAsync(
                    new MetricSample(
                        Guid.Parse("fb0aa93f-6516-45ba-88b6-d9c5f783628a"),
                        "memory.used.percent",
                        "host",
                        oldSampleTime,
                        42),
                    CancellationToken.None);
                await store.SaveMetricSampleAsync(
                    new MetricSample(
                        Guid.Parse("8029d49f-4e12-4cc8-a3b6-e2907a5e7550"),
                        "memory.used.percent",
                        "host",
                        recentSampleTime,
                        67),
                    CancellationToken.None);
                await store.SaveTelemetrySnapshotAsync(
                    new TelemetrySnapshot(
                        Guid.Parse("7360c638-a1d6-44cc-9b8b-6d230516813b"),
                        "memory",
                        1,
                        oldSampleTime,
                        """{"usedPercent":42}"""),
                    CancellationToken.None);
                await store.SaveTelemetrySnapshotAsync(
                    new TelemetrySnapshot(
                        Guid.Parse("b7dd48f7-6d20-46a8-a95b-5ac84cc03e43"),
                        "memory",
                        1,
                        recentSampleTime,
                        """{"usedPercent":67}"""),
                    CancellationToken.None);
                await store.SaveServiceHealthAsync(
                    new ServiceHealthSnapshot("old-collector", oldSampleTime, "Unavailable", "{}"),
                    CancellationToken.None);
                await store.SaveServiceHealthAsync(
                    new ServiceHealthSnapshot("recent-collector", recentSampleTime, "Success", "{}"),
                    CancellationToken.None);
                await store.AppendAuditEntryAsync(
                    CreateAuditEntry("audit-old", oldSampleTime, "old-proposal"),
                    CancellationToken.None);
                Assert.Equal(3, await store.ApplyRetentionAsync(
                    now,
                    TimeSpan.FromDays(30),
                    CancellationToken.None));
            }

            await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync();
                Assert.Equal(1L, await ReadInt64Async(connection, "SELECT COUNT(*) FROM baseline_samples;"));
                Assert.Equal(67d, await ReadDoubleAsync(
                    connection,
                    "SELECT value FROM baseline_samples LIMIT 1;"));
                Assert.Equal(1L, await ReadInt64Async(connection, "SELECT COUNT(*) FROM hourly_metric_aggregates;"));
                Assert.Equal(1L, await ReadInt64Async(connection, "SELECT sample_count FROM hourly_metric_aggregates;"));
                Assert.Equal(42d, await ReadDoubleAsync(connection, "SELECT sum_value FROM hourly_metric_aggregates;"));
                Assert.Equal(1L, await ReadInt64Async(connection, "SELECT COUNT(*) FROM daily_metric_aggregates;"));
                Assert.Equal(1L, await ReadInt64Async(connection, "SELECT sample_count FROM daily_metric_aggregates;"));
                Assert.Equal(1L, await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM telemetry_snapshots WHERE collector_id = 'memory';"));
                Assert.Equal(1L, await ReadInt64Async(connection, "SELECT COUNT(*) FROM service_health;"));
                Assert.Equal("recent-collector", await ReadStringAsync(
                    connection,
                    "SELECT component_id FROM service_health LIMIT 1;"));
                Assert.Equal(1L, await ReadInt64Async(connection, "SELECT COUNT(*) FROM audit_entries;"));
                Assert.Equal("old-proposal", await ReadStringAsync(
                    connection,
                    "SELECT requested_action FROM audit_entries LIMIT 1;"));
            }
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    private static string CreateDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"host-agent-{Guid.NewGuid():N}.db");

    private static AuditEntry CreateAuditEntry(string auditId, DateTimeOffset timestamp, string action) =>
        new(
            auditId,
            timestamp,
            "HostAgent.Service",
            null,
            "storage test",
            0,
            Array.Empty<string>(),
            action,
            PolicyDecision.CONFIRM_REQUIRED,
            ApprovalStatus.Pending,
            null,
            null,
            null,
            null);

    private static async Task<long> ReadInt64Async(SqliteConnection connection, string query)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = query;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<double> ReadDoubleAsync(SqliteConnection connection, string query)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = query;
        return Convert.ToDouble(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReadStringAsync(SqliteConnection connection, string query)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = query;
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

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
}
