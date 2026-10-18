using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using HostAgent.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

// Only the GUID-scoped SCM test host selects this mode. No reboot API is called.
internal sealed class IsolatedScmRebootWorker(string data, string runId, IsolatedScmRebootSafety safety, string proofMode = "reboot-proof") : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunProofAsync(stoppingToken);

    internal async Task RunProofAsync(CancellationToken stoppingToken)
    {
        if (proofMode is not ("reboot-proof" or "poweroff-proof"))
            throw new ArgumentException("reboot_invalid_proof_mode");
        if (!Guid.TryParseExact(runId, "N", out var id) || id == Guid.Empty)
            throw new ArgumentException("reboot_invalid_run_id");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, safety.AbortToken);
        var token = linked.Token;
        var readyPath = Path.Combine(data, "reboot-proof-ready.json");
        var closedPath = Path.Combine(data, "reboot-proof-closed.json");
        if (File.Exists(Path.Combine(data, "reboot-safety-abort.json")))
            throw new InvalidOperationException("reboot_prior_safety_abort");
        if (File.Exists(readyPath))
        {
            ValidateRestart(readyPath, closedPath);
            var restartStarted = Stopwatch.GetTimestamp();
            foreach (var name in new[] { "host-agent.db", "notifications.db" })
            {
                token.ThrowIfCancellationRequested();
                if (proofMode == "poweroff-proof")
                {
                    await WritePoweroffPostBootAsync(Path.Combine(data, name), restartStarted, token);
                    continue;
                }
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
                    DataSource = Path.Combine(data, name), Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false, DefaultTimeout = 3 }.ToString());
                await connection.OpenAsync(token);
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA synchronous=FULL;";
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "INSERT INTO dev_scm_crash_proof(run_id,kind) VALUES($id,'post_boot');";
                command.Parameters.AddWithValue("$id", runId);
                await command.ExecuteNonQueryAsync(token);
            }
            token.ThrowIfCancellationRequested();
            WriteNew(Path.Combine(data, "reboot-after-start.json"), new {
                runId, mode = proofMode, processId = Environment.ProcessId, startedAtUtc = DateTimeOffset.UtcNow });
            await HoldAsync(token);
            return;
        }
        if (File.Exists(closedPath) || File.Exists(Path.Combine(data, "reboot-first-start.json"))
            || File.Exists(Path.Combine(data, "reboot-after-start.json")))
            throw new InvalidOperationException("reboot_incomplete_prior_start");
        WriteNew(Path.Combine(data, "reboot-first-start.json"), new {
            runId, processId = Environment.ProcessId, startedAtUtc = DateTimeOffset.UtcNow });
        var armStarted = Stopwatch.GetTimestamp();
        while (!File.Exists(Path.Combine(data, "reboot-proof-arm.flag")))
        {
            if (Stopwatch.GetElapsedTime(armStarted) >= TimeSpan.FromSeconds(90))
            {
                safety.Abort("reboot_arm_timeout");
                return;
            }
            await Task.Delay(100, token);
        }
        var paths = new[] { Path.Combine(data, "host-agent.db"), Path.Combine(data, "notifications.db") };
        var databaseStarted = Stopwatch.GetTimestamp();
        while (paths.Any(path => !File.Exists(path)))
        {
            if (Stopwatch.GetElapsedTime(databaseStarted) >= TimeSpan.FromSeconds(40))
                throw new InvalidOperationException("reboot_databases_unavailable");
            await Task.Delay(100, token);
        }
        await new SqliteNotificationStore(paths[1]).HasFailuresAsync(token);
        var proof = new IsolatedScmCrashProof();
        var readyWritten = false;
        try
        {
            token.ThrowIfCancellationRequested();
            proof.Prepare(paths, runId);
            // Keep the actual store defaults: host telemetry uses WAL, while
            // notifications uses the rollback journal. Never change either mode
            // just to make the power-loss fixture easier to inspect.
            if (proofMode == "poweroff-proof"
                && (!File.Exists(paths[0] + "-wal") || new FileInfo(paths[0] + "-wal").Length <= 0
                    || !File.Exists(paths[1] + "-journal") || new FileInfo(paths[1] + "-journal").Length <= 0))
                throw new InvalidOperationException("poweroff_pending_journal_evidence_missing");
            var snapshots = ReadSnapshots(paths[0]);
            token.ThrowIfCancellationRequested();
            WriteNew(readyPath, new {
                runId, mode = proofMode, committed = true, pendingTransactions = 2, snapshotIds = snapshots,
                readyAtUtc = DateTimeOffset.UtcNow });
            readyWritten = true;
            await HoldAsync(token);
        }
        finally
        {
            // This marker is written only after both pending transactions were rolled back.
            proof.Dispose();
            if (readyWritten && stoppingToken.IsCancellationRequested && safety.FailureCode is null)
                WriteNew(closedPath, new { runId, reason = "graceful_stop", closedAtUtc = DateTimeOffset.UtcNow });
        }
    }

    private async Task WritePoweroffPostBootAsync(string database, long started, CancellationToken token)
    {
        // Startup collectors may briefly hold the private database writer lock.
        // The shared deadline bounds both databases; successful earlier inserts
        // are never replayed while the later database is retried.
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(20))
                throw new InvalidOperationException("poweroff_post_boot_busy_deadline");
            try
            {
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
                    DataSource = database, Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false, DefaultTimeout = 1 }.ToString());
                await connection.OpenAsync(token);
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA synchronous=FULL;";
                await command.ExecuteNonQueryAsync(token);
                command.CommandText = "INSERT INTO dev_scm_crash_proof(run_id,kind) VALUES($id,'post_boot');";
                command.Parameters.AddWithValue("$id", runId);
                await command.ExecuteNonQueryAsync(token);
                return;
            }
            catch (SqliteException error) when (error.SqliteErrorCode is 5 or 6)
            {
                await Task.Delay(100, token);
            }
        }
    }

    private static async Task HoldAsync(CancellationToken token)
    {
        try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void ValidateRestart(string readyPath, string closedPath)
    {
        var poweroff = proofMode == "poweroff-proof";
        if ((poweroff ? File.Exists(closedPath) : !File.Exists(closedPath))
            || File.Exists(Path.Combine(data, "reboot-after-start.json")))
            throw new InvalidOperationException("reboot_restart_state_invalid");
        try
        {
            using var ready = ReadBounded(readyPath);
            using var closed = poweroff ? null : ReadBounded(closedPath);
            var r = ready.RootElement;
            var c = closed?.RootElement;
            var ids = r.GetProperty("snapshotIds");
            if (r.GetProperty("runId").GetString() != runId || !r.GetProperty("committed").GetBoolean()
                || (poweroff && (!r.TryGetProperty("mode", out var mode) || mode.GetString() != "poweroff-proof"))
                || (!poweroff && r.TryGetProperty("mode", out var rebootMode) && rebootMode.GetString() != "reboot-proof")
                || r.GetProperty("pendingTransactions").GetInt32() != 2 || ids.ValueKind != JsonValueKind.Array
                || ids.GetArrayLength() is < 1 or > 64
                || ids.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || item.GetString()!.Length is < 1 or > 128)
                || (!poweroff && (c!.Value.GetProperty("runId").GetString() != runId
                    || c.Value.GetProperty("reason").GetString() != "graceful_stop"
                    || c.Value.GetProperty("closedAtUtc").GetDateTimeOffset() < r.GetProperty("readyAtUtc").GetDateTimeOffset())))
                throw new InvalidOperationException("reboot_restart_marker_invalid");
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            throw new InvalidOperationException("reboot_restart_marker_invalid", error);
        }
    }

    private static JsonDocument ReadBounded(string path)
    {
        if (new FileInfo(path).Length is < 2 or > 32768) throw new InvalidOperationException("reboot_marker_bounds");
        return JsonDocument.Parse(File.ReadAllBytes(path));
    }

    private static string[] ReadSnapshots(string path)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 2 }.ToString());
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT snapshot_id FROM telemetry_snapshots ORDER BY snapshot_id LIMIT 65;";
        using var rows = command.ExecuteReader();
        var result = new List<string>();
        while (rows.Read()) result.Add(rows.GetString(0));
        if (result.Count is < 1 or > 64) throw new InvalidOperationException("reboot_snapshot_bounds");
        return result.ToArray();
    }

    internal static void WriteNew(string path, object value)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, value);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: false);
    }
}

// This guard survives worker completion and is disposed only after the entire host stopped.
internal sealed class IsolatedScmRebootSafety : IDisposable
{
    private readonly string data, runId;
    private readonly Action requestStop;
    private readonly Action<int> exitOwnProcess;
    private readonly Action<string, object> markerWriter;
    private readonly Func<double> freeMiB;
    private readonly Func<long> freeDiskBytes;
    private readonly Func<TimeSpan> elapsed;
    private readonly CancellationTokenSource abort = new();
    private readonly ManualResetEvent done = new(false);
    private Thread? thread;
    private string? failureCode;
    private TimeSpan abortAt;
    private int fallbackRequested;
    private int disposed;
    private readonly object abortGate = new();
    internal string? FailureCode => Volatile.Read(ref failureCode);
    internal CancellationToken AbortToken => abort.Token;

    internal IsolatedScmRebootSafety(string data, string runId, Action requestStop, Action<int> exitOwnProcess,
        Func<double>? freeMiB = null, Func<long>? freeDiskBytes = null, Func<TimeSpan>? elapsed = null,
        Action<string, object>? markerWriter = null)
    {
        this.data = data; this.runId = runId; this.requestStop = requestStop; this.exitOwnProcess = exitOwnProcess;
        this.freeMiB = freeMiB ?? ReadFreeMiB;
        this.markerWriter = markerWriter ?? IsolatedScmRebootWorker.WriteNew;
        this.freeDiskBytes = freeDiskBytes ?? (() => new DriveInfo("C:\\").AvailableFreeSpace);
        var started = Stopwatch.GetTimestamp();
        this.elapsed = elapsed ?? (() => Stopwatch.GetElapsedTime(started));
    }

    internal void Start()
    {
        if (thread is not null) throw new InvalidOperationException("reboot_guard_already_started");
        thread = new Thread(() => { do { CheckNow(); } while (!done.WaitOne(250)); }) {
            IsBackground = true, Name = "DEV isolated reboot safety" };
        thread.Start();
    }

    internal void CheckNow()
    {
        if (FailureCode is null)
        {
            try
            {
                var memory = freeMiB();
                if (!double.IsFinite(memory) || memory <= 512) Abort("reboot_hard_memory_floor_512_mib");
                else if (memory < 768) Abort("reboot_soft_memory_floor_768_mib");
                else if (freeDiskBytes() < 20L * 1024 * 1024 * 1024) Abort("reboot_disk_floor_20_gib");
                else if (elapsed() >= TimeSpan.FromSeconds(900)) Abort("reboot_lease_expired");
            }
            catch { Abort("reboot_resource_probe_failed"); }
        }
        if (FailureCode is not null && elapsed() - abortAt >= TimeSpan.FromSeconds(10)
            && Interlocked.Exchange(ref fallbackRequested, 1) == 0)
            exitOwnProcess(73);
    }

    internal void Abort(string code)
    {
        lock (abortGate)
        {
            if (FailureCode is not null) return;
            abortAt = elapsed();
            Volatile.Write(ref failureCode, code);
        }
        // Neither storage latency nor synchronous cancellation/lifetime callbacks may
        // occupy the independent guard thread and prevent its ten-second fallback.
        // There is at most one background action of each kind for the latched abort.
        QueueBestEffort(abort.Cancel, "DEV reboot cancellation");
        QueueBestEffort(requestStop, "DEV reboot stop request");
        var observed = DateTimeOffset.UtcNow;
        QueueBestEffort(() => markerWriter(Path.Combine(data, "reboot-safety-abort.json"), new {
            runId, reason = code, observedAtUtc = observed }), "DEV reboot abort marker");
    }

    private static void QueueBestEffort(Action action, string name)
    {
        try
        {
            new Thread(() => {
                // Disposal may race queued cancellation after the host already stopped.
                // A blocked marker writer is never joined during shutdown.
                try { action(); } catch { }
            }) { IsBackground = true, Name = name }.Start();
        }
        catch { } // The existing guard thread still enforces the fixed fallback.
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        done.Set();
        if (thread is not null && !thread.Join(3000)) throw new InvalidOperationException("reboot_guard_shutdown_unconfirmed");
        done.Dispose(); abort.Dispose();
    }

    internal static double ReadFreeMiB()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? status.AvailablePhysical / 1048576d : double.NaN;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
}
