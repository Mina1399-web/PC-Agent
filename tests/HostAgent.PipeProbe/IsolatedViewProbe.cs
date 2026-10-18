using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Runtime.InteropServices;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Storage;
using HostAgent.Service;
using Microsoft.Data.Sqlite;

// Test-only process host: no helper, no outbound sender, no protected service
// configuration or database. All names/paths are derived from a validated GUID.
internal static class IsolatedViewProbe
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 3 || !Guid.TryParseExact(args[1], "N", out var fixture)) return 2;
        var name = "HostAgent-isolated-view-" + fixture.ToString("N");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HostAgent-DEV-evidence", "view-candidate-" + fixture.ToString("N"));
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!;
        if (args[0] == "isolated-view")
        {
            if (!int.TryParse(args[2], out var count) || count is < 1 or > 20) return 2;
            var failures = 0;
            for (var i = 1; i <= count; i++)
            {
                await Task.Delay(1200); // Exercise clients arriving late in the accept budget.
                PipeClientDiagnostic? failure = null;
                var started = Stopwatch.GetTimestamp();
                var bytes = await new ViewPipeClient(name, sid, TimeSpan.FromSeconds(5), d => failure = d)
                    .SendAsync(ViewProtocol.Request("overview", new { }), default);
                using var reply = JsonDocument.Parse(bytes);
                var ok = reply.RootElement.GetProperty("ok").GetBoolean();
                if (!ok) failures++;
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    attempt = i, ok, code = reply.RootElement.GetProperty("code").GetString(),
                    elapsedMilliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    stage = failure?.Stage, kind = failure?.Kind
                }));
            }
            return failures == 0 ? 0 : 1;
        }
        if (args[0] != "isolated-server" || args[2] is not ("normal" or "fault")) return 2;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "fixture.db");
        await using var store = new SqliteHostAgentStore(path);
        await store.InitializeAsync(default);
        var dispatcher = new DashboardDispatcher(store, new(), new(), TimeProvider.System,
            d => Console.WriteLine(JsonSerializer.Serialize(new { source = "dispatch", d.Stage, d.Kind, d.ElapsedMilliseconds })));
        var requests = 0;
        var server = new AdminPipeServer(name, sid, sid, async (payload, token) =>
        {
            var index = Interlocked.Increment(ref requests);
            if (args[2] == "fault" && index == 1) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            await Task.Delay(1200, token); // Bounded slow dispatch below the full request budget.
            return await dispatcher.DispatchAsync(payload, token);
        }, TimeSpan.FromSeconds(2), reportDiagnostic:
            d => Console.WriteLine(JsonSerializer.Serialize(new { source = "server", d.Stage, d.Kind, d.ElapsedMilliseconds })));
        using var worker = new DashboardWorker(server.ServeOnceAsync);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await worker.StartAsync(lifetime.Token);
        Console.WriteLine("{\"ready\":true}");
        try
        {
            while (!lifetime.IsCancellationRequested && !File.Exists(Path.Combine(directory, "stop")))
            {
                var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                if (!GlobalMemoryStatusEx(ref memory) || memory.AvailablePhysical < 512UL * 1024 * 1024)
                    throw new InvalidOperationException("free_memory_floor");
                if (new DriveInfo(Path.GetPathRoot(directory)!).AvailableFreeSpace < 20L * 1024 * 1024 * 1024)
                    throw new InvalidOperationException("free_disk_floor");
                await Task.Delay(250, lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5)); }
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync();
        using var check = connection.CreateCommand(); check.CommandText = "PRAGMA quick_check;";
        var healthy = (string?)await check.ExecuteScalarAsync() == "ok";
        Console.WriteLine(JsonSerializer.Serialize(new { stopped = true, requests, quickCheckHealthy = healthy }));
        return healthy ? 0 : 1;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
}
