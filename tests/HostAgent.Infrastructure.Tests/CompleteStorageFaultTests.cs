using HostAgent.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class CompleteStorageFaultTests
{
    [Fact]
    public async Task Tiny_engine_quota_proves_full_without_large_physical_disk_allocation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HostAgentTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "quota.db");
            Assert.True(await CompleteStorageProbe.ProveSqliteQuotaAsync(path, default));
            Assert.InRange(new FileInfo(path).Length, 1, 65536);
        }
        finally { ChaosFixtureRetention.DeleteDirectory(directory); }
    }

    [Fact]
    public async Task Immutable_quick_check_preserves_fixture_files_and_rejects_nonempty_wal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HostAgentTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "private.db");
        try
        {
            await using (var store = new SqliteHostAgentStore(path)) await store.InitializeAsync(default);
            var before = Directory.GetFiles(directory).ToDictionary(file => file, file => System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
            Assert.True(await CompleteStorageProbe.QuickCheckReadOnlyAsync(path, default));
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(directory).Order());
            foreach (var entry in before) Assert.Equal(entry.Value, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(entry.Key)));
            await File.WriteAllBytesAsync(path + "-wal", [1]);
            Assert.False(await CompleteStorageProbe.QuickCheckReadOnlyAsync(path, default));
        }
        finally { ChaosFixtureRetention.DeleteDirectory(directory); }
    }

    [Fact]
    public void Soak_trend_rejects_insufficient_observation_and_bounded_resource_growth_violation()
    {
        Assert.False(CompleteSoakProbe.EvaluateTrend([]).Accepted);
        var samples = Enumerable.Range(0, 40).Select(index => new CompleteSoakProbe.SoakSample(120 + index * 10,
            50_000_000, 80_000_000, 100, 12, 1_000_000, 0)).ToArray();
        Assert.True(CompleteSoakProbe.EvaluateTrend(samples).Accepted);
        var leak = samples.Select((sample, index) => sample with { PrivateBytes = sample.PrivateBytes + index * 10_000_000 }).ToArray();
        Assert.False(CompleteSoakProbe.EvaluateTrend(leak).Accepted);
    }

    [Fact]
    public async Task Private_write_boundary_full_keeps_reads_available_and_recovers_without_replacing_store()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HostAgentTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "private.db");
        try
        {
            await using var inner = new SqliteHostAgentStore(path);
            await inner.InitializeAsync(default);
            var store = CompleteStorageWriteFault.Create(inner, out var fault);
            fault.Enabled = true;
            var error = await Assert.ThrowsAsync<SqliteException>(() => store.SaveMetricSampleAsync(
                new(Guid.NewGuid(), "cpu.usage.percent", "host", DateTimeOffset.UtcNow, 10), default).AsTask());
            Assert.Equal(13, error.SqliteErrorCode);
            Assert.Equal(1, fault.InjectedFailures);
            Assert.Empty(await store.ReadMetricSamplesAsync(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, 4, default));
            fault.Enabled = false;
            await store.SaveMetricSampleAsync(new(Guid.NewGuid(), "cpu.usage.percent", "host", DateTimeOffset.UtcNow, 10), default);
            Assert.Single(await store.ReadMetricSamplesAsync(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, 4, default));
        }
        finally { ChaosFixtureRetention.DeleteDirectory(directory); }
    }
}
