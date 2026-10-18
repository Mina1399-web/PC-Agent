param([switch]$RejectRuntimeHash)
$ErrorActionPreference='Stop'
# Regression: a published campaign must pass read-only preflight without a
# build SDK. Exercise the real entry point; never pass -Execute or control SCM.
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$main=Join-Path $repo 'scripts\chaos\run-complete-dev-resilience.ps1'
$before=Get-CimInstance Win32_Service -Filter "Name='HostAgent'" -OperationTimeoutSec 5
$info=[Diagnostics.ProcessStartInfo]::new()
$info.FileName='C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
$fixture=Join-Path $PSScriptRoot 'Invoke-RuntimeOnlyPreflight.ps1'
$info.Arguments='-NoProfile -NonInteractive -File "'+$fixture+'"'
if($RejectRuntimeHash){$info.Arguments+=' -WrongRuntimeHash'}
$info.UseShellExecute=$false;$info.CreateNoWindow=$true
$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
$info.EnvironmentVariables['PSModulePath']='C:\Windows\System32\WindowsPowerShell\v1.0\Modules;C:\Program Files\WindowsPowerShell\Modules'
$info.EnvironmentVariables['DOTNET_ROOT']='C:\HostAgent-DEV-nonexistent-sdk'
$info.EnvironmentVariables['DOTNET_ROOT_X64']='C:\HostAgent-DEV-nonexistent-sdk'
$child=[Diagnostics.Process]::Start($info)
$outTask=$child.StandardOutput.ReadToEndAsync();$errTask=$child.StandardError.ReadToEndAsync()
$clock=[Diagnostics.Stopwatch]::StartNew();$minimum=99999.0
try {
 while(-not $child.HasExited){
  $free=(Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 5).FreePhysicalMemory/1024
  $minimum=[math]::Min($minimum,$free)
  if($free -lt 768 -or $clock.Elapsed.TotalSeconds -ge 45){throw 'readonly_test_resource_or_deadline_abort'}
  Start-Sleep -Milliseconds 250
 }
 $child.WaitForExit();$out=$outTask.GetAwaiter().GetResult();$err=$errTask.GetAwaiter().GetResult()
 if($RejectRuntimeHash){
  if($child.ExitCode -eq 0 -or $err -notmatch 'system_runtime_host_identity_mismatch'){
   throw 'changed_system_runtime_identity_not_rejected'
  }
  $after=Get-CimInstance Win32_Service -Filter "Name='HostAgent'" -OperationTimeoutSec 5
  foreach($property in @('Name','State','ProcessId','PathName','StartName','StartMode')){
   if($before.$property -cne $after.$property){throw 'protected_service_changed_during_rejection_test'}
  }
  [pscustomobject]@{scenario='changed_system_runtime_identity_rejected';ok=$true;childExit=$child.ExitCode;
   minimumFreeMiB=[math]::Round($minimum,1);serviceOperations=0;executeUsed=$false}|ConvertTo-Json -Compress
  return
 }
 if($child.ExitCode -ne 0){
  $reason=if($err -match 'simulated_build_sdk_unavailable'){'_sdk_unavailable'}else{'_other_failure'}
  throw ('runtime_only_preflight_expected_exit_0_actual_'+$child.ExitCode+$reason)
 }
 if($err){throw 'runtime_only_preflight_unexpected_stderr'}
 $result=$out|ConvertFrom-Json
 if($result.mode -ne 'READ_ONLY_PREFLIGHT' -or @($result.phases).Count -ne 10 -or
    @($result.phases|Where-Object kind -eq 'Manual').Count -ne 1){throw 'runtime_only_preflight_contract_changed'}
 if($result.runtimeRoot -cne 'C:\Program Files\dotnet'){throw 'runtime_only_preflight_wrong_runtime_identity'}
 $after=Get-CimInstance Win32_Service -Filter "Name='HostAgent'" -OperationTimeoutSec 5
 foreach($property in @('Name','State','ProcessId','PathName','StartName','StartMode')){
  if($before.$property -cne $after.$property){throw 'protected_service_changed_during_readonly_test'}
 }
 [pscustomobject]@{scenario='published_campaign_without_build_sdk';ok=$true;exitCode=0;minimumFreeMiB=[math]::Round($minimum,1);
  protectedState=$after.State;protectedPid=$after.ProcessId;runtimeRoot=$result.runtimeRoot;candidateManifest=$result.candidateManifestSha256;
  serviceOperations=0;executeUsed=$false}|ConvertTo-Json -Compress
}finally{
 if(-not $child.HasExited){$child.Kill();[void]$child.WaitForExit(5000)}
 $child.Dispose()
}
