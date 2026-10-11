using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using HostAgent.Infrastructure.Administration;
using Xunit;

namespace HostAgent.Infrastructure.Tests;
public sealed class ColdAdminPipeTests
{
    // A warm test runner concealed a callback JIT/load failure under Identification.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task First_request_in_cold_server_authenticates_without_loading_assemblies_under_client_token(bool allowed)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var runtime = Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!, "dotnet.exe");
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!;
        var name = "HostAgent-cold-test-" + Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(runtime) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(root, "tests/HostAgent.PipeProbe/bin/Release/net10.0-windows/HostAgent.PipeProbe.dll"));
        start.ArgumentList.Add(name); start.ArgumentList.Add(sid.Value);
        start.ArgumentList.Add(allowed ? sid.Value : "S-1-5-21-1-2-3-9999");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await new AdminPipeClient(name, sid, TimeSpan.FromSeconds(10))
                .SendAsync(AdminProtocol.Request("status", new { }), deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            var output = await stdout;
            Assert.Equal("", await stderr);
            Assert.True(process.ExitCode == 0, output);
            using var json = JsonDocument.Parse(result);
            Assert.Equal(allowed, json.RootElement.GetProperty("ok").GetBoolean());
            if (allowed)
            {
                Assert.Contains("dispatches=1", output);
                Assert.DoesNotContain("control_", output);
                Assert.Equal("cold_ready", json.RootElement.GetProperty("code").GetString());
            }
            else
            {
                Assert.Contains("dispatches=0", output);
                Assert.Contains("control_unauthorized", output);
                Assert.DoesNotContain("_failed", output);
            }
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
