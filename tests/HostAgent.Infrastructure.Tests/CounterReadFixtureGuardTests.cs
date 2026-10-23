using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

// The hosted workflow excludes FullyQualifiedName~ReleaseToolingTests. Keep
// these exact-VM operator boundaries in that exclusion without editing it.
[Collection("ReleaseTooling")]
public sealed class CounterReadFixtureGuardTests_ReleaseToolingTests
{
    [Fact]
    public async Task Intentional_fault_receipt_rejects_unrelated_1603_even_when_the_existing_state_matches_rollback_proof()
    {
        using var fixture = new ReleaseToolingTests.PackageFixture();
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var script = Path.Combine(fixture.Path, "fault-receipt-regression.ps1");
        File.WriteAllText(script, """
            param([string]$Harness)
            $ErrorActionPreference='Stop';Set-StrictMode -Version Latest
            $tokens=$null;$errors=$null
            $ast=[System.Management.Automation.Language.Parser]::ParseFile($Harness,[ref]$tokens,[ref]$errors)
            if($errors.Count){throw 'parse_failed'}
            $node=$ast.Find({param($n) $n-is[System.Management.Automation.Language.FunctionDefinitionAst]-and$n.Name-ceq'Assert-FixtureFaultReceipt'},$true)
            if(-not$node){'{"helperPresent":false}';exit 0}
            . ([scriptblock]::Create($node.Extent.Text))
            $proofNode=$ast.Find({param($n) $n-is[System.Management.Automation.Language.FunctionDefinitionAst]-and$n.Name-ceq'Assert-FixtureProof'},$true)
            if(-not$proofNode){throw 'proof_validation_missing'}
            . ([scriptblock]::Create($proofNode.Extent.Text))
            # An early failure can leave exactly this already-existing state.
            # It passes the real after-proof check without proving any rollback.
            $before=[pscustomobject]@{member=$false;owned=$false;journal='absent'}
            Assert-FixtureProof $before $false $false 'absent'
            $cases=@(
                @{name='early1603_already_before';expected=1603;phase='deferred_grant';d=0;c=0;accept=$false},
                @{name='missing_both';expected=1603;phase='deferred_grant';accept=$false},
                @{name='missing_commit';expected=1603;phase='deferred_grant';d=1;accept=$false},
                @{name='missing_deferred';expected=1603;phase='commit_grant';c=1;accept=$false},
                @{name='wrong_deferred_mode';expected=1603;phase='deferred_grant';d=0;c=1;accept=$false},
                @{name='wrong_commit_mode';expected=1603;phase='commit_grant';d=1;c=0;accept=$false},
                @{name='duplicate_deferred';expected=1603;phase='deferred_grant';d=2;c=0;accept=$false},
                @{name='duplicate_commit';expected=1603;phase='commit_grant';d=0;c=2;accept=$false},
                @{name='mixed_markers';expected=1603;phase='deferred_release';d=1;c=1;accept=$false},
                @{name='exact_grant';expected=1603;phase='deferred_grant';d=1;c=0;accept=$true},
                @{name='exact_release';expected=1603;phase='deferred_release';d=1;c=0;accept=$true},
                @{name='exact_commit';expected=1603;phase='commit_grant';d=0;c=1;accept=$true},
                @{name='success_no_fault';expected=0;phase='none';d=0;c=0;accept=$true},
                @{name='success_deferred_fault';expected=0;phase='none';d=1;c=0;accept=$false},
                @{name='success_commit_fault';expected=0;phase='none';d=0;c=1;accept=$false}
            )
            $results=@(foreach($case in $cases){
                $fields=@{msiExitCode=$case.expected;proof=$before}
                if($case.ContainsKey('d')){$fields.intentionalDeferredFaults=$case.d}
                if($case.ContainsKey('c')){$fields.intentionalCommitFaults=$case.c}
                $accepted=$false
                try{$null=Assert-FixtureFaultReceipt ([pscustomobject]$fields) $case.expected $case.phase;$accepted=$true}catch{}
                [pscustomobject]@{name=$case.name;expectedAccepted=$case.accept;accepted=$accepted}
            })
            [pscustomobject]@{helperPresent=$true;existingStatePassesAfterProof=$true;cases=$results}|ConvertTo-Json -Depth 5
            """);
        var result = await RunChild("pwsh.exe", "-NoProfile", "-NonInteractive", "-File", script,
            "-Harness", Path.Combine(sourceRoot, "scripts", "diagnostics", "Test-CounterReadMsiFixture.ps1"));
        Assert.True(result.Exit == 0, result.Error + result.Out);
        using var receipt = JsonDocument.Parse(result.Out);
        Assert.True(receipt.RootElement.GetProperty("helperPresent").GetBoolean(), "The phase must validate an intentional fault receipt independently of after-proof.");
        Assert.True(receipt.RootElement.GetProperty("existingStatePassesAfterProof").GetBoolean());
        var cases = receipt.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(15, cases.Length);
        foreach (var sample in cases)
            Assert.True(sample.GetProperty("expectedAccepted").GetBoolean() == sample.GetProperty("accepted").GetBoolean(),
                sample.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Actual_controller_callback_counts_only_exact_info_fault_markers_without_native_calls()
    {
        using var fixture = new ReleaseToolingTests.PackageFixture();
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var targets = Path.Combine(fixture.Path, "CallbackTargets.cs");
        File.WriteAllText(targets, "internal static class FixtureTargets { public const string RunId=null, GroupName=null, GroupSid=null, MemberSid=null, RegistryRoot=null, MsiPath=null, MsiSha256=null; }");
        var probe = Path.Combine(fixture.Path, "CallbackProbe.cs");
        File.WriteAllText(probe, """
            using System;
            using System.Reflection;
            internal static class CallbackProbe
            {
                static int Main()
                {
                    var type=typeof(CounterReadMsiFixtureController);
                    var flags=BindingFlags.NonPublic|BindingFlags.Static;
                    var ui=type.GetMethod("Ui",flags,null,new[]{typeof(IntPtr),typeof(uint),typeof(string)},null);
                    var deferred=type.GetField("intentionalDeferredFaults",flags);
                    var commit=type.GetField("intentionalCommitFaults",flags);
                    var failure=type.GetField("failure",flags);
                    if(ui==null || ui.ReturnType!=typeof(int) || deferred==null || commit==null || failure==null
                        || deferred.FieldType!=typeof(int) || commit.FieldType!=typeof(int))
                    {Console.WriteLine("{\"callbackContractPresent\":false}");return 0;}
                    deferred.SetValue(null,0);commit.SetValue(null,0);failure.SetValue(null,null);
                    const string d="HostAgent.GuidFixture.Fault.Deferred.v1";
                    const string c="HostAgent.GuidFixture.Fault.Commit.v1";
                    foreach(var token in new[]{d,c})
                    {
                        foreach(var text in new[]{"prefix"+token,token+"suffix",token.ToLowerInvariant(),"unknown"})
                            ui.Invoke(null,new object[]{IntPtr.Zero,0x04000000u,text});
                        ui.Invoke(null,new object[]{IntPtr.Zero,0x0A000000u,token});
                    }
                    var ignored=(int)deferred.GetValue(null)==0 && (int)commit.GetValue(null)==0;
                    ui.Invoke(null,new object[]{IntPtr.Zero,0x04000000u,d});
                    ui.Invoke(null,new object[]{IntPtr.Zero,0x04000000u,c});
                    var exact=(int)deferred.GetValue(null)==1 && (int)commit.GetValue(null)==1;
                    // A latched abort still cancels progress; an INFO message
                    // cannot clear it or change the cancellation boundary.
                    failure.SetValue(null,"hard_memory_floor_512");
                    var progress=(int)ui.Invoke(null,new object[]{IntPtr.Zero,0x0A000000u,d});
                    var info=(int)ui.Invoke(null,new object[]{IntPtr.Zero,0x04000000u,"unknown"});
                    var preserved=(string)failure.GetValue(null)=="hard_memory_floor_512"
                        && (int)deferred.GetValue(null)==1 && (int)commit.GetValue(null)==1;
                    Console.WriteLine("{\"callbackContractPresent\":true,\"noiseIgnored\":"+(ignored?"true":"false")
                        +",\"exactCounts\":"+(exact?"true":"false")+",\"failurePreserved\":"+(preserved?"true":"false")
                        +",\"progressReturn\":"+progress+",\"infoReturn\":"+info+"}");return 0;
                }
            }
            """);
        var executable = Path.Combine(fixture.Path, "CallbackProbe.exe");
        var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        var compile = await RunChild(compiler, "/nologo", "/langversion:5", "/target:exe", "/platform:x64", "/main:CallbackProbe",
            "/out:" + executable, Path.Combine(sourceRoot, "scripts", "diagnostics", "CounterReadMsiFixtureController.cs"), targets, probe);
        Assert.True(compile.Exit == 0, compile.Error + compile.Out);
        var result = await RunChild(executable);
        Assert.True(result.Exit == 0, result.Error + result.Out);
        using var receipt = JsonDocument.Parse(result.Out);
        Assert.True(receipt.RootElement.GetProperty("callbackContractPresent").GetBoolean(), "The real callback must expose the two private integer fault counters.");
        Assert.True(receipt.RootElement.GetProperty("noiseIgnored").GetBoolean());
        Assert.True(receipt.RootElement.GetProperty("exactCounts").GetBoolean());
        Assert.True(receipt.RootElement.GetProperty("failurePreserved").GetBoolean());
        Assert.Equal(2, receipt.RootElement.GetProperty("progressReturn").GetInt32());
        Assert.Equal(1, receipt.RootElement.GetProperty("infoReturn").GetInt32());
    }

    [Fact]
    public async Task Seed_removal_preserves_historical_journal_but_MSI_proof_still_requires_current_after_state()
    {
        using var fixture = new ReleaseToolingTests.PackageFixture();
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var script = Path.Combine(fixture.Path, "proof-regression.ps1");
        File.WriteAllText(script, """
            param([string]$Harness)
            $ErrorActionPreference='Stop'
            $tokens=$null;$errors=$null
            $ast=[System.Management.Automation.Language.Parser]::ParseFile($Harness,[ref]$tokens,[ref]$errors)
            if($errors.Count){throw 'parse_failed'}
            foreach($name in @('Assert-FixtureProof','Assert-FixtureSeedRemoval')){
                $node=$ast.Find({param($n) $n-is[System.Management.Automation.Language.FunctionDefinitionAst]-and$n.Name-ceq$name},$true)
                if(-not$node){throw 'seed_removal_validation_missing'}
                . ([scriptblock]::Create($node.Extent.Text))
            }
            $before=[pscustomobject]@{member=$true;owned=$false;journal='committed';journalBeforeMember=$true;journalBeforeOwned=$false;journalAfterMember=$true;journalAfterOwned=$false}
            $after=[pscustomobject]@{member=$false;owned=$false;journal='committed';journalBeforeMember=$true;journalBeforeOwned=$false;journalAfterMember=$true;journalAfterOwned=$false}
            Assert-FixtureSeedRemoval $before $after
            $msiRejected=$false
            try{Assert-FixtureProof $after $false $false 'committed'}catch{$msiRejected=$true}
            if(-not$msiRejected){throw 'msi_proof_weakened'}
            $after.journalAfterMember=$false
            $tamperRejected=$false
            try{Assert-FixtureSeedRemoval $before $after}catch{$tamperRejected=$true}
            if(-not$tamperRejected){throw 'historical_journal_tamper_accepted'}
            '{"historicalSeedAccepted":true,"msiDivergenceRejected":true,"journalTamperRejected":true}'
            """);
        var result = await RunChild("pwsh.exe", "-NoProfile", "-NonInteractive", "-File", script,
            "-Harness", Path.Combine(sourceRoot, "scripts", "diagnostics", "Test-CounterReadMsiFixture.ps1"));
        Assert.True(result.Exit == 0, result.Error + result.Out);
        using var receipt = JsonDocument.Parse(result.Out);
        Assert.True(receipt.RootElement.GetProperty("historicalSeedAccepted").GetBoolean());
        Assert.True(receipt.RootElement.GetProperty("msiDivergenceRejected").GetBoolean());
        Assert.True(receipt.RootElement.GetProperty("journalTamperRejected").GetBoolean());
    }
    [Fact]
    public async Task Actual_controller_policy_preserves_memory_cancel_and_deadline_boundaries_without_native_MSI_calls()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var directory = Path.Combine(Path.GetTempPath(), "counter-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var targets = Path.Combine(directory, "FixtureTargets.cs");
            File.WriteAllText(targets, "internal static class FixtureTargets { public const string RunId=null, GroupName=null, GroupSid=null, MemberSid=null, RegistryRoot=null, MsiPath=null, MsiSha256=null; }");
            var executable = Path.Combine(directory, "PolicyOnly.exe");
            var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
            var compile = await RunChild(compiler, "/nologo", "/langversion:5", "/target:exe", "/platform:x64", "/out:" + executable,
                Path.Combine(sourceRoot, "scripts", "diagnostics", "CounterReadMsiFixtureController.cs"), targets);
            Assert.True(compile.Exit == 0, compile.Error + compile.Out);
            var cases = new[]
            {
                ("768", "0", "false", "continue"),
                ("767.9", "0", "false", "continue"),
                ("512", "0", "false", "hard_memory_floor_512"),
                ("512.1", "0", "false", "continue"),
                ("NaN", "0", "false", "hard_memory_floor_512"),
                ("1024", "89.9", "false", "continue"),
                ("1024", "90", "false", "msi_cooperative_deadline"),
                ("1024", "0", "true", "cancel_requested")
            };
            foreach (var (free, seconds, cancelled, expected) in cases)
            {
                // --policy returns before administrator/target checks or any
                // Windows API. Null fixture constants make native use refuse.
                var result = await RunChild(executable, "--policy", free, seconds, cancelled);
                Assert.Equal(0, result.Exit);
                Assert.Equal(expected, result.Out.Trim());
                Assert.True(string.IsNullOrWhiteSpace(result.Error), result.Error);
            }
        }
        finally
        {
            var resolved = Path.GetFullPath(directory);
            Assert.Equal(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(resolved));
            Assert.StartsWith("counter-policy-", Path.GetFileName(resolved));
            Directory.Delete(resolved, recursive: true);
        }
    }

    [Fact]
    public async Task Actual_controller_reports_low_memory_warning_separately_from_abort_policy()
    {
        using var fixture = new ReleaseToolingTests.PackageFixture();
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var targets = Path.Combine(fixture.Path, "WarningTargets.cs");
        File.WriteAllText(targets, "internal static class FixtureTargets { public const string RunId=null, GroupName=null, GroupSid=null, MemberSid=null, RegistryRoot=null, MsiPath=null, MsiSha256=null; }");
        var probe = Path.Combine(fixture.Path, "WarningProbe.cs");
        File.WriteAllText(probe, """
            using System;
            using System.Reflection;
            internal static class WarningProbe
            {
                static int Main()
                {
                    // Contract: Warning(double) is a pure, separately observable
                    // warning code; Evaluate remains the cancellation decision.
                    // Reflection lets an absent method fail as a behavioral RED,
                    // rather than preventing the real controller from compiling.
                    var method=typeof(CounterReadFixturePolicy).GetMethod("Warning",BindingFlags.Public|BindingFlags.Static,
                        null,new[]{typeof(double)},null);
                    if(method==null || method.ReturnType!=typeof(string))
                    {
                        Console.WriteLine("{\"warningMethodPresent\":false}");return 0;
                    }
                    var values=new[]{768d,767.9d,512.1d,512d,1024d};
                    var results=new string[values.Length];
                    for(var index=0;index<values.Length;index++)
                    {
                        var warning=(string)method.Invoke(null,new object[]{values[index]});
                        var abort=CounterReadFixturePolicy.Evaluate(values[index],0,false);
                        // Only the documented fixed warning escapes this probe.
                        var knownWarning=warning==null?"null":warning=="soft_memory_floor_768"?"\"soft_memory_floor_768\"":"\"unexpected_warning\"";
                        var knownAbort=abort==null?"null":abort=="hard_memory_floor_512"?"\"hard_memory_floor_512\"":"\"unexpected_abort\"";
                        results[index]="{\"warning\":"+knownWarning+",\"abort\":"+knownAbort+"}";
                    }
                    Console.WriteLine("{\"warningMethodPresent\":true,\"samples\":["+String.Join(",",results)+"]}");return 0;
                }
            }
            """);
        var executable = Path.Combine(fixture.Path, "WarningProbe.exe");
        var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        var compile = await RunChild(compiler, "/nologo", "/langversion:5", "/target:exe", "/platform:x64", "/main:WarningProbe",
            "/out:" + executable, Path.Combine(sourceRoot, "scripts", "diagnostics", "CounterReadMsiFixtureController.cs"), targets, probe);
        Assert.True(compile.Exit == 0, compile.Error + compile.Out);
        var run = await RunChild(executable);
        Assert.True(run.Exit == 0, run.Error + run.Out);
        using var receipt = JsonDocument.Parse(run.Out);
        Assert.True(receipt.RootElement.GetProperty("warningMethodPresent").GetBoolean(),
            "The controller must expose Warning(double) separately from its abort decision.");
        var samples = receipt.RootElement.GetProperty("samples").EnumerateArray().ToArray();
        Assert.Equal(5, samples.Length);
        Assert.Equal(JsonValueKind.Null, samples[0].GetProperty("warning").ValueKind); // 768
        Assert.Equal(JsonValueKind.Null, samples[0].GetProperty("abort").ValueKind);
        foreach (var index in new[] { 1, 2 }) // 767.9 and 512.1: warning only.
        {
            Assert.Equal("soft_memory_floor_768", samples[index].GetProperty("warning").GetString());
            Assert.Equal(JsonValueKind.Null, samples[index].GetProperty("abort").ValueKind);
        }
        Assert.Equal(JsonValueKind.Null, samples[3].GetProperty("warning").ValueKind); // 512: emergency, not soft warning.
        Assert.Equal("hard_memory_floor_512", samples[3].GetProperty("abort").GetString());
        Assert.Equal(JsonValueKind.Null, samples[4].GetProperty("warning").ValueKind);
        Assert.Equal(JsonValueKind.Null, samples[4].GetProperty("abort").ValueKind);
    }

    [Theory]
    [InlineData(768d, false, false)]
    [InlineData(768.1d, false, true)]
    [InlineData(700d, true, true)]
    [InlineData(512d, true, false)]
    public async Task Actual_resource_guard_distinguishes_initial_headroom_from_runtime_emergency(
        double freeMiB, bool duringRun, bool expectedAccepted)
    {
        using var fixture = new ReleaseToolingTests.PackageFixture();
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var script = Path.Combine(fixture.Path, "resource-policy-regression.ps1");
        File.WriteAllText(script, """
            param([string]$Preparation,[double]$FreeMiB,[switch]$DuringRun)
            $ErrorActionPreference='Stop';$WarningPreference='SilentlyContinue'
            $tokens=$null;$errors=$null
            $ast=[System.Management.Automation.Language.Parser]::ParseFile($Preparation,[ref]$tokens,[ref]$errors)
            if($errors.Count){throw 'parse_failed'}
            $node=$ast.Find({param($n) $n-is[System.Management.Automation.Language.FunctionDefinitionAst]-and$n.Name-ceq'Assert-CounterFixtureResources'},$true)
            if(-not$node){throw 'resource_guard_missing'}
            # Execute the actual resource function, never the entry script.
            # Its only OS reads are replaced by local deterministic objects.
            . ([scriptblock]::Create($node.Extent.Text))
            $script:cimReads=0;$script:diskReads=0
            function Get-CimInstance {
                param([string]$ClassName)
                if($ClassName-cne'Win32_OperatingSystem'){throw 'unexpected_cim_query'}
                $script:cimReads++
                [pscustomobject]@{FreePhysicalMemory=($FreeMiB*1024)}
            }
            function Get-PSDrive {
                param([string]$Name)
                if($Name-cne'C'){throw 'unexpected_drive_query'}
                $script:diskReads++
                [pscustomobject]@{Free=15GB}
            }
            $accepted=$false
            try {
                if($DuringRun){$null=Assert-CounterFixtureResources -DuringRun}
                else{$null=Assert-CounterFixtureResources}
                $accepted=$true
            }catch{}
            [pscustomobject]@{accepted=$accepted;cimReads=$script:cimReads;diskReads=$script:diskReads;
                fakeDiskGiB=15;nativeOperationsPerformed=$false}|ConvertTo-Json
            """);
        var arguments = new List<string> { "-NoProfile", "-NonInteractive", "-File", script, "-Preparation",
            Path.Combine(sourceRoot, "scripts", "diagnostics", "Prepare-CounterReadMsiFixture.ps1"),
            "-FreeMiB", freeMiB.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (duringRun) arguments.Add("-DuringRun");
        var result = await RunChild("pwsh.exe", arguments.ToArray());
        Assert.True(result.Exit == 0, result.Error + result.Out);
        using var receipt = JsonDocument.Parse(result.Out);
        Assert.Equal(expectedAccepted, receipt.RootElement.GetProperty("accepted").GetBoolean());
        Assert.Equal(1, receipt.RootElement.GetProperty("cimReads").GetInt32());
        if (expectedAccepted) Assert.Equal(1, receipt.RootElement.GetProperty("diskReads").GetInt32());
        Assert.False(receipt.RootElement.GetProperty("nativeOperationsPerformed").GetBoolean());
    }

    [Theory]
    [InlineData("Test-CounterReadMsiFixture.ps1")]
    [InlineData("Prepare-CounterReadMsiFixture.ps1")]
    public async Task Ordinary_execute_refuses_before_creating_fixture_state(string script)
    {
        using var identity = WindowsIdentity.GetCurrent();
        Assert.False(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
            "This refusal test requires the ordinary DEV operator token.");
        var id = Guid.NewGuid().ToString("N");
        var expectedRoot = Path.Combine("C:\\ProgramData", "HostAgentCounterFixture-" + id);
        var beforeRoots = Directory.GetDirectories("C:\\ProgramData", "HostAgentCounterFixture-*").OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Assert.False(Directory.Exists(expectedRoot));
        var arguments = script.StartsWith("Prepare", StringComparison.Ordinal)
            ? new[] { "-RunId", id, "-Execute" } : new[] { "-Execute" };

        var result = await Run(script, arguments);

        Assert.Equal(1, result.Exit);
        Assert.True(string.IsNullOrWhiteSpace(result.Error), result.Error);
        using var receipt = JsonDocument.Parse(result.Out);
        Assert.Equal("NOTRUN", receipt.RootElement.GetProperty("status").GetString());
        Assert.Equal("elevated_operator_required", receipt.RootElement.GetProperty("reason").GetString());
        Assert.False(receipt.RootElement.GetProperty("changesPerformed").GetBoolean());
        Assert.False(receipt.RootElement.GetProperty("nativeMatrixRun").GetBoolean());
        if (script.StartsWith("Test-", StringComparison.Ordinal))
            Assert.Equal(JsonValueKind.Null, receipt.RootElement.GetProperty("quiescenceConfirmed").ValueKind);
        Assert.False(Directory.Exists(expectedRoot));
        Assert.Equal(beforeRoots, Directory.GetDirectories("C:\\ProgramData", "HostAgentCounterFixture-*").OrderBy(path => path, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Private_builder_compiles_CSharp5_and_real_Wix4_MSI_without_installing_or_creating_native_targets()
    {
        using var fixture = new ReleaseToolingTests.PackageFixture();
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var script = Path.Combine(fixture.Path, "build-private-fixture.ps1");
        File.WriteAllText(script, """
            param([string]$PackagePath,[string]$PreparationScript,[string]$RunId)
            $ErrorActionPreference='Stop'
            . $PreparationScript
            # Test-only override: no native adapter is instantiated. The real
            # compiler and WiX build only disposable Temp files under this root.
            function Assert-CounterFixtureAdmin { }
            $script:FixtureMonitor=$null
            $root=Join-Path $PackagePath 'private-fixture'
            $null=New-Item -ItemType Directory -Path $root
            $scope=[pscustomobject]@{Id=$RunId;Root=$root;Name=('HostAgentFixture_'+$RunId);
                Registry=('SOFTWARE\HostAgent.CounterReadFixture.'+$RunId);
                ProgramFiles=('C:\Program Files\HostAgentCounterFixture-'+$RunId)}
            $build=Build-CounterFixture $scope 'S-1-5-80-1-2-3-4-5' 'S-1-5-21-1-2-3-1000' $null
            $installer=New-Object -ComObject WindowsInstaller.Installer
            $db=$installer.OpenDatabase($build.Msi,0)
            function ReadRows([string]$Sql,[int]$Count){
                $v=$db.OpenView($Sql);$null=$v.Execute()
                try{while($null-ne($r=$v.Fetch())){$row=@();for($i=1;$i-le$Count;$i++){$row+=$r.StringData($i)};,$row}}
                finally{$null=$v.Close()}
            }
            $services=@(ReadRows 'SELECT `Name`,`StartType`,`StartName` FROM `ServiceInstall`' 3)
            if($services.Count-ne1-or$services[0][0]-cne$scope.Name-or$services[0][1]-ne'3'-or$services[0][2]-cne('NT SERVICE\'+$scope.Name)){throw 'fixture_service_schema_invalid'}
            $controls=@(ReadRows 'SELECT `Name`,`Event` FROM `ServiceControl`' 2)
            if($controls.Count-ne1-or$controls[0][0]-cne$scope.Name-or([int]$controls[0][1]-band51)-ne0){throw 'fixture_service_can_start_or_stop'}
            $actions=@(ReadRows 'SELECT `Action`,`Type`,`Target` FROM `CustomAction`' 3)
            $late=@($actions|Where-Object {$_[0]-ceq'FailCommit'})
            if($late.Count-ne1-or$late[0][1]-ne'3585'-or$late[0][2]-cne'ForceFailure'){throw 'fixture_late_commit_schema_invalid'}
            $sequence=@(ReadRows 'SELECT `Action`,`Condition`,`Sequence` FROM `InstallExecuteSequence`' 3)
            $grant=@($sequence|Where-Object {$_[0]-ceq'CommitCounterReadGrant'})
            $failure=@($sequence|Where-Object {$_[0]-ceq'FailCommit'})
            if($grant.Count-ne1-or$failure.Count-ne1-or[int]$failure[0][2]-le[int]$grant[0][2]-or-not$failure[0][1].Contains('FAIL_PHASE="commit_grant"')){throw 'fixture_late_commit_sequence_invalid'}
            [pscustomobject]@{built=$true;substitutionCount=$build.ExactSubstitutionCount;
                msiSha256=(Get-FileHash -LiteralPath $build.Msi).Hash;controllerExists=(Test-Path -LiteralPath $build.Controller);
                serviceDemandOnly=$true;serviceNeverStarts=$true;lateCommitFailureScheduled=$true;
                nativeTargetsCreated=$false;installerExecuted=$false}|ConvertTo-Json
            """);
        var result = await RunChild("pwsh.exe", "-NoProfile", "-NonInteractive", "-File", script,
            "-PackagePath", fixture.Path, "-PreparationScript", Path.Combine(sourceRoot, "scripts", "diagnostics", "Prepare-CounterReadMsiFixture.ps1"),
            "-RunId", Guid.NewGuid().ToString("N"));
        Assert.True(result.Exit == 0, result.Error + result.Out);
        using var receipt = JsonDocument.Parse(result.Out);
        Assert.True(receipt.RootElement.GetProperty("built").GetBoolean());
        Assert.Equal(4, receipt.RootElement.GetProperty("substitutionCount").GetInt32());
        Assert.True(receipt.RootElement.GetProperty("controllerExists").GetBoolean());
        Assert.True(receipt.RootElement.GetProperty("serviceDemandOnly").GetBoolean());
        Assert.True(receipt.RootElement.GetProperty("serviceNeverStarts").GetBoolean());
        Assert.True(receipt.RootElement.GetProperty("lateCommitFailureScheduled").GetBoolean());
        Assert.False(receipt.RootElement.GetProperty("nativeTargetsCreated").GetBoolean());
        Assert.False(receipt.RootElement.GetProperty("installerExecuted").GetBoolean());
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    public async Task Preparation_refuses_unsafe_guid_before_dependencies_or_any_writes(string id)
    {
        var result = await Run("Prepare-CounterReadMsiFixture.ps1", "-RunId", id);

        Assert.Equal(1, result.Exit);
        Assert.True(string.IsNullOrWhiteSpace(result.Error), result.Error);
        using var receipt = JsonDocument.Parse(result.Out);
        Assert.Equal("NOTRUN", receipt.RootElement.GetProperty("status").GetString());
        Assert.Equal("fixture_run_id_invalid", receipt.RootElement.GetProperty("reason").GetString());
        Assert.False(receipt.RootElement.GetProperty("changesPerformed").GetBoolean());
        Assert.False(receipt.RootElement.GetProperty("planPrepared").GetBoolean());
    }

    [Fact]
    public async Task Preparation_cannot_redirect_fixture_to_an_arbitrary_output_directory()
    {
        var id = Guid.NewGuid().ToString("N");
        var outside = Path.Combine(Path.GetTempPath(), "counter-fixture-forbidden-" + id);
        Assert.False(Directory.Exists(outside));

        var result = await Run("Prepare-CounterReadMsiFixture.ps1", "-RunId", id, "-FixtureRoot", outside);

        Assert.Equal(1, result.Exit);
        using var receipt = JsonDocument.Parse(result.Out);
        Assert.Equal("fixture_root_invalid", receipt.RootElement.GetProperty("reason").GetString());
        Assert.False(receipt.RootElement.GetProperty("changesPerformed").GetBoolean());
        Assert.False(Directory.Exists(outside));
    }

    private static async Task<(int Exit, string Out, string Error)> Run(string script, params string[] arguments)
    {
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(source, "scripts", "diagnostics", script);
        Assert.True(File.Exists(path), "The fixture boundary script is missing.");
        return await RunChild("pwsh.exe", new[] { "-NoProfile", "-NonInteractive", "-File", path }.Concat(arguments).ToArray());
    }

    private static async Task<(int Exit, string Out, string Error)> RunChild(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        using var child = Process.Start(info)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)); }
        catch (TimeoutException)
        {
            // Only this ordinary guard-test child exists; no MSI is launched.
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            throw;
        }
        return (child.ExitCode, await stdout, await stderr);
    }
}
