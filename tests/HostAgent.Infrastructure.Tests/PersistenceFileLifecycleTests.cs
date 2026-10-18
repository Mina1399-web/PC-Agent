using HostAgent.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class PersistenceFileLifecycleTests
{
    [Fact]
    public async Task Unused_helper_journal_is_lazy_and_first_journal_access_creates_healthy_storage()
    {
        var path = Path.Combine(Path.GetTempPath(), "helper-lifecycle-" + Guid.NewGuid() + ".db");
        try
        {
            await using var journal = new SqliteHelperJournal(path, []);
            Assert.False(File.Exists(path));
            Assert.Empty(await journal.ReadAuditAsync(Guid.NewGuid(), default));
            Assert.True(File.Exists(path));
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            Assert.Equal("ok", await command.ExecuteScalarAsync());
        }
        finally { ChaosFixtureRetention.DeleteFile(path); }
    }

    [Fact]
    public async Task Quiescent_host_store_can_have_wal_header_without_live_sidecars()
    {
        var path = Path.Combine(Path.GetTempPath(), "store-lifecycle-" + Guid.NewGuid() + ".db");
        try
        {
            await using var store = new SqliteHostAgentStore(path);
            await store.InitializeAsync(default);
            var header = new byte[100];
            using (var input = File.OpenRead(path)) { input.ReadExactly(header); }
            Assert.Equal(2, header[18]);
            Assert.Equal(2, header[19]);
            Assert.False(File.Exists(path + "-wal"));
            Assert.False(File.Exists(path + "-shm"));
            // The initialized store remains usable on its next ordinary connection.
            Assert.Empty(await store.ReadIncidentHeadsAsync(default));
        }
        finally { ChaosFixtureRetention.DeleteFile(path); }
    }
}
