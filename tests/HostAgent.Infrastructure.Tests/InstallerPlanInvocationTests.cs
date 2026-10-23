using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed partial class ReleaseToolingTests
{
    // A valid read-only plan must not depend on caller strictness or an old native exit.
    [Theory]
    [InlineData("unset", false)]
    [InlineData("nonzero", false)]
    [InlineData("nonzero", true)]
    public async Task Installer_plan_uses_current_validation_result_in_strict_callers(string priorExit, bool tamper)
    {
        using var fixture = new PackageFixture();
        if (tamper) File.AppendAllText(Path.Combine(fixture.Path, "Service", "HostAgent.Service.exe"), "tampered");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var command = "Set-StrictMode -Version Latest; " +
            (priorExit == "unset" ? "Remove-Variable LASTEXITCODE -ErrorAction SilentlyContinue; " : "$LASTEXITCODE=37; ") +
            "& $args[0] -PackagePath $args[1] -DevVm -PlanOnly";
        var info = new ProcessStartInfo("pwsh.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-CommandWithArgs", command, Path.Combine(root, "tools", "Install-HostAgent.ps1"), fixture.Path }) info.ArgumentList.Add(arg);
        using var child = Process.Start(info)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)); }
        catch (TimeoutException) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); throw; }
        var result = await stdout; var error = await stderr;
        if (tamper)
        {
            Assert.NotEqual(0, child.ExitCode);
            Assert.Empty(result);
            Assert.Contains("release_", error);
            return;
        }
        Assert.True(child.ExitCode == 0, error);
        using var json = JsonDocument.Parse(result);
        Assert.Equal("Shadow", json.RootElement.GetProperty("mode").GetString());
        Assert.True(json.RootElement.GetProperty("readOnly").GetBoolean());
        Assert.False(json.RootElement.GetProperty("changesPerformed").GetBoolean());
    }
}
