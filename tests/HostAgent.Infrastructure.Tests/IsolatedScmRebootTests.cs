using System.Text.Json;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class IsolatedScmRebootTests
{
    [Fact]
    public async Task Reboot_mode_is_recognized_but_cannot_run_outside_scm()
    {
        Assert.Equal(3, await IsolatedScmServiceProbe.RunServiceAsync(new[] {
            "isolated-scm-service", Guid.NewGuid().ToString("N"), "S-1-5-18", "reboot-proof" }));
    }

    [Fact]
    public async Task Poweroff_mode_is_recognized_but_cannot_run_outside_scm()
    {
        Assert.Equal(3, await IsolatedScmServiceProbe.RunServiceAsync(new[] {
            "isolated-scm-service", Guid.NewGuid().ToString("N"), "S-1-5-18", "poweroff-proof" }));
    }

    [Fact]
    public async Task Ready_proves_two_commits_pending_stays_invisible_and_graceful_stop_rolls_back_both()
    {
        using var fixture = await Fixture.CreateAsync();
        using var safety = fixture.Safety();
        using var stop = new CancellationTokenSource();
        using var worker = new IsolatedScmRebootWorker(fixture.Root, fixture.Id, safety);
        var run = worker.RunProofAsync(stop.Token);
        await Task.Delay(100);
        Assert.False(File.Exists(fixture.File("reboot-proof-ready.json")));
        await System.IO.File.WriteAllTextAsync(fixture.File("reboot-proof-arm.flag"), "arm");
        await WaitForAsync(() => System.IO.File.Exists(fixture.File("reboot-proof-ready.json")));
        using var ready = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(fixture.File("reboot-proof-ready.json")));
        Assert.Equal(fixture.Id, ready.RootElement.GetProperty("runId").GetString());
        Assert.True(ready.RootElement.GetProperty("committed").GetBoolean());
        Assert.Equal(2, ready.RootElement.GetProperty("pendingTransactions").GetInt32());
        Assert.Single(ready.RootElement.GetProperty("snapshotIds").EnumerateArray());
        foreach (var database in fixture.Databases) AssertCommittedOnly(database, fixture.Id);
        stop.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        using var closed = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(fixture.File("reboot-proof-closed.json")));
        Assert.Equal("graceful_stop", closed.RootElement.GetProperty("reason").GetString());
        foreach (var database in fixture.Databases) AssertCommittedOnly(database, fixture.Id);
        Assert.False(System.IO.File.Exists(fixture.File("reboot-safety-abort.json")));
    }

    [Fact]
    public async Task Restart_preserves_ready_commits_and_records_only_one_post_start()
    {
        using var fixture = await Fixture.CreateAsync();
        await PrepareAndCloseAsync(fixture);
        var ready = await System.IO.File.ReadAllBytesAsync(fixture.File("reboot-proof-ready.json"));
        using var safety = fixture.Safety();
        using var stop = new CancellationTokenSource();
        using var worker = new IsolatedScmRebootWorker(fixture.Root, fixture.Id, safety);
        var run = worker.RunProofAsync(stop.Token);
        await WaitForAsync(() => System.IO.File.Exists(fixture.File("reboot-after-start.json")));
        stop.Cancel(); await run;
        Assert.Equal(ready, await System.IO.File.ReadAllBytesAsync(fixture.File("reboot-proof-ready.json")));
        foreach (var database in fixture.Databases) AssertCommittedOnly(database, fixture.Id, postBoot: true);
        using var third = new IsolatedScmRebootWorker(fixture.Root, fixture.Id, safety);
        await Assert.ThrowsAsync<InvalidOperationException>(() => third.RunProofAsync(default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restart_rejects_missing_graceful_close_or_wrong_run_id(bool wrongId)
    {
        using var fixture = await Fixture.CreateAsync();
        await System.IO.File.WriteAllTextAsync(fixture.File("reboot-proof-ready.json"), JsonSerializer.Serialize(new {
            runId = wrongId ? Guid.NewGuid().ToString("N") : fixture.Id,
            committed = true, pendingTransactions = 2, snapshotIds = new[] { "snapshot" }, readyAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) }));
        if (wrongId) await System.IO.File.WriteAllTextAsync(fixture.File("reboot-proof-closed.json"), JsonSerializer.Serialize(new {
            runId = fixture.Id, reason = "graceful_stop", closedAtUtc = DateTimeOffset.UtcNow }));
        using var safety = fixture.Safety();
        using var worker = new IsolatedScmRebootWorker(fixture.Root, fixture.Id, safety);
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunProofAsync(default));
        Assert.False(System.IO.File.Exists(fixture.File("reboot-after-start.json")));
    }

    [Theory]
    [InlineData(767.9, "reboot_soft_memory_floor_768_mib")]
    [InlineData(512, "reboot_hard_memory_floor_512_mib")]
    public async Task Resource_abort_stops_own_host_rolls_back_and_never_claims_graceful_close(double free, string code)
    {
        using var fixture = await Fixture.CreateAsync();
        var memory = 2048d;
        var stopRequests = 0;
        using var safety = fixture.Safety(() => stopRequests++, () => memory);
        using var worker = new IsolatedScmRebootWorker(fixture.Root, fixture.Id, safety);
        await System.IO.File.WriteAllTextAsync(fixture.File("reboot-proof-arm.flag"), "arm");
        var run = worker.RunProofAsync(default);
        await WaitForAsync(() => System.IO.File.Exists(fixture.File("reboot-proof-ready.json")));
        memory = free; safety.CheckNow();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitForAsync(() => Volatile.Read(ref stopRequests) == 1
            && System.IO.File.Exists(fixture.File("reboot-safety-abort.json")));
        Assert.Equal(code, safety.FailureCode); Assert.Equal(1, stopRequests);
        Assert.True(System.IO.File.Exists(fixture.File("reboot-safety-abort.json")));
        Assert.False(System.IO.File.Exists(fixture.File("reboot-proof-closed.json")));
        foreach (var database in fixture.Databases) AssertCommittedOnly(database, fixture.Id);
    }

    [Fact]
    public async Task Independent_guard_thread_requests_stop_without_worker_progress()
    {
        using var fixture = await Fixture.CreateAsync();
        using var stopped = new ManualResetEventSlim();
        using var safety = fixture.Safety(stopped.Set, () => 767);
        safety.Start();
        Assert.True(stopped.Wait(TimeSpan.FromSeconds(3)));
        await WaitForAsync(() => System.IO.File.Exists(fixture.File("reboot-safety-abort.json")));
        Assert.Equal("reboot_soft_memory_floor_768_mib", safety.FailureCode);
    }

    [Fact]
    public async Task Fixed_lease_and_ten_second_fallback_are_distinct_from_graceful_stop()
    {
        using var fixture = await Fixture.CreateAsync();
        var elapsed = TimeSpan.FromSeconds(899);
        var stopRequests = 0; var exits = 0;
        using var safety = fixture.Safety(() => stopRequests++, elapsed: () => elapsed, exit: code => { Assert.Equal(73, code); exits++; });
        safety.CheckNow(); Assert.Null(safety.FailureCode);
        elapsed = TimeSpan.FromSeconds(900); safety.CheckNow();
        await WaitForAsync(() => Volatile.Read(ref stopRequests) == 1
            && System.IO.File.Exists(fixture.File("reboot-safety-abort.json")));
        Assert.Equal("reboot_lease_expired", safety.FailureCode); Assert.Equal(1, stopRequests); Assert.Equal(0, exits);
        elapsed += TimeSpan.FromSeconds(9); safety.CheckNow(); Assert.Equal(0, exits);
        elapsed += TimeSpan.FromSeconds(1); safety.CheckNow(); safety.CheckNow(); Assert.Equal(1, exits);
        Assert.False(System.IO.File.Exists(fixture.File("reboot-proof-closed.json")));
    }

    [Fact]
    public async Task Guard_preserves_exact_memory_disk_boundaries_and_disposes_idempotently()
    {
        using var fixture = await Fixture.CreateAsync();
        var disk = 20L * 1024 * 1024 * 1024;
        var safety = fixture.Safety(free: () => 768, disk: () => disk);
        safety.CheckNow(); Assert.Null(safety.FailureCode);
        disk--; safety.CheckNow(); Assert.Equal("reboot_disk_floor_20_gib", safety.FailureCode);
        await WaitForAsync(() => System.IO.File.Exists(fixture.File("reboot-safety-abort.json")));
        safety.Dispose(); safety.Dispose();
    }

    [Fact]
    public async Task Blocked_abort_marker_io_cannot_block_stop_request_or_ten_second_fallback()
    {
        using var fixture = await Fixture.CreateAsync();
        using var writerEntered = new ManualResetEventSlim();
        using var writerReleased = new ManualResetEventSlim();
        using var writerCompleted = new ManualResetEventSlim();
        using var stopped = new ManualResetEventSlim();
        using var exited = new ManualResetEventSlim();
        long elapsedTicks = 0;
        using var safety = fixture.Safety(stopped.Set, free: () => 767,
            elapsed: () => TimeSpan.FromTicks(Volatile.Read(ref elapsedTicks)),
            exit: _ => exited.Set(), marker: (_, _) => {
                writerEntered.Set(); writerReleased.Wait(); writerCompleted.Set();
            });
        try
        {
            safety.Start();
            Assert.True(writerEntered.Wait(TimeSpan.FromSeconds(3)));
            Volatile.Write(ref elapsedTicks, TimeSpan.FromSeconds(10).Ticks);
            var stopObserved = stopped.Wait(TimeSpan.FromSeconds(2));
            var fallbackObserved = exited.Wait(TimeSpan.FromSeconds(2));
            Assert.True(stopObserved && fallbackObserved,
                $"Blocked marker writer prevented stop={stopObserved}, fallback={fallbackObserved}");
        }
        finally
        {
            writerReleased.Set();
            Assert.True(writerCompleted.Wait(TimeSpan.FromSeconds(3)));
        }
    }

    [Fact]
    public async Task Blocked_stop_and_cancellation_callbacks_cannot_block_ten_second_fallback()
    {
        using var fixture = await Fixture.CreateAsync();
        using var released = new ManualResetEventSlim();
        using var stopEntered = new ManualResetEventSlim();
        using var stopCompleted = new ManualResetEventSlim();
        using var cancellationEntered = new ManualResetEventSlim();
        using var cancellationCompleted = new ManualResetEventSlim();
        using var exited = new ManualResetEventSlim();
        long elapsedTicks = 0;
        using var safety = fixture.Safety(() => {
            stopEntered.Set(); released.Wait(); stopCompleted.Set();
        }, free: () => 767, elapsed: () => TimeSpan.FromTicks(Volatile.Read(ref elapsedTicks)),
            exit: _ => exited.Set(), marker: (_, _) => { });
        using var registration = safety.AbortToken.Register(() => {
            cancellationEntered.Set(); released.Wait(); cancellationCompleted.Set();
        });
        try
        {
            safety.Start();
            Assert.True(cancellationEntered.Wait(TimeSpan.FromSeconds(3)));
            Volatile.Write(ref elapsedTicks, TimeSpan.FromSeconds(10).Ticks);
            var stopObserved = stopEntered.Wait(TimeSpan.FromSeconds(2));
            var fallbackObserved = exited.Wait(TimeSpan.FromSeconds(2));
            Assert.True(stopObserved && fallbackObserved,
                $"Blocking callbacks prevented independent stop={stopObserved}, fallback={fallbackObserved}");
        }
        finally
        {
            released.Set();
            Assert.True(cancellationCompleted.Wait(TimeSpan.FromSeconds(3)));
            Assert.True(stopCompleted.Wait(TimeSpan.FromSeconds(3)));
        }
    }

    [Fact]
    public async Task Poweroff_ready_binds_mode_and_graceful_stop_is_not_power_loss()
    {
        using var fixture = await Fixture.CreateAsync();
        using var safety = fixture.Safety();
        using var stop = new CancellationTokenSource();
        using var worker = new IsolatedScmRebootWorker(fixture.Root, fixture.Id, safety, "poweroff-proof");
        await System.IO.File.WriteAllTextAsync(fixture.File("reboot-proof-arm.flag"), "arm");
        var run = worker.RunProofAsync(stop.Token);
        await WaitForAsync(() => System.IO.File.Exists(fixture.File("reboot-proof-ready.json")));
        try
        {
            using var ready = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(fixture.File("reboot-proof-ready.json")));
            Assert.True(ready.RootElement.TryGetProperty("mode", out var mode), "Ready must bind the poweroff mode.");
            Assert.Equal("poweroff-proof", mode.GetString());
            using (var notifications = new SqliteConnection($"Data Source={fixture.Databases[1]};Mode=ReadOnly;Pooling=False"))
            {
                notifications.Open();
                using var modeQuery = notifications.CreateCommand();
                modeQuery.CommandText = "PRAGMA journal_mode;";
                Assert.Equal("delete", modeQuery.ExecuteScalar());
            }
            Assert.True(new FileInfo(fixture.Databases[0] + "-wal").Length > 0);
            Assert.True(new FileInfo(fixture.Databases[1] + "-journal").Length > 0);
            foreach (var database in fixture.Databases) AssertCommittedOnly(database, fixture.Id);
        }
        finally { stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(10)); }
        using var second = new IsolatedScmRebootWorker(fixture.Root, fixture.Id, safety, "poweroff-proof");
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.RunProofAsync(default));
        Assert.False(System.IO.File.Exists(fixture.File("reboot-after-start.json")));
    }

    [Theory]
    [InlineData("poweroff-proof", false)]
    [InlineData("reboot-proof", true)]
    [InlineData("", true)]
    public async Task Poweroff_restart_requires_its_bound_ready_and_absent_graceful_close(string mode, bool rejected)
    {
        using var fixture = await Fixture.CreateAsync();
        // A clean rollback fixture tests validation, not physical power-loss recovery.
        using (var proof = new IsolatedScmCrashProof()) proof.Prepare(fixture.Databases, fixture.Id);
        var before = JsonSerializer.Serialize(new {
            runId = fixture.Id, mode, committed = true, pendingTransactions = 2,
            snapshotIds = new[] { "snapshot" }, readyAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) });
        await System.IO.File.WriteAllTextAsync(fixture.File("reboot-proof-ready.json"), before);
        using var safety = fixture.Safety();
        using var stop = new CancellationTokenSource();
        using var worker = new IsolatedScmRebootWorker(fixture.Root, fixture.Id, safety, "poweroff-proof");
        var run = worker.RunProofAsync(stop.Token);
        if (rejected)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => run);
            Assert.False(System.IO.File.Exists(fixture.File("reboot-after-start.json")));
            foreach (var database in fixture.Databases) AssertCommittedOnly(database, fixture.Id);
        }
        else
        {
            await WaitForAsync(() => System.IO.File.Exists(fixture.File("reboot-after-start.json")));
            stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(10));
            using var after = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(fixture.File("reboot-after-start.json")));
            Assert.Equal("poweroff-proof", after.RootElement.GetProperty("mode").GetString());
            Assert.Equal(before, await System.IO.File.ReadAllTextAsync(fixture.File("reboot-proof-ready.json")));
            foreach (var database in fixture.Databases) AssertCommittedOnly(database, fixture.Id, postBoot: true);
            using var repeated = new IsolatedScmRebootWorker(fixture.Root, fixture.Id, safety, "poweroff-proof");
            await Assert.ThrowsAsync<InvalidOperationException>(() => repeated.RunProofAsync(default));
        }
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Poweroff_restart_recovers_after_transient_startup_writer_contention(int databaseIndex)
    {
        using var fixture = await Fixture.CreateAsync();
        using (var proof = new IsolatedScmCrashProof()) proof.Prepare(fixture.Databases, fixture.Id);
        await System.IO.File.WriteAllTextAsync(fixture.File("reboot-proof-ready.json"), JsonSerializer.Serialize(new {
            runId = fixture.Id, mode = "poweroff-proof", committed = true, pendingTransactions = 2,
            snapshotIds = new[] { "snapshot" }, readyAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) }));
        using var writer = new SqliteConnection($"Data Source={fixture.Databases[databaseIndex]};Mode=ReadWrite;Pooling=False");
        writer.Open();
        using var transaction = writer.BeginTransaction();
        using var command = writer.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE dev_scm_crash_proof SET kind=kind;";
        command.ExecuteNonQuery();
        using var safety = fixture.Safety();
        using var stop = new CancellationTokenSource();
        using var worker = new IsolatedScmRebootWorker(fixture.Root, fixture.Id, safety, "poweroff-proof");
        var run = Task.Run(() => worker.RunProofAsync(stop.Token));
        try
        {
            await Task.Delay(4500);
            transaction.Rollback();
            if (run.IsFaulted) await run;
            await WaitForAsync(() => System.IO.File.Exists(fixture.File("reboot-after-start.json")));
            foreach (var database in fixture.Databases) AssertCommittedOnly(database, fixture.Id, postBoot: true);
        }
        finally { stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(25)); }
    }
    private static async Task PrepareAndCloseAsync(Fixture fixture)
    {
        using var safety = fixture.Safety();
        using var stop = new CancellationTokenSource();
        using var worker = new IsolatedScmRebootWorker(fixture.Root, fixture.Id, safety);
        await System.IO.File.WriteAllTextAsync(fixture.File("reboot-proof-arm.flag"), "arm");
        var run = worker.RunProofAsync(stop.Token);
        await WaitForAsync(() => System.IO.File.Exists(fixture.File("reboot-proof-ready.json")));
        stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25);
        Assert.True(condition());
    }

    private static void AssertCommittedOnly(string database, string id, bool postBoot = false)
    {
        using var db = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False"); db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "PRAGMA query_only=ON;"; command.ExecuteNonQuery();
        command.CommandText = "SELECT kind FROM dev_scm_crash_proof WHERE run_id=$id ORDER BY kind;";
        command.Parameters.AddWithValue("$id", id);
        using (var rows = command.ExecuteReader())
        {
            Assert.True(rows.Read()); Assert.Equal("committed", rows.GetString(0));
            if (postBoot) { Assert.True(rows.Read()); Assert.Equal("post_boot", rows.GetString(0)); }
            Assert.False(rows.Read());
        }
        command.CommandText = "PRAGMA quick_check;";
        Assert.Equal("ok", command.ExecuteScalar());
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "HostAgent-reboot-unit-" + Guid.NewGuid().ToString("N"));
        internal string Id { get; } = Guid.NewGuid().ToString("N");
        internal string File(string name) => Path.Combine(Root, name);
        internal string[] Databases => new[] { File("host-agent.db"), File("notifications.db") };
        internal static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture(); Directory.CreateDirectory(fixture.Root);
            await using var store = new SqliteHostAgentStore(fixture.Databases[0]);
            await store.InitializeAsync(default);
            await store.SaveTelemetrySnapshotAsync(new TelemetrySnapshot(Guid.NewGuid(), "system", 1, DateTimeOffset.UtcNow, "{}"), default);
            await new SqliteNotificationStore(fixture.Databases[1]).HasFailuresAsync(default);
            return fixture;
        }
        internal IsolatedScmRebootSafety Safety(Action? stop = null, Func<double>? free = null,
            Func<TimeSpan>? elapsed = null, Action<int>? exit = null, Func<long>? disk = null,
            Action<string, object>? marker = null) => new(
                Root, Id, stop ?? (() => { }), exit ?? (_ => { }), free ?? (() => 2048),
                disk ?? (() => 30L * 1024 * 1024 * 1024), elapsed ?? (() => TimeSpan.Zero), marker);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
