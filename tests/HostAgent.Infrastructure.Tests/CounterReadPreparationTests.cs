using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace HostAgent.Infrastructure.Tests;

// Keep exact-VM MSI tests inside the existing hosted-build exclusion.
public sealed partial class ReleaseToolingTests
{
    // Exercise the actual packaged action through a disposable, read-only MSI
    // session. No DoAction, installation, native membership adapter or service
    // operation is invoked. ProductCode remains identical across both attempts.
    [Fact]
    public async Task Actual_prepare_uses_fresh_attempt_ids_and_refuses_disabled_rollback_without_changing_action_data()
    {
        using var fixture = new ReleaseToolingTests.PackageFixture();
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));
        var tools = Path.Combine(workspace, "HostAgent-DEV-tools");
        var build = await ReleaseToolingTests.Run("Build-Installer.ps1", fixture.Path,
            "-WixPath", Path.Combine(tools, "wix", "wix.exe"), "-DevVm");
        Assert.True(build.Exit == 0, build.Error + build.Out);
        using var buildResult = JsonDocument.Parse(build.Out);
        var intermediate = buildResult.RootElement.GetProperty("intermediate").GetString()!;
        var probeDirectory = Path.Combine(fixture.Path, "prepare-action-probe");
        Directory.CreateDirectory(probeDirectory);
        var managedAction = Path.Combine(probeDirectory, "Preflight.dll");
        File.Copy(Path.Combine(intermediate, "Preflight.dll"), managedAction);
        var dtf = Path.Combine(probeDirectory, "WixToolset.Dtf.WindowsInstaller.dll");
        File.Copy(Path.Combine(tools, "dtf-5.0.2", "wixtoolset.dtf.windowsinstaller", "lib", "net20",
            "WixToolset.Dtf.WindowsInstaller.dll"), dtf);
        var source = Path.Combine(probeDirectory, "PreparationProbe.cs");
        File.WriteAllText(source, ProbeSource, new UTF8Encoding(false));
        var executable = Path.Combine(probeDirectory, "PreparationProbe.exe");
        var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        var compile = await RunProbe(compiler, "/nologo", "/optimize+", "/target:exe", "/platform:x64",
            "/out:" + executable, "/reference:" + managedAction, "/reference:" + dtf, source);
        Assert.True(compile.Exit == 0, compile.Error + compile.Out);
        File.WriteAllText(executable + ".config",
            "<configuration><startup><supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.8\" /></startup></configuration>",
            new UTF8Encoding(false));

        var run = await RunProbe(executable, Path.Combine(fixture.Path, "HostAgent.msi"));
        Assert.True(run.Exit == 0, run.Error + run.Out);
        output.WriteLine(run.Out);
        using var receipt = JsonDocument.Parse(run.Out);
        var result = receipt.RootElement;
        Assert.True(result.GetProperty("MatchingActionData").GetBoolean());
        Assert.True(result.GetProperty("SessionProductCodeUnchanged").GetBoolean());
        Assert.Equal(2, result.GetProperty("RollbackDisabledRejected").GetInt32());
        var firstGrant = result.GetProperty("GrantTransaction1").GetString()!;
        var secondGrant = result.GetProperty("GrantTransaction2").GetString()!;
        var firstRelease = result.GetProperty("ReleaseTransaction1").GetString()!;
        var secondRelease = result.GetProperty("ReleaseTransaction2").GetString()!;
        Assert.Matches("^g[0-9a-f]{32}$", firstGrant);
        Assert.Matches("^g[0-9a-f]{32}$", secondGrant);
        Assert.Matches("^r[0-9a-f]{32}$", firstRelease);
        Assert.Matches("^r[0-9a-f]{32}$", secondRelease);
        Assert.NotEqual(firstGrant, secondGrant);
        Assert.NotEqual(firstRelease, secondRelease);
    }

    private static async Task<(int Exit, string Out, string Error)> RunProbe(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var child = Process.Start(info)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)); }
        catch (TimeoutException)
        {
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            throw;
        }
        return (child.ExitCode, await stdout, await stderr);
    }

    private const string ProbeSource = """
        using System;
        using System.Collections.Generic;
        using HostAgent.Installer;
        using WixToolset.Dtf.WindowsInstaller;

        internal static class PreparationProbe
        {
            static readonly string[] Names = {
                "GrantCounterRead", "RollbackCounterReadGrant", "CommitCounterReadGrant",
                "ReleaseCounterRead", "RollbackCounterReadRelease", "CommitCounterReadRelease"
            };
            static void Require(bool condition, string code)
            {
                if (!condition) throw new InvalidOperationException(code);
            }
            static string Transaction(Session session, string mode)
            {
                var value = session[mode + "CounterRead"];
                Require(value == session["RollbackCounterRead" + mode], "rollback_data_mismatch");
                Require(value == session["CommitCounterRead" + mode], "commit_data_mismatch");
                var data = new CustomActionData(value);
                Require(data.Count == 1 && data.ContainsKey("Transaction"), "unexpected_action_data");
                var id = data["Transaction"];
                Guid parsed;
                Require(id.Length == 33 && id[0] == (mode == "Grant" ? 'g' : 'r') &&
                    Guid.TryParseExact(id.Substring(1), "N", out parsed), "invalid_transaction_id");
                return id;
            }
            static int Main(string[] args)
            {
                try
                {
                    Require(args.Length == 1, "arguments_required");
                    // OpenPackage(ignoreMachineState:true) creates an inspection
                    // session only. Never invoke DoAction or an execute sequence.
                    using (var session = Installer.OpenPackage(args[0], true))
                    {
                        var product = session["ProductCode"];
                        Require(!String.IsNullOrEmpty(product), "missing_product_code");
                        session["DISABLEROLLBACK"] = "";
                        session["RollbackDisabled"] = "";
                        Require(CounterReadActions.PrepareCounterRead(session) == ActionResult.Success, "first_prepare_failed");
                        var grant1 = Transaction(session, "Grant");
                        var release1 = Transaction(session, "Release");
                        Require(CounterReadActions.PrepareCounterRead(session) == ActionResult.Success, "second_prepare_failed");
                        var grant2 = Transaction(session, "Grant");
                        var release2 = Transaction(session, "Release");
                        Require(product == session["ProductCode"], "product_code_changed");

                        int rejected = 0;
                        foreach (var property in new[] { "DISABLEROLLBACK", "RollbackDisabled" })
                        {
                            var before = new Dictionary<string, string>();
                            foreach (var name in Names) before.Add(name, session[name]);
                            session[property] = "1";
                            Require(CounterReadActions.PrepareCounterRead(session) == ActionResult.Failure, "rollback_guard_not_enforced");
                            foreach (var name in Names)
                                Require(before[name] == session[name], "guard_changed_action_data");
                            session[property] = "";
                            rejected++;
                        }
                        // IDs are validated as fixed GUID tokens before JSON output.
                        Console.WriteLine("{\"GrantTransaction1\":\"" + grant1 +
                            "\",\"GrantTransaction2\":\"" + grant2 +
                            "\",\"ReleaseTransaction1\":\"" + release1 +
                            "\",\"ReleaseTransaction2\":\"" + release2 +
                            "\",\"MatchingActionData\":true,\"SessionProductCodeUnchanged\":true,\"RollbackDisabledRejected\":" + rejected + "}");
                    }
                    return 0;
                }
                catch (Exception)
                {
                    Console.Error.WriteLine("prepare_action_fixture_failed");
                    return 2;
                }
            }
        }
        """;
}
