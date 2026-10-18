using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class CompleteHelperJournalTests
{
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
