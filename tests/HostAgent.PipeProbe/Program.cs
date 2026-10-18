using System.Security.Principal;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using System.Text.Json;

if (args.Length > 0 && args[0] == "isolated-complete-storage")
    return await CompleteStorageProbe.RunAsync(args);
if (args.Length > 0 && args[0] == "isolated-complete-soak")
    return await CompleteSoakProbe.RunAsync(args);
if (args.Length > 0 && args[0] == "isolated-complete-helper")
    return await CompleteHelperProbe.RunAsync(args);
if (args.Length > 0 && args[0] == "isolated-complete-target")
    return await CompleteTargetService.RunAsync(args);
if (args.Length > 0 && args[0] == "isolated-complete-wpf")
    return await CompleteWpfProbe.RunAsync(args);

if (args.Length > 0 && args[0] == "isolated-persistence")
    return await IsolatedPersistenceProbe.RunAsync(args);
if (args.Length > 0 && args[0] == "isolated-runtime")
    return await IsolatedRuntimeProbe.RunAsync(args);
if (args.Length > 0 && args[0] == "isolated-scm-service")
    return await IsolatedScmServiceProbe.RunServiceAsync(args);
if (args.Length > 0 && args[0] == "isolated-scm-view")
    return await IsolatedScmServiceProbe.RunViewAsync(args);
if (args.Length > 0 && args[0] == "isolated-scm-dbcheck")
    return IsolatedScmServiceProbe.RunDatabaseCheck(args);
if (args.Length > 0 && args[0] == "isolated-scm-watchdog")
    return await IsolatedScmMemoryWatchdog.RunAsync(args);
if (args.Length > 0 && args[0].StartsWith("isolated-", StringComparison.Ordinal))
    return await IsolatedViewProbe.RunAsync(args);

// Bounded read-only probe for the locally installed View endpoint. Output
// includes only transport stage, duration and the fixed response code.
if (args.Length > 0 && args[0] == "view")
{
    if (args.Length != 2 || !int.TryParse(args[1], out var attempts) || attempts is < 1 or > 5) return 2;
    var owner = (SecurityIdentifier)new NTAccount("NT SERVICE", "HostAgent").Translate(typeof(SecurityIdentifier));
    for (var attempt = 1; attempt <= attempts; attempt++)
    {
        PipeClientDiagnostic? diagnostic = null;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var bytes = await new ViewPipeClient("HostAgent.View.v1", owner, TimeSpan.FromSeconds(10), value => diagnostic = value)
            .SendAsync(ViewProtocol.Request("overview", new { }), CancellationToken.None);
        using var response = JsonDocument.Parse(bytes);
        var ok = response.RootElement.GetProperty("ok").GetBoolean();
        var code = response.RootElement.GetProperty("code").GetString();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            attempt,
            ok,
            code,
            elapsedMilliseconds = (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            clientStage = diagnostic?.Stage,
            failureKind = diagnostic?.Kind,
            clientFailureMilliseconds = diagnostic?.ElapsedMilliseconds
        }));
        if (ok && code == "overview") return 0;
        if (attempt < attempts) await Task.Delay(250);
    }
    return 1;
}

// Test-only cold server: do not touch WindowsIdentity/WindowsPrincipal before
// the production authorization callback. No secrets or operations are served.
if (args.Length != 3 || !args[0].StartsWith("HostAgent-cold-test-", StringComparison.Ordinal)) return 2;
var sid = new SecurityIdentifier(args[1]);
var role = new SecurityIdentifier(args[2]);
var calls = 0;
await new AdminPipeServer(args[0], sid, role, (_, _) =>
{
    calls++;
    return ValueTask.FromResult(AdminProtocol.Reply(true, "cold_ready"));
}, TimeSpan.FromSeconds(10), code => Console.WriteLine(code)).ServeOnceAsync(default);
Console.WriteLine("dispatches=" + calls);
return 0;
