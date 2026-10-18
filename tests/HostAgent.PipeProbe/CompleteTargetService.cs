using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using HostAgent.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;

// A harmless test service, not HostAgent. Its only work is bounded waiting and
// writing a few own-GUID metadata files. No collector, helper or network exists.
internal static class CompleteTargetService
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 3) return 2;
        try
        {
            CompleteProbeGuard.ValidateVm();
            CompleteProbeGuard.CheckLimits();
            var id = CompleteProbeGuard.Parse(args[1]);
            var operatorSid = new SecurityIdentifier(args[2]);
            if (!operatorSid.IsAccountSid() || operatorSid.Value != args[2]) return 3;
            using var process = Process.GetCurrentProcess();
            using var identity = WindowsIdentity.GetCurrent();
            var name = CompleteHelperScope.Name(id);
            var serviceSid = (SecurityIdentifier)new NTAccount("NT SERVICE", name).Translate(typeof(SecurityIdentifier));
            if (!WindowsServiceHelpers.IsWindowsService() || Environment.UserInteractive || process.SessionId != 0
                || identity.User is null || !serviceSid.Equals(identity.User) || identity.Name != @"NT SERVICE\" + name
                || !string.Equals(Environment.ProcessPath, CompleteHelperScope.Image(id), StringComparison.OrdinalIgnoreCase)) return 3;
            CompleteHelperScope.AssertRegistration(id, operatorSid.Value);
            var app = Path.Combine(CompleteHelperScope.Root(id), "app");
            if (!string.Equals(typeof(CompleteTargetService).Assembly.Location,
                    Path.Combine(app, "HostAgent.PipeProbe.dll"), StringComparison.OrdinalIgnoreCase)
                || !ValidateReadOnlyApp(app, serviceSid)) return 4;
            var data = HostAgentDataDirectory.EnsureSecure(Path.Combine(CompleteHelperScope.Root(id), "data"), serviceSid);
            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder.Logging.ClearProviders();
            builder.Services.AddWindowsService(options => options.ServiceName = name);
            builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(15));
            builder.Services.AddHostedService(provider => new TargetWorker(data, id,
                provider.GetRequiredService<IHostApplicationLifetime>()));
            using var host = builder.Build();
            using var guardDone = new CancellationTokenSource();
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var guard = GuardOwnLifetimeAsync(lifetime, data, id, guardDone.Token);
            try { await host.RunAsync(); }
            finally { guardDone.Cancel(); await guard; }
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // ValidateVm also checks resources before ordinary startup reaches
            // its data path. A transient startup abort must survive process exit.
            if (CompleteProbeGuard.ResourceFailureCode is { } code)
            {
                TryRecordStartupResourceAbort(args, code);
                return 75;
            }
            return 5;
        }
    }

    private static void TryRecordStartupResourceAbort(string[] args, string code)
    {
        try
        {
            // ResourceFailureCode is latched only after ValidateVm completed
            // the VM/UUID/account checks. Independently verify the exact service
            // token, registration, executable and existing secured data ACL.
            var id = CompleteProbeGuard.Parse(args[1]);
            var operatorSid = new SecurityIdentifier(args[2]);
            var name = CompleteHelperScope.Name(id);
            using var process = Process.GetCurrentProcess();
            using var identity = WindowsIdentity.GetCurrent();
            var sid = (SecurityIdentifier)new NTAccount("NT SERVICE", name).Translate(typeof(SecurityIdentifier));
            if (!operatorSid.IsAccountSid() || !WindowsServiceHelpers.IsWindowsService() || Environment.UserInteractive
                || process.SessionId != 0 || identity.User is null || !sid.Equals(identity.User)
                || identity.Name != @"NT SERVICE\" + name
                || !string.Equals(Environment.ProcessPath, CompleteHelperScope.Image(id), StringComparison.OrdinalIgnoreCase)) return;
            CompleteHelperScope.AssertRegistration(id, operatorSid.Value);
            var path = Path.Combine(CompleteHelperScope.Root(id), "data");
            if (!Directory.Exists(path)) return;
            var data = HostAgentDataDirectory.EnsureSecure(path, sid); // Existing ACL validation only.
            RecordResourceAbort(data, id, code);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { }
    }

    private static async Task GuardOwnLifetimeAsync(IHostApplicationLifetime lifetime, string data, Guid id, CancellationToken token)
    {
        var lease = Stopwatch.StartNew();
        try
        {
            while (lease.Elapsed < TimeSpan.FromSeconds(300))
            {
                // This branch can terminate only this already verified GUID
                // service process. It never opens or kills any other process.
                var available = CompleteProbeGuard.FreeMiB();
                if (!double.IsFinite(available) || available <= 512)
                {
                    RecordResourceAbort(data, id, !double.IsFinite(available) ? "memory_reading_unavailable" : "hard_free_memory_floor_512_mib");
                    Environment.Exit(75);
                }
                try { CompleteProbeGuard.CheckLimits(); }
                catch
                {
                    available = CompleteProbeGuard.FreeMiB();
                    RecordResourceAbort(data, id, CompleteProbeGuard.ResourceFailureCode ?? "resource_guard_failed");
                    if (!double.IsFinite(available) || available <= 512) Environment.Exit(75);
                    break;
                }
                await Task.Delay(250, token);
            }
            lifetime.StopApplication();
            var shutdown = Stopwatch.StartNew();
            while (shutdown.Elapsed < TimeSpan.FromSeconds(20))
            {
                var available = CompleteProbeGuard.FreeMiB();
                if (!double.IsFinite(available) || available <= 512)
                {
                    RecordResourceAbort(data, id, !double.IsFinite(available) ? "memory_reading_unavailable" : "hard_free_memory_floor_512_mib");
                    Environment.Exit(75);
                }
                await Task.Delay(250, token);
            }
            Environment.Exit(74); // Bounded self-only fail-safe if shutdown stalls.
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static void RecordResourceAbort(string data, Guid id, string code)
    {
        // Best effort on the already secured own data directory, never the app
        // or another service. Existing evidence is not overwritten.
        try
        {
            CompleteProbeGuard.AssertPlainPath(data);
            var path = Path.Combine(data, "resource-abort-" + Environment.ProcessId + ".json");
            CompleteProbeGuard.AssertPlainPath(path);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { runId = id.ToString("N"),
                processId = Environment.ProcessId, code, observedAtUtc = DateTimeOffset.UtcNow });
            file.Write(bytes); file.Flush(flushToDisk: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static bool ValidateReadOnlyApp(string app, SecurityIdentifier serviceSid)
    {
        static bool ReadExecuteOnly(CommonObjectSecurity security, SecurityIdentifier sid)
        {
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
                .Where(rule => sid.Equals(rule.IdentityReference)).ToArray();
            const FileSystemRights forbidden = FileSystemRights.Write | FileSystemRights.Delete
                | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
            return rules.Length > 0 && rules.All(rule => rule.AccessControlType == AccessControlType.Allow
                && (rule.FileSystemRights & forbidden) == 0
                && (rule.FileSystemRights & FileSystemRights.ReadAndExecute) == FileSystemRights.ReadAndExecute);
        }
        try
        {
            if (!ReadExecuteOnly(new DirectoryInfo(app).GetAccessControl(AccessControlSections.Access), serviceSid)) return false;
            var pending = new Stack<string>();
            pending.Push(app);
            var entries = 0;
            while (pending.Count > 0)
            {
                foreach (var entry in new DirectoryInfo(pending.Pop()).EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
                {
                    if (++entries > 2048 || (entry.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                    CompleteProbeGuard.AssertPlainPath(entry.FullName);
                    if (entry is DirectoryInfo directory)
                    {
                        if (!ReadExecuteOnly(directory.GetAccessControl(AccessControlSections.Access), serviceSid)) return false;
                        pending.Push(directory.FullName);
                    }
                    else if (entry is FileInfo file && !ReadExecuteOnly(file.GetAccessControl(AccessControlSections.Access), serviceSid)) return false;
                }
            }
            var write = Path.Combine(app, ".scm-acl-write-canary");
            var delete = Path.Combine(app, ".scm-acl-delete-canary");
            var expected = System.Text.Encoding.UTF8.GetBytes("HostAgent isolated SCM ACL canary v1");
            if (!File.ReadAllBytes(write).SequenceEqual(expected) || !File.ReadAllBytes(delete).SequenceEqual(expected)) return false;
            try { File.WriteAllBytes(write, "replacement"u8.ToArray()); return false; }
            catch (UnauthorizedAccessException) { }
            try { File.Delete(delete); return false; }
            catch (UnauthorizedAccessException) { }
            return File.ReadAllBytes(write).SequenceEqual(expected) && File.ReadAllBytes(delete).SequenceEqual(expected);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private sealed class TargetWorker(string data, Guid id, IHostApplicationLifetime lifetime) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            File.WriteAllText(Path.Combine(data, "target-" + Environment.ProcessId + ".json"),
                JsonSerializer.Serialize(new { runId = id.ToString("N"), processId = Environment.ProcessId,
                    startedAtUtc = DateTimeOffset.UtcNow, fixedLifetimeSeconds = 300 }));
            try { await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            lifetime.StopApplication();
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            // An actual SCM stop handler may still complete after the helper's
            // one-second deadline. This fixed eight-second delay is harmless,
            // bounded, and scoped only to the fresh target's own data flag.
            if (File.Exists(Path.Combine(data, "stop-delay.flag")))
                await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
            await base.StopAsync(cancellationToken);
        }
    }
}
