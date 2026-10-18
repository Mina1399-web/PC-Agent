using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using HostAgent.Core.Models.Audit;
using HostAgent.Core.Models.Remediation;
using HostAgent.Core.Policy;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

// Test-only closed target set. No production helper/bootstrap configuration is
// loaded, no process capability is exposed, and no arbitrary service name can
// enter the real Windows API adapter.
internal static class CompleteHelperScope
{
    internal static string Name(Guid id) => "HostAgentDevScm_" + id.ToString("N");
    internal static string Root(Guid id) => @"C:\ProgramData\HostAgent-DEV-SCM-" + id.ToString("N");
    internal static string Image(Guid id) => Path.Combine(Root(id), "app", "HostAgent.PipeProbe.exe");
    internal static string Command(Guid id, string sid) => $"\"{Image(id)}\" isolated-complete-target {id:N} {sid}";

    internal static bool IsExpectedTarget(Guid id, string operatorSid, string name,
        string imagePath, string account, int type)
    {
        if (id == Guid.Empty || type != 16) return false;
        try
        {
            var sid = new SecurityIdentifier(operatorSid);
            return sid.IsAccountSid() && sid.Value == operatorSid
                && name == Name(id) && account == @"NT SERVICE\" + Name(id)
                && string.Equals(imagePath, Command(id, operatorSid), StringComparison.Ordinal);
        }
        catch (ArgumentException) { return false; }
    }

    internal static void AssertRegistration(Guid id, string operatorSid)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + Name(id), false);
        if (key is null || !IsExpectedTarget(id, operatorSid, Name(id),
                key.GetValue("ImagePath") as string ?? "", key.GetValue("ObjectName") as string ?? "",
                Convert.ToInt32(key.GetValue("Type"), System.Globalization.CultureInfo.InvariantCulture))
            || Convert.ToInt32(key.GetValue("Start"), System.Globalization.CultureInfo.InvariantCulture) is not (3 or 4))
            throw new UnauthorizedAccessException("isolated_target_registration_mismatch");
        CompleteProbeGuard.AssertPlainPath(Root(id));
        CompleteProbeGuard.AssertPlainPath(Path.Combine(Root(id), "app"));
        CompleteProbeGuard.AssertPlainPath(Path.Combine(Root(id), "data"));
        if (!Directory.Exists(Path.Combine(Root(id), "data")))
            throw new UnauthorizedAccessException("isolated_target_data_missing");
    }

    internal static int Pid(Guid id)
    {
        using var service = new ServiceController(Name(id), ".");
        if (!QueryServiceStatusEx(service.ServiceHandle.DangerousGetHandle(), 0, out var status,
                Marshal.SizeOf<ServiceProcessStatus>(), out _))
            throw new InvalidOperationException("isolated_target_status_unavailable");
        return checked((int)status.ProcessId);
    }

    internal static bool IsState(Guid id, ServiceControllerStatus state)
    {
        using var service = new ServiceController(Name(id), ".");
        return service.Status == state;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceProcessStatus
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode,
            ServiceSpecificExitCode, CheckPoint, WaitHint, ProcessId, ServiceFlags;
    }
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(IntPtr service, int level,
        out ServiceProcessStatus status, int bufferSize, out int bytesNeeded);
}

internal sealed class ScopedGuidWindowsApi : IWindowsOperationApi
{
    private readonly HashSet<Guid> _existing;
    private readonly Guid _missing;
    private readonly string _sid;
    private readonly string _expectedDllHash;
    private readonly string _expectedExeHash;
    private readonly WindowsOperationApi _native = new();
    private int _calls;
    internal int Calls => Volatile.Read(ref _calls);

    internal ScopedGuidWindowsApi(Guid[] existing, Guid missing, string sid)
    {
        if (existing.Length != 3 || existing.Contains(Guid.Empty) || missing == Guid.Empty
            || existing.Distinct().Count() != 3 || existing.Contains(missing))
            throw new ArgumentException("invalid_closed_target_set");
        _existing = new(existing); _missing = missing; _sid = sid;
        _expectedDllHash = Digest(typeof(ScopedGuidWindowsApi).Assembly.Location);
        _expectedExeHash = Digest(Environment.ProcessPath ?? throw new InvalidOperationException("missing_executable"));
    }

    public async ValueTask SetServiceRunningAsync(string serviceName, bool running, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CompleteProbeGuard.CheckLimits();
        var id = _existing.SingleOrDefault(value => CompleteHelperScope.Name(value) == serviceName);
        if (id != Guid.Empty)
        {
            CompleteHelperScope.AssertRegistration(id, _sid);
            var app = Path.Combine(CompleteHelperScope.Root(id), "app");
            // Hold read-only handles throughout invocation, as well as checking
            // the immutable package identity immediately before the native API.
            using var exe = new FileStream(CompleteHelperScope.Image(id), FileMode.Open, FileAccess.Read, FileShare.Read);
            using var dll = new FileStream(Path.Combine(app, "HostAgent.PipeProbe.dll"), FileMode.Open, FileAccess.Read, FileShare.Read);
            if (Convert.ToHexString(SHA256.HashData(exe)) != _expectedExeHash
                || Convert.ToHexString(SHA256.HashData(dll)) != _expectedDllHash)
                throw new UnauthorizedAccessException("isolated_target_package_mismatch");
            var pid = CompleteHelperScope.Pid(id);
            if (pid > 0)
            {
                using var process = Process.GetProcessById(pid);
                if (process.SessionId != 0 || !string.Equals(process.MainModule?.FileName,
                        CompleteHelperScope.Image(id), StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("isolated_target_process_mismatch");
            }
            Interlocked.Increment(ref _calls);
            await _native.SetServiceRunningAsync(serviceName, running, token);
            return;
        }
        if (serviceName != CompleteHelperScope.Name(_missing) || !running)
            throw new UnauthorizedAccessException("outside_closed_target_set");
        using var absent = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + serviceName, false);
        if (absent is not null || Directory.Exists(CompleteHelperScope.Root(_missing)))
            throw new UnauthorizedAccessException("missing_target_is_not_absent");
        Interlocked.Increment(ref _calls);
        // A real lookup of an absent GUID service is the native failure fixture.
        await _native.SetServiceRunningAsync(serviceName, true, token);
    }

    public IProcessLease OpenProcess(int pid) => throw new UnauthorizedAccessException("process_capability_absent");
    private static string Digest(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(file));
    }
}

internal static class CompleteHelperProbe
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 6) return 2;
        string? directory = null;
        var checks = new List<object>();
        var nativeCalls = 0;
        var ok = false;
        var failure = "preflight_not_complete";
        var stage = "vm_and_target_preflight";
        var journalQuickCheck = "not_run";
        string? journalDatabase = null;
        var stageEvents = new ConcurrentQueue<object>();
        var campaignClock = Stopwatch.StartNew();
        void Report(string component, string boundary)
        {
            if (stageEvents.Count < 512)
                stageEvents.Enqueue(new { component, boundary, elapsedMilliseconds = campaignClock.ElapsedMilliseconds });
        }
        try
        {
            CompleteProbeGuard.ValidateVm(requireAdmin: true);
            CompleteProbeGuard.CheckLimits();
            var ids = args.Skip(1).Select(CompleteProbeGuard.Parse).ToArray();
            if (ids.Distinct().Count() != 5) throw new InvalidOperationException("duplicate_run_or_target");
            var (run, a, b, c, missing) = (ids[0], ids[1], ids[2], ids[3], ids[4]);
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User ?? throw new InvalidOperationException("missing_operator_identity");
            foreach (var id in new[] { a, b, c }) CompleteHelperScope.AssertRegistration(id, sid.Value);
            if (!CompleteHelperScope.IsState(a, ServiceControllerStatus.Stopped) || CompleteHelperScope.Pid(a) != 0
                || !CompleteHelperScope.IsState(b, ServiceControllerStatus.Running)
                || !CompleteHelperScope.IsState(c, ServiceControllerStatus.Running)
                || !File.Exists(Path.Combine(CompleteHelperScope.Root(b), "data", "stop-delay.flag")))
                throw new InvalidOperationException("target_initial_state_mismatch");
            await WaitReadyAsync(b, CancellationToken.None);
            await WaitReadyAsync(c, CancellationToken.None);
            using (var absent = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + CompleteHelperScope.Name(missing), false))
                if (absent is not null || Directory.Exists(CompleteHelperScope.Root(missing)))
                    throw new InvalidOperationException("missing_target_not_absent");

            directory = CompleteProbeGuard.NewDirectory(run, "complete-helper");
            var database = Path.Combine(directory, "helper-journal.db");
            journalDatabase = database;
            var api = new ScopedGuidWindowsApi([a, b, c], missing, sid.Value);
            var allowlist = new HelperAllowlist([
                new("target-a", CompleteHelperScope.Name(a), [HelperOperation.StartService, HelperOperation.StopService]),
                new("target-b", CompleteHelperScope.Name(b), [HelperOperation.StopService, HelperOperation.StartService]),
                new("target-c", CompleteHelperScope.Name(c), [HelperOperation.RestartService]),
                new("missing", CompleteHelperScope.Name(missing), [HelperOperation.StartService])], []);
            var requests = new List<HelperOperationRequest>();
            var pipe = "HostAgent.Dev.Complete.Helper." + run.ToString("N");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            var safetyFailure = 0;
            var serverFailure = 0;
            var monitor = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        CompleteProbeGuard.CheckLimits();
                        await Task.Delay(250, deadline.Token);
                    }
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
                catch { Interlocked.Exchange(ref safetyFailure, 1); deadline.Cancel(); }
            });
            try
            {
                await using (var journal = new SqliteHelperJournal(database, [sid.Value]))
                {
                    var helperTimeoutSeconds = 15;
                    var dispatcher = new HelperDispatcher(journal, allowlist, new WindowsOperationExecutor(api),
                        new PolicyEngine(), () => new HelperSettings(ReadOnly: false, Shadow: false,
                            ExecutionTimeoutSeconds: Volatile.Read(ref helperTimeoutSeconds)),
                        stageObserver: boundary => Report("dispatcher", boundary));
                    var server = new NamedPipeHelperServer(pipe, new PipeAuthorization(sid, sid), dispatcher,
                        TimeSpan.FromSeconds(25), boundary => Report("server", boundary));
                    using var listenerDone = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                    var listener = Task.Run(async () =>
                    {
                        try
                        {
                            for (var served = 0; served < 24 && !listenerDone.IsCancellationRequested; served++)
                                await server.ServeOnceAsync(listenerDone.Token);
                        }
                        catch (OperationCanceledException) when (listenerDone.IsCancellationRequested) { }
                        catch { Interlocked.Exchange(ref serverFailure, 1); deadline.Cancel(); }
                    });
                    try
                    {
                        async Task<HelperOperationRequest> Approve(string target, Guid targetId, HelperOperation operation,
                            Guid? wrongTarget = null)
                        {
                            var request = new HelperOperationRequest(1, Guid.NewGuid(), "dev-" + Guid.NewGuid().ToString("N"),
                                "controlled-helper-fixture", 95, ["private-fixture-state"], operation, new ServiceParameters(target), Guid.NewGuid());
                            var now = DateTimeOffset.UtcNow;
                            await journal.RecordApprovalAsync(new(request.ApprovalId!.Value, request.RequestId,
                                request.IncidentId, request.DiagnosisId, request.Operation, HelperProtocol.Digest(request),
                                sid.Value, now, now.AddMinutes(3))
                                { Target = new(CompleteHelperScope.Name(wrongTarget ?? targetId), null, null) }, deadline.Token);
                            requests.Add(request);
                            return request;
                        }
                        async Task Expect(string name, HelperOperationRequest request, HelperResult expected, string code,
                            int expectedCalls, SecurityIdentifier? owner = null)
                        {
                            stage = name;
                            CompleteProbeGuard.CheckLimits();
                            var clock = Stopwatch.StartNew();
                            var response = await new NamedPipeHelperClient(pipe, owner ?? sid, TimeSpan.FromSeconds(25),
                                boundary => Report("client-" + name, boundary))
                                .ExecuteAsync(request, deadline.Token);
                            nativeCalls = api.Calls;
                            var pass = response.Result == expected && response.Code == code && api.Calls == expectedCalls;
                            checks.Add(new { name, ok = pass, result = response.Result.ToString(), code = response.Code,
                                nativeCalls = api.Calls, elapsedMilliseconds = clock.ElapsedMilliseconds });
                            if (!pass) throw new InvalidOperationException("helper_boundary_assertion_failed");
                        }

                        var start = await Approve("target-a", a, HelperOperation.StartService);
                        await Expect("real_start", start, HelperResult.Succeeded, "succeeded", 1);
                        if (!CompleteHelperScope.IsState(a, ServiceControllerStatus.Running) || CompleteHelperScope.Pid(a) <= 0)
                            throw new InvalidOperationException("target_a_not_running");
                        await WaitReadyAsync(a, deadline.Token);
                        await Expect("exact_replay_denied", start, HelperResult.Denied, "replayed_request", 1);
                        var cooldown = await Approve("target-a", a, HelperOperation.StopService);
                        await Expect("fresh_operation_cooldown", cooldown, HelperResult.Denied, "cooldown", 1);

                        var oldPid = CompleteHelperScope.Pid(c);
                        var restart = await Approve("target-c", c, HelperOperation.RestartService);
                        // A normal restart may include SCM process startup; allow
                        // its real production default execution deadline (15s).
                        // Only the next dedicated stop fixture uses 1s.
                        // Only the private fixture deadline changes; no clock,
                        // journal, cooldown, retry or production limit is altered.
                        helperTimeoutSeconds = 15;
                        await Expect("real_restart", restart, HelperResult.Succeeded, "succeeded", 3);
                        var newPid = CompleteHelperScope.Pid(c);
                        if (oldPid <= 0 || newPid <= 0 || oldPid == newPid || !CompleteHelperScope.IsState(c, ServiceControllerStatus.Running))
                            throw new InvalidOperationException("restart_process_not_replaced");
                        await WaitReadyAsync(c, deadline.Token);
                        checks.Add(new { name = "restart_new_pid", ok = true, oldPid, newPid });

                        helperTimeoutSeconds = 1;
                        var stop = await Approve("target-b", b, HelperOperation.StopService);
                        await Expect("real_stop_deadline", stop, HelperResult.Indeterminate, "execution_interrupted", 4);
                        await Expect("indeterminate_replay_denied", stop, HelperResult.Denied, "replayed_request", 4);
                        var unresolved = await Approve("target-b", b, HelperOperation.StartService);
                        await Expect("indeterminate_retry_denied", unresolved, HelperResult.Denied, "unresolved_attempt", 4);
                        var wait = Stopwatch.StartNew();
                        while (!CompleteHelperScope.IsState(b, ServiceControllerStatus.Stopped) && wait.Elapsed < TimeSpan.FromSeconds(15))
                        { CompleteProbeGuard.CheckLimits(); await Task.Delay(250, deadline.Token); }
                        if (!CompleteHelperScope.IsState(b, ServiceControllerStatus.Stopped) || CompleteHelperScope.Pid(b) != 0)
                            throw new InvalidOperationException("timed_out_target_not_quiescent");
                        checks.Add(new { name = "timed_out_native_stop_eventually_quiescent", ok = true });

                        helperTimeoutSeconds = 15;
                        var absent = await Approve("missing", missing, HelperOperation.StartService);
                        await Expect("real_missing_service_failure", absent, HelperResult.Failed, "execution_failed", 5);
                        await Expect("wrong_pipe_owner_denied", start, HelperResult.Denied, "helper_identity_mismatch", 5,
                            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
                        var forged = await Approve("target-a", a, HelperOperation.StopService, c);
                        await Expect("forged_target_approval_denied", forged, HelperResult.Denied, "approval_target_mismatch", 5);
                        var absentApproval = start with { RequestId = Guid.NewGuid(), ApprovalId = Guid.NewGuid() };
                        await Expect("unrecorded_approval_denied", absentApproval, HelperResult.Denied, "DENIED", 5);
                    }
                    finally
                    {
                        listenerDone.Cancel();
                        await listener.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                }
                await using (var reopened = new SqliteHelperJournal(database, [sid.Value]))
                {
                    stage = "durable_journal_reopen";
                    foreach (var request in requests)
                    {
                        var audit = await reopened.ReadAuditAsync(request.RequestId, deadline.Token);
                        if (!audit.Any(row => row.Event == AuditEvent.Approval)
                            || !audit.Any(row => row.Event == AuditEvent.Proposal))
                            throw new InvalidOperationException("durable_audit_missing");
                        var expectedStarted = request.Parameters is ServiceParameters p
                            && (p.ServiceId == "target-c" || p.ServiceId == "missing"
                                || (p.ServiceId == "target-a" && request.Operation == HelperOperation.StartService)
                                || (p.ServiceId == "target-b" && request.Operation == HelperOperation.StopService));
                        if (audit.Count(row => row.Event == AuditEvent.ExecutionStarted) != (expectedStarted ? 1 : 0))
                            throw new InvalidOperationException("durable_execution_count_mismatch");
                    }
                }
                stage = "readonly_journal_quick_check";
                journalQuickCheck = ReadOnlyQuickCheck(database);
                if (journalQuickCheck != "ok") throw new InvalidOperationException("journal_quick_check_failed");
                checks.Add(new { name = "durable_approvals_audit_and_readonly_quick_check", ok = true, requestCount = requests.Count });
                if (Volatile.Read(ref safetyFailure) != 0 || Volatile.Read(ref serverFailure) != 0)
                    throw new InvalidOperationException("helper_guard_or_listener_failed");
                nativeCalls = api.Calls;
                ok = true; failure = "none"; stage = "complete";
            }
            finally { deadline.Cancel(); await monitor; }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            failure = error is OperationCanceledException ? "bounded_deadline_or_safety_abort"
                : error is UnauthorizedAccessException ? "scope_or_identity_guard_rejected" : "helper_integration_failed";
            // A failed assertion must still report disposable-journal health
            // after its owning connection has been disposed. Never repair it.
            if (journalDatabase is not null && File.Exists(journalDatabase))
            {
                try { journalQuickCheck = ReadOnlyQuickCheck(journalDatabase); }
                catch (Exception checkError) when (checkError is IOException or UnauthorizedAccessException or InvalidOperationException)
                { journalQuickCheck = "not_run_or_sidecar_present"; }
            }
        }
        var resourceFailureCode = CompleteProbeGuard.ResourceFailureCode;
        if (resourceFailureCode is not null) { ok = false; failure = "latched_resource_abort"; }
        var result = new { scenario = "complete-real-helper-integration", ok, outcome = ok ? "PASS" : "FAIL",
            failureCode = ok ? null : failure, stage, directory, checks, nativeCalls, journalQuickCheck,
            transportStages = stageEvents.ToArray(),
            resourceAbort = resourceFailureCode is not null, resourceFailureCode,
            scope = "private_operator_owned_pipe_and_guid_virtual_services_only", productionBootstrapVerified = false };
        if (directory is not null) File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(result));
        Console.WriteLine(JsonSerializer.Serialize(result));
        return ok ? 0 : 1;
    }

    private static async Task WaitReadyAsync(Guid id, CancellationToken token)
    {
        var wait = Stopwatch.StartNew();
        while (wait.Elapsed < TimeSpan.FromSeconds(10))
        {
            CompleteProbeGuard.CheckLimits();
            var pid = CompleteHelperScope.Pid(id);
            var file = Path.Combine(CompleteHelperScope.Root(id), "data", "target-" + pid + ".json");
            if (pid > 0 && CompleteHelperScope.IsState(id, ServiceControllerStatus.Running) && File.Exists(file))
            {
                CompleteProbeGuard.AssertPlainPath(file);
                if (new FileInfo(file).Length is <= 0 or > 1024)
                    throw new InvalidOperationException("target_readiness_metadata_invalid");
                using var metadata = JsonDocument.Parse(File.ReadAllBytes(file));
                using var process = Process.GetProcessById(pid);
                if (metadata.RootElement.GetProperty("runId").GetString() == id.ToString("N")
                    && metadata.RootElement.GetProperty("processId").GetInt32() == pid
                    && metadata.RootElement.GetProperty("fixedLifetimeSeconds").GetInt32() == 300
                    && metadata.RootElement.GetProperty("startedAtUtc").GetDateTimeOffset() >= process.StartTime.ToUniversalTime()
                    && string.Equals(process.MainModule?.FileName, CompleteHelperScope.Image(id), StringComparison.OrdinalIgnoreCase)) return;
            }
            await Task.Delay(250, token);
        }
        throw new InvalidOperationException("target_readiness_deadline");
    }

    internal static string ReadOnlyQuickCheck(string path)
    {
        if (new FileInfo(path).Length is <= 0 or > 4 * 1024 * 1024)
            throw new InvalidOperationException("journal_fixture_size_rejected");
        foreach (var suffix in new[] { "-wal", "-journal" })
            if (File.Exists(path + suffix) && new FileInfo(path + suffix).Length != 0)
                throw new InvalidOperationException("nonempty_journal_readonly_check_not_run");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = new Uri(Path.GetFullPath(path)).AbsoluteUri + "?mode=ro&immutable=1",
            Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA query_only=ON; PRAGMA temp_store=MEMORY;";
        command.ExecuteNonQuery();
        command.CommandText = "PRAGMA quick_check;";
        command.CommandTimeout = 10;
        using var rows = command.ExecuteReader();
        return rows.Read() && rows.GetString(0) == "ok" && !rows.Read() ? "ok" : "not_ok";
    }
}
