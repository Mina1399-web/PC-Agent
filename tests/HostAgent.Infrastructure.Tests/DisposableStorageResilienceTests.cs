using HostAgent.Core.Models.Telemetry;
using HostAgent.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

// These are actual SQLite faults on tiny disposable databases, never on
// ProgramData or a host/shared drive. Page-limit exhaustion is an engine-level
// equivalent, not a claim that Windows disk-full behavior has been exercised.
public sealed class DisposableStorageResilienceTests
{
    [Fact]
    public async Task Corrupt_fixture_is_rejected_without_overwriting_its_bytes()
    {
        var path = FixturePath();
        var corrupt = Enumerable.Repeat((byte)0xA5, 1024).ToArray();
        try
        {
            await File.WriteAllBytesAsync(path, corrupt);
            await using var store = new SqliteHostAgentStore(path);
            var error = await Assert.ThrowsAsync<SqliteException>(async () => await store.InitializeAsync(default));
            Assert.Equal(26, error.SqliteErrorCode); // SQLITE_NOTADB
            Assert.Equal(corrupt, await File.ReadAllBytesAsync(path));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Page_limit_full_rolls_back_fixture_write_and_preserves_existing_product_data()
    {
        var path = FixturePath();
        var now = DateTimeOffset.UtcNow;
        try
        {
            await using (var store = new SqliteHostAgentStore(path))
                await store.SaveMetricSampleAsync(new(Guid.NewGuid(), "cpu.usage.percent", "host", now, 42), default);
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                using var limit = connection.CreateCommand();
                limit.CommandText = "PRAGMA max_page_count=1;";
                await limit.ExecuteScalarAsync(); // SQLite clamps to the existing page count.
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO telemetry_snapshots(snapshot_id,collector_id,payload_schema_version,sampled_at_utc,payload_json) VALUES('full-fixture','synthetic',1,$at,$payload);";
                insert.Parameters.AddWithValue("$at", now.ToString("O"));
                insert.Parameters.AddWithValue("$payload", System.Text.Json.JsonSerializer.Serialize(new string('x', 128 * 1024)));
                var error = await Assert.ThrowsAsync<SqliteException>(async () => await insert.ExecuteNonQueryAsync());
                Assert.Equal(13, error.SqliteErrorCode); // SQLITE_FULL
                using var check = connection.CreateCommand(); check.CommandText = "PRAGMA quick_check;";
                Assert.Equal("ok", await check.ExecuteScalarAsync());
            }
            await using var reopened = new SqliteHostAgentStore(path);
            Assert.Single(await reopened.ReadMetricSeriesAsync("cpu.usage.percent", "host", now.AddMinutes(-1), now.AddMinutes(1), 10, default));
            Assert.Empty(await reopened.ReadTelemetrySnapshotsAsync("synthetic", now.AddMinutes(-1), now.AddMinutes(1), 10, default));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task Uncommitted_fixture_transaction_is_absent_after_connection_interruption()
    {
        var path = FixturePath();
        var now = DateTimeOffset.UtcNow;
        try
        {
            await using (var store = new SqliteHostAgentStore(path))
                await store.SaveMetricSampleAsync(new(Guid.NewGuid(), "cpu.usage.percent", "host", now, 42), default);
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                using var transaction = connection.BeginTransaction();
                using var insert = connection.CreateCommand(); insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO baseline_samples(sample_id,metric_name,scope,sampled_at_utc,value) VALUES('uncommitted','cpu.usage.percent','host',$at,99);";
                insert.Parameters.AddWithValue("$at", now.ToString("O"));
                await insert.ExecuteNonQueryAsync();
                // Dispose without commit: cooperative interruption only.
            }
            await using var reopened = new SqliteHostAgentStore(path);
            var sample = Assert.Single(await reopened.ReadMetricSeriesAsync("cpu.usage.percent", "host", now.AddMinutes(-1), now.AddMinutes(1), 10, default));
            Assert.Equal(42, sample.Value);
        }
        finally { Cleanup(path); }
    }

    private static string FixturePath() => Path.Combine(Path.GetTempPath(), "HostAgent-disposable-" + Guid.NewGuid().ToString("N") + ".db");
    private static void Cleanup(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(path + suffix)) ChaosFixtureRetention.DeleteFile(path + suffix);
    }
}
