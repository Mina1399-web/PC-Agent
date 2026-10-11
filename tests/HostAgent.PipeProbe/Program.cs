using System.Security.Principal;
using HostAgent.Infrastructure.Administration;

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
