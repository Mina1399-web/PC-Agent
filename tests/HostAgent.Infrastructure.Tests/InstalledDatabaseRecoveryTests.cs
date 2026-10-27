using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

[Collection("ReleaseTooling")]
public sealed class InstalledDatabaseRecoveryTests
{
    // A rejected Start-Service parameter must not strand both services after a successful DB check.
    // Execute the operator script's actual finally block; replace only privileged SCM/file-hash reads.
    [Theory]
    [InlineData("both_running", "PASS", 2)]
    [InlineData("helper_originally_stopped", "PASS", 1)]
    [InlineData("helper_start_fails", "FAIL", 2)]
    [InlineData("helper_identity_changed", "FAIL", 1)]
    [InlineData("preflight_not_complete", "FAIL", 0)]
    [InlineData("both_originally_stopped", "PASS", 0)]
    [InlineData("helper_path_changed", "FAIL", 1)]
    [InlineData("helper_hash_changed", "FAIL", 1)]
    [InlineData("helper_start_times_out", "FAIL", 2)]
    public async Task Offline_check_restores_original_service_states_and_reports_failed_recovery(
        string scenario, string expectedResult, int expectedStartAttempts)
    {
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var target = Path.Combine(source, "scripts", "diagnostics", "Run-InstalledDatabaseIntegrityOffline.ps1");
        var temporary = Path.Combine(Path.GetTempPath(), "hostagent-recovery-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var probe = Path.Combine(temporary, "probe.ps1");
            await File.WriteAllTextAsync(probe, """
                param([string]$Target,[string]$Scenario)
                Set-StrictMode -Version Latest
                $ErrorActionPreference='Stop'
                $tokens=$null;$errors=$null
                $ast=[Management.Automation.Language.Parser]::ParseFile($Target,[ref]$tokens,[ref]$errors)
                if($errors.Count){throw 'script_parse_failed'}
                $outer=@($ast.EndBlock.Statements|Where-Object{$_ -is [Management.Automation.Language.TryStatementAst]})
                if($outer.Count -ne 1 -or -not $outer[0].Finally){throw 'recovery_block_missing'}
                $finally=$outer[0].Finally.Extent.Text
                $body=[scriptblock]::Create($finally.Substring(1,$finally.Length-2))
                $script:startAttempts=[Collections.Generic.List[string]]::new()
                $script:states=@{}
                $taskServices=@(
                    [pscustomobject]@{Name='HostAgent';State='Running';StartMode='Auto';StartName='NT SERVICE\HostAgent';PathName='fixed-monitor';ExecutablePath='fixed-monitor';ExecutableSha256='MONITOR';OriginallyRunning=($Scenario -ne 'both_originally_stopped')},
                    [pscustomobject]@{Name='HostAgent.Helper';State='Running';StartMode='Auto';StartName='LocalSystem';PathName='fixed-helper';ExecutablePath='fixed-helper';ExecutableSha256='HELPER';OriginallyRunning=($Scenario -notin @('helper_originally_stopped','both_originally_stopped'))}
                )
                foreach($expected in $taskServices){
                    $expected|Add-Member -NotePropertyName StopRequested -NotePropertyValue ($Scenario -ne 'preflight_not_complete')
                    $script:states[$expected.Name]=[pscustomobject]@{Name=$expected.Name;State='Stopped';StartMode=$expected.StartMode;StartName=$expected.StartName;PathName=$expected.PathName;ProcessId=0}
                }
                if($Scenario -eq 'helper_identity_changed'){$script:states['HostAgent.Helper'].StartName='unexpected-account'}
                if($Scenario -eq 'helper_path_changed'){$script:states['HostAgent.Helper'].PathName='unexpected-image'}
                foreach($definition in $ast.EndBlock.Statements|Where-Object{$_ -is [Management.Automation.Language.FunctionDefinitionAst]}){
                    . ([scriptblock]::Create($definition.Extent.Text))
                }
                function Get-TaskServiceSnapshot([string]$Name){return $script:states[$Name]}
                function Start-Service {
                    [CmdletBinding()]param([string]$Name)
                    $script:startAttempts.Add($Name)
                    if($Scenario -eq 'helper_start_fails' -and $Name -eq 'HostAgent.Helper'){throw 'synthetic_scm_start_denied'}
                    if($Scenario -eq 'helper_start_times_out' -and $Name -eq 'HostAgent.Helper'){
                        $script:states[$Name].State='Start Pending';return
                    }
                    $script:states[$Name].State='Running';$script:states[$Name].ProcessId=123
                }
                function Wait-TaskServiceState([string]$Name,[string]$Expected,[int]$TimeoutSeconds,[bool]$ForRecovery){
                    if(-not $ForRecovery -or $script:states[$Name].State -ne $Expected){throw 'recovery_wait_failed'}
                    return $script:states[$Name]
                }
                function Get-FileHash {
                    [CmdletBinding()]param([string]$LiteralPath,[string]$Algorithm)
                    [pscustomobject]@{Hash=if($LiteralPath -eq 'fixed-monitor'){'MONITOR'}elseif($Scenario -eq 'helper_hash_changed'){'CHANGED'}else{'HELPER'}}
                }
                $taskChild=$null;$taskCleanupFailures=[Collections.Generic.List[string]]::new()
                $taskResult='PASS';$taskFailureCode=$null;$taskStartTimeoutSeconds=45
                $taskWorkRoot=Join-Path $env:TEMP ('absent-work-'+[guid]::NewGuid().ToString('N'))
                $taskRunRoot=Join-Path $env:TEMP ('absent-evidence-'+[guid]::NewGuid().ToString('N'))
                $taskRunId='test';$taskDatabases=@();$taskIdentity=[Security.Principal.WindowsIdentity]::GetCurrent()
                $json=(& $body)|ConvertFrom-Json
                [pscustomobject]@{result=$json.result;failureCode=$json.failureCode;cleanupFailures=@($json.cleanupFailures);
                    services=@($json.servicesAfter);startAttempts=@($script:startAttempts.ToArray())}|ConvertTo-Json -Depth 6
                """);
            var info = new ProcessStartInfo("pwsh.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", probe, "-Target", target, "-Scenario", scenario })
                info.ArgumentList.Add(arg);
            using var child = Process.Start(info)!;
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (TimeoutException) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); throw; }
            var output = await stdout; var error = await stderr;
            Assert.True(child.ExitCode == 0, error + output);
            using var receipt = JsonDocument.Parse(output);
            var result = receipt.RootElement;
            Assert.Equal(expectedResult, result.GetProperty("result").GetString());
            var attempts = result.GetProperty("startAttempts").EnumerateArray().Select(x => x.GetString()).ToArray();
            Assert.Equal(expectedStartAttempts, attempts.Length);
            Assert.Equal(expectedStartAttempts == 2 ? new[] { "HostAgent.Helper", "HostAgent" } :
                expectedStartAttempts == 1 ? new[] { "HostAgent" } : Array.Empty<string>(), attempts);
            var services = result.GetProperty("services").EnumerateArray().ToArray();
            Assert.Equal(scenario is "preflight_not_complete" or "both_originally_stopped" ? "Stopped" : "Running", services[0].GetProperty("state").GetString());
            Assert.Equal(scenario == "both_running" ? "Running" : scenario == "helper_start_times_out" ? "Start Pending" : "Stopped", services[1].GetProperty("state").GetString());
            if (expectedResult == "PASS")
            {
                Assert.Equal(JsonValueKind.Null, result.GetProperty("failureCode").ValueKind);
                Assert.Empty(result.GetProperty("cleanupFailures").EnumerateArray());
            }
            else
            {
                Assert.Equal("service_restore_or_identity_check_failed", result.GetProperty("failureCode").GetString());
                if (scenario is "helper_start_fails" or "helper_start_times_out")
                    Assert.Contains(result.GetProperty("cleanupFailures").EnumerateArray(), x => x.GetString() == "HostAgent.Helper_restore_failed");
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
