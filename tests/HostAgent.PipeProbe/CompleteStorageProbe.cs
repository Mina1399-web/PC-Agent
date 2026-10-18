using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HostAgent.Core.Contracts;
using HostAgent.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using HostAgent.Service;

public class CompleteStorageWriteFault : DispatchProxy
{
    public IHostAgentStore Inner { get; set; } = null!;
    private int enabled;
    private int injectedFailures;
    public bool Enabled { get => Volatile.Read(ref enabled) != 0; set => Volatile.Write(ref enabled, value ? 1 : 0); }
    public int InjectedFailures => Volatile.Read(ref injectedFailures);
    public Action? FailureInjected { get; set; }
    public static IHostAgentStore Create(IHostAgentStore inner, out CompleteStorageWriteFault controller)
    {
        var proxy = DispatchProxy.Create<IHostAgentStore, CompleteStorageWriteFault>();
        controller = (CompleteStorageWriteFault)(object)proxy;
        controller.Inner = inner;
        return proxy;
    }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (Enabled && (targetMethod.Name.StartsWith("Save", StringComparison.Ordinal)
            || targetMethod.Name.StartsWith("Append", StringComparison.Ordinal) || targetMethod.Name == "ApplyRetentionAsync"))
        {
            Interlocked.Increment(ref injectedFailures);
            FailureInjected?.Invoke();
            throw new SqliteException("private_write_boundary_sqlite_full", 13);
        }
        try { return targetMethod.Invoke(Inner, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
}

public static class CompleteStorageProbe
{
    public static bool ComponentHasStorageFailure(IReadOnlyList<ComponentHealth> baseline, IReadOnlyList<ComponentHealth> current)
        => ComponentRecovered(baseline) && current.Any(component => component.Component == "system" && component.Degraded
            && component.Code is "collector_exception" or "collector_timeout" or "backoff_active");
    private static bool ComponentRecovered(IReadOnlyList<ComponentHealth> current)
        => current.Any(component => component.Component == "system" && !component.Degraded && component.Code == "collector_healthy");
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 3 || args[0] != "isolated-complete-storage" || args[2] is not ("unavailable" or "full" or "corrupt")) return 2;
        var id = CompleteProbeGuard.Parse(args[1]); CompleteProbeGuard.ValidateVm(true); CompleteProbeGuard.CheckLimits();
        var directory = CompleteProbeGuard.NewDirectory(id, "complete-storage");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var timer = Stopwatch.StartNew();
        object? evidence = null; var ok = false; string? errorCode = null;
        var integrity = new List<object>();
        try
        {
            evidence = args[2] switch
            {
                "unavailable" => await UnavailableAsync(directory, id, deadline.Token),
                "full" => await FullAsync(directory, id, deadline.Token),
                _ => await CorruptAsync(directory, id, deadline.Token)
            };
            CheckFixture(directory, 32L * 1024 * 1024, 64); CompleteProbeGuard.CheckLimits(); ok = true;
        }
        catch (Exception error)
        {
            errorCode = error switch { OperationCanceledException => "storage_deadline_or_guard", SqliteException sqlite => "sqlite_" + sqlite.SqliteErrorCode,
                TimeoutException => "private_recovery_deadline", InvalidOperationException when SafeFailureCodes.Contains(error.Message) => error.Message,
                _ => "storage_assertion_or_runtime_failure" };
        }
        // Runtime graph disposal has completed before this outer path. Even a
        // failed scenario gets bounded read-only checks when resources permit.
        try
        {
            CompleteProbeGuard.CheckLimits(); CheckFixture(directory, 32L * 1024 * 1024, 64);
            foreach (var file in Directory.EnumerateFiles(directory, "*.db", SearchOption.AllDirectories).Take(8))
            {
                using var checkDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                string check;
                try { check = await QuickCheckReadOnlyAsync(file, checkDeadline.Token) ? "ok" : "not_ok_or_nonempty_journal"; }
                catch (SqliteException sqlite) { check = "sqlite_" + sqlite.SqliteErrorCode; }
                catch (Exception) { check = "check_unavailable"; }
                integrity.Add(new { database = Path.GetRelativePath(directory, file), quickCheck = check });
            }
        }
        catch (Exception) { integrity.Add(new { database = "fixture", quickCheck = "not_run_resource_or_fixture_guard" }); }
        if (CompleteProbeGuard.ResourceFailureCode is { } resourceError) { ok = false; errorCode = resourceError; }
        Console.WriteLine(JsonSerializer.Serialize(new { finished = true, mode = "complete_storage", fixtureId = id.ToString("N"),
            scenario = args[2], ok, errorCode, elapsedSeconds = Math.Round(timer.Elapsed.TotalSeconds, 1), directory, evidence, postRunIntegrity = integrity,
            resourceAbort = CompleteProbeGuard.ResourceFailureCode is not null, resourceFailureCode = CompleteProbeGuard.ResourceFailureCode,
            physicalVolumeFullTested = false, protectedServiceTargeted = false }));
        return ok ? 0 : 1;
    }

    private static async Task<object> UnavailableAsync(string directory, Guid id, CancellationToken token)
    {
        bool viewUnavailable; bool degraded; bool baselineHealthy; bool componentRecovered;
        long before; long after; int acknowledgedCount; bool retained; int helperCalls;
        await using (var graph = new PrivateRuntimeGraph(directory, id))
        {
            await graph.StartAsync(token);
            await WaitAsync(async () => ComponentRecovered(graph.Health.Snapshot()) && (await AcknowledgedAsync(graph, token)).Length > 0 && await graph.OverviewAsync(token), TimeSpan.FromSeconds(35), token);
            var baselineHealth = graph.Health.Snapshot();
            baselineHealthy = ComponentRecovered(baselineHealth);
            before = await graph.SnapshotCountAsync(token); var acknowledged = await AcknowledgedAsync(graph, token);
            acknowledgedCount = acknowledged.Length;
            FileStream? exclusive = null;
            await WaitAsync(() =>
            {
                try { exclusive = new(graph.DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return Task.FromResult(true); }
                catch (IOException) { return Task.FromResult(false); }
            }, TimeSpan.FromSeconds(8), token);
            viewUnavailable = false; degraded = false;
            try
            {
                var holding = Stopwatch.StartNew();
                while (holding.Elapsed < TimeSpan.FromSeconds(37))
                {
                    CompleteProbeGuard.CheckLimits();
                    viewUnavailable |= !await graph.OverviewAsync(token);
                    degraded |= ComponentHasStorageFailure(baselineHealth, graph.Health.Snapshot());
                    await DelayAsync(TimeSpan.FromSeconds(1), token);
                }
            }
            finally { exclusive!.Dispose(); }
            if (!viewUnavailable || !degraded) throw new InvalidOperationException("exclusive_lock_effect_not_observed");
            await WaitAsync(async () => ComponentRecovered(graph.Health.Snapshot()) && await graph.SnapshotCountAsync(token) > before
                && await graph.OverviewAsync(token), TimeSpan.FromSeconds(90), token);
            after = await graph.SnapshotCountAsync(token);
            retained = acknowledged.All((await AcknowledgedAsync(graph, token)).Contains);
            await graph.StopAsync(token);
            componentRecovered = ComponentRecovered(graph.Health.Snapshot());
            helperCalls = graph.HelperCalls;
        }
        // The runtime and SQLite handles must be fully disposed before the
        // immutable read-only check. An open WAL is valid but must not be
        // silently omitted from an immutable database view.
        var main = await QuickCheckReadOnlyAsync(Path.Combine(directory, "host-agent.db"), token);
        var notices = await QuickCheckReadOnlyAsync(Path.Combine(directory, "notifications.db"), token);
        if (!main || !notices || !retained || helperCalls != 0) throw new InvalidOperationException("private_recovery_integrity_failed");
        return new { injection = "actual_windows_exclusive_file_lock", lockHeldAtLeastSeconds = 37, viewUnavailable, degraded,
            recovery = true, component = "system", baselineComponentHealthy = baselineHealthy, componentFailureObserved = degraded,
            componentRecovered, snapshotsBefore = before, snapshotsAfter = after, acknowledgedSnapshots = acknowledgedCount,
            committedSnapshotsPreserved = retained, hostQuickCheck = "ok", notificationsQuickCheck = "ok", helperCalls };
    }

    private static async Task<object> FullAsync(string directory, Guid id, CancellationToken token)
    {
        var quota = await ProveSqliteQuotaAsync(Path.Combine(directory, "quota.db"), token);
        if (!quota) throw new InvalidOperationException("quota_engine_proof_failed");
        await using var graph = new PrivateRuntimeGraph(directory, id);
        await graph.StartAsync(token);
        await WaitAsync(async () => ComponentRecovered(graph.Health.Snapshot()) && (await AcknowledgedAsync(graph, token)).Length > 0
            && await graph.OverviewAsync(token), TimeSpan.FromSeconds(35), token);
        var baselineHealth = graph.Health.Snapshot();
        var before = await graph.SnapshotCountAsync(token); var acknowledged = await AcknowledgedAsync(graph, token); graph.WriteFault.Enabled = true;
        var degraded = false;
        try
        {
            var fault = Stopwatch.StartNew();
            while (fault.Elapsed < TimeSpan.FromSeconds(25))
            { degraded |= ComponentHasStorageFailure(baselineHealth, graph.Health.Snapshot()); await DelayAsync(TimeSpan.FromSeconds(1), token); }
            if (graph.WriteFault.InjectedFailures == 0 || !degraded || !await graph.OverviewAsync(token))
                throw new InvalidOperationException("live_full_boundary_not_observed");
        }
        finally { graph.WriteFault.Enabled = false; }
        await WaitAsync(async () => ComponentRecovered(graph.Health.Snapshot()) && await graph.SnapshotCountAsync(token) > before
            && await graph.OverviewAsync(token), TimeSpan.FromSeconds(95), token);
        var after = await graph.SnapshotCountAsync(token); var retained = acknowledged.All((await AcknowledgedAsync(graph, token)).Contains);
        await graph.StopAsync(token);
        if (!retained || !await QuickCheckReadOnlyAsync(graph.DatabasePath, token) || !await QuickCheckReadOnlyAsync(graph.NotificationPath, token) || graph.HelperCalls != 0)
            throw new InvalidOperationException("full_recovery_integrity_failed");
        return new { injection = "engine_page_quota_plus_scoped_live_write_boundary_sqlite_full_equivalent", engineSqliteFull = quota,
            liveWriteFailures = graph.WriteFault.InjectedFailures, degraded, viewReadAvailable = true, snapshotsBefore = before, snapshotsAfter = after,
            component = "system", baselineComponentHealthy = ComponentRecovered(baselineHealth), componentFailureObserved = degraded,
            componentRecovered = ComponentRecovered(graph.Health.Snapshot()),
            acknowledgedSnapshots = acknowledged.Length, committedSnapshotsPreserved = retained,
            hostQuickCheck = "ok", notificationsQuickCheck = "ok", helperCalls = graph.HelperCalls, physicalVolumeFullTested = false };
    }

    private static async Task<object> CorruptAsync(string directory, Guid id, CancellationToken token)
    {
        var backup = Path.Combine(directory, "verified-backup"); Directory.CreateDirectory(backup);
        var backupDb = Path.Combine(backup, "host-agent.db"); var backupNotices = Path.Combine(backup, "notifications.db");
        var acknowledged = Guid.NewGuid();
        await using (var store = new SqliteHostAgentStore(backupDb))
        {
            await store.InitializeAsync(token);
            await store.SaveTelemetrySnapshotAsync(new(acknowledged, "private-proof", 1, DateTimeOffset.UtcNow, "{}"), token);
        }
        _ = await new SqliteNotificationStore(backupNotices).HasFailuresAsync(token);
        if (!await QuickCheckReadOnlyAsync(backupDb, token) || !await QuickCheckReadOnlyAsync(backupNotices, token))
            throw new InvalidOperationException("backup_not_healthy");
        var damaged = Path.Combine(directory, "corrupt-preserved"); Directory.CreateDirectory(damaged);
        var damagedDb = Path.Combine(damaged, "host-agent.db");
        await File.WriteAllBytesAsync(damagedDb, Enumerable.Repeat((byte)0xA5, 4096).ToArray(), token);
        var originalHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(damagedDb, token)));
        var rejected = false; var degraded = false;
        await using (var broken = new PrivateRuntimeGraph(damaged, id))
        {
            await broken.StartAsync(token);
            for (var attempt = 0; attempt < 12; attempt++)
            {
                rejected |= !await broken.OverviewAsync(token); degraded |= broken.Health.Degraded;
                if (rejected && degraded) break;
                await DelayAsync(TimeSpan.FromSeconds(1), token);
            }
            await broken.StopAsync(token);
        }
        var preserved = originalHash == Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(damagedDb, token)));
        if (!rejected || !degraded || !preserved) throw new InvalidOperationException("corruption_not_rejected_or_preserved");
        var recovery = Path.Combine(directory, "explicit-fresh-recovery"); Directory.CreateDirectory(recovery);
        await CopyNewAsync(backupDb, Path.Combine(recovery, "host-agent.db"), token);
        await CopyNewAsync(backupNotices, Path.Combine(recovery, "notifications.db"), token);
        await using var recovered = new PrivateRuntimeGraph(recovery, id);
        await recovered.StartAsync(token);
        await WaitAsync(async () => await recovered.SnapshotCountAsync(token) > 1 && await recovered.OverviewAsync(token), TimeSpan.FromSeconds(40), token);
        var retained = (await recovered.DurableStore.ReadTelemetrySnapshotsAsync("private-proof", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, 4, token))
            .Any(snapshot => snapshot.SnapshotId == acknowledged);
        await recovered.StopAsync(token);
        if (!retained || !await QuickCheckReadOnlyAsync(recovered.DatabasePath, token) || !await QuickCheckReadOnlyAsync(recovered.NotificationPath, token))
            throw new InvalidOperationException("explicit_backup_recovery_failed");
        return new { injection = "actual_corrupt_disposable_database", rejected, degraded, corruptBytesPreserved = preserved, corruptSha256 = originalHash,
            recovery = "explicit_verified_backup_to_new_fixture", acknowledgedSnapshotRetained = retained, hostQuickCheck = "ok", notificationsQuickCheck = "ok" };
    }

    public static async Task<bool> ProveSqliteQuotaAsync(string path, CancellationToken token)
    {
        if (File.Exists(path)) throw new InvalidOperationException("quota_fixture_exists");
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, DefaultTimeout = 2 }.ToString());
        await connection.OpenAsync(token);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA page_size=4096; PRAGMA journal_mode=DELETE; PRAGMA max_page_count=16; CREATE TABLE quota_proof(id INTEGER PRIMARY KEY, value BLOB); INSERT INTO quota_proof VALUES(1,zeroblob(8));";
        await command.ExecuteNonQueryAsync(token);
        var full = false;
        using (var transaction = connection.BeginTransaction())
        {
            using var large = connection.CreateCommand(); large.Transaction = transaction;
            large.CommandText = "INSERT INTO quota_proof VALUES(2,zeroblob(1048576));";
            try { await large.ExecuteNonQueryAsync(token); }
            catch (SqliteException error) when (error.SqliteErrorCode == 13) { full = true; }
            // SQLITE_FULL may already have rolled back the transaction.
            try { transaction.Rollback(); } catch (SqliteException) { }
        }
        command.CommandText = "SELECT COUNT(*) FROM quota_proof;";
        var rows = Convert.ToInt64(await command.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture);
        command.CommandText = "PRAGMA quick_check;";
        return full && rows == 1 && (string?)await command.ExecuteScalarAsync(token) == "ok";
    }

    public static async Task<bool> QuickCheckReadOnlyAsync(string path, CancellationToken token)
    {
        CompleteProbeGuard.AssertPlainPath(path);
        if (!File.Exists(path)) return false;
        foreach (var suffix in new[] { "-wal", "-journal" })
            if (File.Exists(path + suffix) && new FileInfo(path + suffix).Length != 0) return false;
        using var readHandle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = new Uri(Path.GetFullPath(path)).AbsoluteUri + "?immutable=1",
            Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 2
        }.ToString());
        await connection.OpenAsync(token);
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA query_only=ON; PRAGMA temp_store=MEMORY;";
        await command.ExecuteNonQueryAsync(token); command.CommandText = "PRAGMA quick_check;";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var registration = deadline.Token.Register(() => connection.Close());
        await using var reader = await command.ExecuteReaderAsync(deadline.Token);
        var rows = 0; var ok = true;
        while (await reader.ReadAsync(deadline.Token)) { rows++; ok &= reader.GetString(0) == "ok"; }
        return ok && rows == 1;
    }

    public static async Task WaitAsync(Func<Task<bool>> condition, TimeSpan timeout, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            token.ThrowIfCancellationRequested(); CompleteProbeGuard.CheckLimits();
            try { if (await condition()) return; }
            catch (Exception error) when (error is SqliteException or IOException) { }
            await DelayAsync(TimeSpan.FromMilliseconds(500), token);
        }
        throw new TimeoutException("private_recovery_deadline");
    }
    public static async Task DelayAsync(TimeSpan duration, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < duration)
        { CompleteProbeGuard.CheckLimits(); token.ThrowIfCancellationRequested(); await Task.Delay(250, token); }
    }
    public static void CheckFixture(string directory, long maximumBytes, int maximumEntries)
    {
        var pending = new Stack<string>(); pending.Push(directory); long bytes = 0; var entries = 0;
        while (pending.Count > 0)
        {
            var current = pending.Pop(); CompleteProbeGuard.AssertPlainPath(current);
            foreach (var item in new DirectoryInfo(current).EnumerateFileSystemInfos())
            {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("fixture_reparse_point");
                if (++entries > maximumEntries) throw new InvalidOperationException("fixture_entry_limit");
                if (item is DirectoryInfo child) pending.Push(child.FullName);
                else bytes += ((FileInfo)item).Length;
                if (bytes > maximumBytes) throw new InvalidOperationException("fixture_size_limit");
            }
        }
    }
    private static async Task CopyNewAsync(string source, string destination, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, token);
    }
    private static async Task<Guid[]> AcknowledgedAsync(PrivateRuntimeGraph graph, CancellationToken token)
    {
        var snapshots = await graph.DurableStore.ReadTelemetrySnapshotsAsync("system", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, 128, token);
        return snapshots.Select(snapshot => snapshot.SnapshotId).ToArray();
    }
    private static readonly HashSet<string> SafeFailureCodes = new(StringComparer.Ordinal)
    {
        "exclusive_lock_effect_not_observed", "private_recovery_integrity_failed", "quota_engine_proof_failed", "live_full_boundary_not_observed",
        "full_recovery_integrity_failed", "backup_not_healthy", "corruption_not_rejected_or_preserved", "explicit_backup_recovery_failed",
        "acknowledged_system_snapshot_missing", "hard_free_memory_floor_512_mib", "soft_free_memory_floor_768_mib", "disk_free_below_20_gib",
        "fixture_entry_limit", "fixture_size_limit", "fixture_reparse_point"
    };
}
