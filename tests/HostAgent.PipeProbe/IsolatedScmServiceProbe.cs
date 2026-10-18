using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using HostAgent.Core.Contracts;
using HostAgent.Core.Metrics;
using HostAgent.Core.Models.Remediation;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Core.Orchestration;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Collectors;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Heartbeat;
using HostAgent.Infrastructure.Notifications;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Storage;
using HostAgent.Service;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.ServiceProcess;

// Reviewable test-only host for the current Release service assemblies.
// It does not register production configuration, endpoints, secrets, or helper.
internal static class IsolatedScmServiceProbe
{
    private const string DataPrefix = "HostAgent-DEV-SCM-";
    private const string ServicePrefix = "HostAgentDevScm_";
    private const string PipePrefix = "HostAgent.Dev.Scm.View.";

    internal static async Task<int> RunServiceAsync(string[] args)
    {
        if (args.Length != 3 && !(args.Length == 4 && args[3] is "crash-proof" or "reboot-proof" or "poweroff-proof")) return 2;
        if (!TryGetIdentity(args[1], out var id, out var serviceName, out var pipeName)) return 2;
        if (!TryGetSecurityIdentifier(args[2], out var operatorSid)) return 2;

        using var process = Process.GetCurrentProcess();
        if (!OperatingSystem.IsWindows() || !WindowsServiceHelpers.IsWindowsService()
            || process.SessionId != 0 || Environment.UserInteractive || !IsDevVm() || !HasSafetyHeadroom()) return 3;

        SecurityIdentifier serviceSid;
        try { serviceSid = (SecurityIdentifier)new NTAccount("NT SERVICE", serviceName).Translate(typeof(SecurityIdentifier)); }
        catch (IdentityNotMappedException) { return 3; }

        if (args.Length == 4 && args[3] is "reboot-proof" or "poweroff-proof" && FreeMiB() < 1024) return 3;
        var root = GetRoot(id);
        var app = Path.Combine(root, "app");
        var data = Path.Combine(root, "data");
        if (!Directory.Exists(app) || IsReparsePath(root) || IsReparsePath(app)) return 3;
        if (!VerifyStartupAcl(app, serviceSid,
                path => new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access),
                VerifyAppIsReadOnly)) return 5;

        string securedData;
        try { securedData = HostAgentDataDirectory.EnsureSecure(data, serviceSid); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return 4; }
        var database = Path.Combine(securedData, "host-agent.db");
        var store = new SqliteHostAgentStore(database);
        var configuration = new ServiceConfiguration(); // Shadow, read-only, no external delivery or probes.
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Logging.ClearProviders();
        builder.Services.AddWindowsService(options => options.ServiceName = serviceName);
        var helper = new DenyHelper();
        builder.Services.AddSingleton<IHostAgentStore>(store);
        builder.Services.AddSingleton<ICollectorCheckpointStore>(store);
        builder.Services.AddSingleton<IHelperClient>(helper);
        builder.Services.AddSingleton<INotificationStore>(_ => new SqliteNotificationStore(Path.Combine(securedData, "notifications.db")));
        builder.Services.AddSingleton<INotificationSender, LocalNotificationSender>();
        builder.Services.AddSingleton<IHeartbeatSender, DisabledHeartbeat>();
        builder.Services.AddSingleton<DiagnosticCycleProcessor>();
        Add(builder.Services, store, new WindowsSystemCollector(), new SystemTelemetryMetricProjector());
        Add(builder.Services, store, new WindowsCpuMemoryCollector(), new CpuMemoryMetricProjector());
        Add(builder.Services, store, new WindowsDiskCollector(), new DiskTelemetryMetricProjector());
        Add(builder.Services, store, new WindowsProcessServicesCollector(), new ProcessServiceMetricProjector());
        Add(builder.Services, store, new WindowsNetworkCollector(false), new NetworkTelemetryMetricProjector());
        Add(builder.Services, store, new WindowsEventLogCollector(store), new EventLogMetricProjector());
        ServiceRuntime.AddWorkers(builder.Services, configuration);
        if (args.Length == 4 && args[3] == "crash-proof")
            builder.Services.AddHostedService(_ => new IsolatedScmCrashWorker(securedData,id.ToString("N")));
        if (args.Length == 4 && args[3] is "reboot-proof" or "poweroff-proof")
        {
            builder.Services.AddSingleton(provider => new IsolatedScmRebootSafety(securedData, id.ToString("N"),
                provider.GetRequiredService<IHostApplicationLifetime>().StopApplication, Environment.Exit));
            builder.Services.AddHostedService(provider => new IsolatedScmRebootWorker(securedData, id.ToString("N"),
                provider.GetRequiredService<IsolatedScmRebootSafety>(), args[3]));
        }
        builder.Services.AddHostedService(provider =>
        {
            var dispatcher = new DashboardDispatcher(store, configuration,
                provider.GetRequiredService<AgentRuntimeHealth>(), TimeProvider.System);
            var server = new AdminPipeServer(pipeName, serviceSid, operatorSid,
                (payload, token) => IsolatedScmDashboardDispatch.DispatchAsync(payload, dispatcher.DispatchAsync, token),
                TimeSpan.FromSeconds(10));
            return new DashboardWorker(server.ServeOnceAsync);
        });

        using var host = builder.Build();
        using var rebootSafety = args.Length == 4 && args[3] is "reboot-proof" or "poweroff-proof"
            ? host.Services.GetRequiredService<IsolatedScmRebootSafety>() : null;
        rebootSafety?.Start();
        await host.RunAsync();
        if (helper.Calls != 0) return 5;
        return 0;

    }

    private static void Add<T>(IServiceCollection services, IHostAgentStore store,
        ITelemetryCollector<T> collector, ITelemetryMetricProjector<T> projector) where T : class =>
        services.AddSingleton<ITelemetryCollectionJob>(new TelemetryCollectionJob<T>(collector, store, projector));

    internal static bool VerifyStartupAcl(string app, SecurityIdentifier serviceSid,
        Func<string, DirectorySecurity> readDirectorySecurity,
        Func<string, SecurityIdentifier, bool> verifyApp)
    {
        // The administrative runner checks the package root. The service has
        // no READ_CONTROL there and must inspect only its read/execute app.
        try
        {
            return HasReadExecuteOnlyAccess(readDirectorySecurity(app), serviceSid)
                && verifyApp(app, serviceSid);
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    private static bool VerifyAppIsReadOnly(string app, SecurityIdentifier serviceSid)
    {
        var writeCanary = Path.Combine(app, ".scm-acl-write-canary");
        var deleteCanary = Path.Combine(app, ".scm-acl-delete-canary");
        var expected = System.Text.Encoding.UTF8.GetBytes("HostAgent isolated SCM ACL canary v1");
        try
        {
            foreach (var file in Directory.EnumerateFiles(app, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReparsePoint) != 0
                    || !HasReadExecuteOnlyAccess(new FileInfo(file).GetAccessControl(AccessControlSections.Access), serviceSid)) return false;
            }
            if (!File.ReadAllBytes(writeCanary).SequenceEqual(expected)
                || !File.ReadAllBytes(deleteCanary).SequenceEqual(expected)) return false;
            try { File.WriteAllBytes(writeCanary, "replacement"u8.ToArray()); return false; }
            catch (UnauthorizedAccessException) { }
            try { File.Delete(deleteCanary); return false; }
            catch (UnauthorizedAccessException) { }
            return File.ReadAllBytes(writeCanary).SequenceEqual(expected)
                && File.ReadAllBytes(deleteCanary).SequenceEqual(expected);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool HasReadExecuteOnlyAccess(CommonObjectSecurity security, SecurityIdentifier serviceSid)
    {
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(rule => serviceSid.Equals(rule.IdentityReference))
            .ToArray();
        if (rules.Length == 0) return false;
        const FileSystemRights forbidden = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        return rules.All(rule => rule.AccessControlType == AccessControlType.Allow
                && (rule.FileSystemRights & forbidden) == 0
                && (rule.FileSystemRights & FileSystemRights.ReadAndExecute) == FileSystemRights.ReadAndExecute);
    }

    internal static async Task<int> RunViewAsync(string[] args)
    {
        if (args.Length != 3) return 2;
        if (!TryGetIdentity(args[1], out _, out _, out var pipeName)) return 2;
        if (!TryGetSecurityIdentifier(args[2], out var serviceSid)) return 2;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            PipeClientDiagnostic? diagnostic = null;
            var started = Stopwatch.GetTimestamp();
            try
            {
                var bytes = await new ViewPipeClient(pipeName, serviceSid, TimeSpan.FromSeconds(8), value => diagnostic = value)
                    .SendAsync(ViewProtocol.Request("overview", new { }), CancellationToken.None);
                using var response = JsonDocument.Parse(bytes);
                var ok = response.RootElement.GetProperty("ok").GetBoolean()
                    && response.RootElement.GetProperty("code").GetString() == "overview";
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    attempt, ok, elapsedMilliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    clientStage = diagnostic?.Stage, failureKind = diagnostic?.Kind
                }));
                if (ok) return 0;
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or JsonException or InvalidOperationException)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    attempt, ok = false, elapsedMilliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    clientStage = diagnostic?.Stage, failureKind = diagnostic?.Kind ?? SafeKind(error)
                }));
            }
            if (attempt < 3) await Task.Delay(250);
        }
        return 1;
    }

    internal static int RunDatabaseCheck(string[] args)
    {
        if (args.Length != 2 || !Guid.TryParseExact(args[1], "N", out var id)) return 2;
        var root = GetRoot(id);
        var data = Path.Combine(root, "data");
        var database = Path.Combine(data, "host-agent.db");
        if (IsReparsePath(root) || !File.Exists(database)) return 3;
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 3
            }.ToString();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var quickCheck = connection.CreateCommand();
            quickCheck.CommandText = "PRAGMA quick_check;";
            var result = quickCheck.ExecuteScalar() as string;
            using var migrations = connection.CreateCommand();
            migrations.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
            var migrationCount = Convert.ToInt32(migrations.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            using var snapshots = connection.CreateCommand();
            snapshots.CommandText = "SELECT COUNT(*) FROM telemetry_snapshots;";
            var snapshotCount = Convert.ToInt32(snapshots.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            using var baselines = connection.CreateCommand();
            baselines.CommandText = "SELECT COUNT(*) FROM baseline_samples;";
            var baselineCount = Convert.ToInt32(baselines.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            var size = new FileInfo(database).Length;
            var healthy = result == "ok" && migrationCount >= SqliteHostAgentStore.LatestSchemaVersion && snapshotCount > 0;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                scenario = "isolated-scm-database-read-only-check", healthy,
                quickCheck = result, migrationCount, snapshotCount, baselineCount, databaseBytes = size
            }));
            return healthy ? 0 : 1;
        }
        catch (SqliteException)
        {
            Console.WriteLine("{\"scenario\":\"isolated-scm-database-read-only-check\",\"healthy\":false,\"failureCode\":\"sqlite_read_check_failed\"}");
            return 1;
        }
    }

    private static bool TryGetIdentity(string text, out Guid id, out string serviceName, out string pipeName)
    {
        serviceName = ""; pipeName = ""; id = default;
        if (!Guid.TryParseExact(text, "N", out id)) return false;
        serviceName = ServicePrefix + id.ToString("N");
        pipeName = PipePrefix + id.ToString("N");
        return true;
    }

    private static bool TryGetSecurityIdentifier(string value, out SecurityIdentifier sid)
    {
        try { sid = new SecurityIdentifier(value); return true; }
        catch (ArgumentException) { sid = new SecurityIdentifier(WellKnownSidType.WorldSid, null); return false; }
    }

    private static string GetRoot(Guid id) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), DataPrefix + id.ToString("N"));

    private static bool IsReparsePath(string path)
    {
        var full = Path.GetFullPath(path);
        for (DirectoryInfo? current = new(full); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }

    private static bool IsDevVm()
    {
        using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS", false);
        using var additions = new ServiceController("VBoxService");
        try
        {
            return Environment.MachineName == "AGENTPC"
                && bios?.GetValue("SystemProductName") as string == "VirtualBox"
                && additions.Status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static bool HasSafetyHeadroom() => FreeMiB() >= 512
        && new DriveInfo(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))!).AvailableFreeSpace
            >= 20L * 1024 * 1024 * 1024;

    private static double FreeMiB()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref memory) ? memory.AvailablePhysical / 1048576d : 0;
    }

    private static string SafeKind(Exception error) => error switch
    {
        OperationCanceledException => "client_deadline",
        IOException => "io",
        JsonException => "invalid_response",
        _ => "unexpected"
    };

    private sealed class DenyHelper : IHelperClient
    {
        internal int Calls;
        public ValueTask<HelperOperationResponse> ExecuteAsync(HelperOperationRequest request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            return ValueTask.FromResult(new HelperOperationResponse(1, request.RequestId, HelperResult.Denied, "fixture_denied"));
        }
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
