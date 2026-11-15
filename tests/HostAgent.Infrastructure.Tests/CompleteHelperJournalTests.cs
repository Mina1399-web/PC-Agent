using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using HostAgent.Infrastructure.Storage;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class CompleteHelperJournalTests
{
    [Fact]
    public async Task Read_only_helper_journal_reads_existing_audit_without_changing_database_bytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "helper-journal-readonly-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "helper.db");
        var requestId = Guid.NewGuid();
        try
        {
            await using (var writable = new SqliteHelperJournal(path, []))
                Assert.Empty(await writable.ReadAuditAsync(requestId, default));
            var before = SHA256.HashData(File.ReadAllBytes(path));
            var inventory = Directory.GetFileSystemEntries(root).Order().ToArray();
            await using (var readOnly = new SqliteHelperJournal(path, [], readOnly: true))
                Assert.Empty(await readOnly.ReadAuditAsync(requestId, default));
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(inventory, Directory.GetFileSystemEntries(root).Order().ToArray());
        }
        finally { ChaosFixtureRetention.DeleteDirectory(root); }
    }

    [Fact]
    public async Task Read_only_helper_journal_does_not_create_a_missing_database()
    {
        var root = Path.Combine(Path.GetTempPath(), "helper-journal-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "missing.db");
        try
        {
            await using var readOnly = new SqliteHelperJournal(path, [], readOnly: true);
            await Assert.ThrowsAnyAsync<SqliteException>(() => readOnly.ReadAuditAsync(Guid.NewGuid(), default).AsTask());
            Assert.False(File.Exists(path));
        }
        finally { ChaosFixtureRetention.DeleteDirectory(root); }
    }

    [Fact]
    public void Immutable_quick_check_keeps_fixture_bytes_inventory_and_rejects_unapplied_wal()
    {
        var configured = Environment.GetEnvironmentVariable("HOSTAGENT_DEV_FIXTURE_ROOT");
        var root = Path.Combine(string.IsNullOrWhiteSpace(configured) ? Path.GetTempPath() : configured,
            "complete-helper-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "private.db");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Pooling = false }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE fixture(value INTEGER); INSERT INTO fixture VALUES(1);";
                command.ExecuteNonQuery();
            }
            var before = SHA256.HashData(File.ReadAllBytes(path));
            var inventory = Directory.GetFileSystemEntries(root).Order().ToArray();
            Assert.Equal("ok", CompleteHelperProbe.ReadOnlyQuickCheck(path));
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(inventory, Directory.GetFileSystemEntries(root).Order().ToArray());
            File.WriteAllBytes(path + "-wal", [1]);
            Assert.Throws<InvalidOperationException>(() => CompleteHelperProbe.ReadOnlyQuickCheck(path));
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path + "-wal"));
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
        }
        finally { ChaosFixtureRetention.DeleteDirectory(root); }
    }
}
