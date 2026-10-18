$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$module=Import-Module (Join-Path $repo 'scripts/chaos/CompleteHelperTargets.psm1') -Force -PassThru
& $module {
 $target=@(Get-CompleteHelperTargetPlan @{Path='C:\candidate-fixture';ManifestSha256=('A'*64)} @('11111111111111111111111111111111','22222222222222222222222222222222','33333333333333333333333333333333') 'S-1-5-21-111-222-333-1001')[0]
 function Get-ItemProperty { [pscustomobject]@{Type=16} }
 function Get-CimInstance {
  param($ClassName,$Filter,$OperationTimeoutSec,$ErrorAction)
  if($ClassName-eq'Win32_Process'){return $script:raceProcess}
  if($Filter-eq"Name='HostAgent'"){return [pscustomobject]@{ProcessId=3632}}
  $script:raceReads++
  if($script:raceReads-eq1){return $script:raceInitial}
  return $script:raceFinal
 }
 $checked=0
 foreach($case in @('exited_and_stopped','missing_but_running','stopped_with_pid','reconfigured_after_exit','mismatched_image','mismatched_command','mismatched_session','valid_running')){
  $script:raceReads=0
  $script:raceInitial=[pscustomobject]@{Name=$target.Name;PathName=$target.BinaryPath;StartName=('NT SERVICE\'+$target.Name);StartMode='Manual';State='Running';ProcessId=9000}
  $script:raceFinal=$script:raceInitial.PSObject.Copy();$script:raceFinal.State='Stopped';$script:raceFinal.ProcessId=0
  $script:raceProcess=$null
  switch($case){
   'missing_but_running' {$script:raceFinal=$script:raceInitial}
   'stopped_with_pid' {$script:raceFinal.ProcessId=9000}
   'reconfigured_after_exit' {$script:raceFinal.PathName='C:\wrong.exe'}
   {$_-in@('mismatched_image','mismatched_command','mismatched_session','valid_running')} {
    $script:raceProcess=[pscustomobject]@{ExecutablePath=$target.Image;CommandLine=$target.BinaryPath;SessionId=0}
    if($case-eq'mismatched_image'){$script:raceProcess.ExecutablePath='C:\wrong.exe'}
    if($case-eq'mismatched_command'){$script:raceProcess.CommandLine='wrong'}
    if($case-eq'mismatched_session'){$script:raceProcess.SessionId=1}
   }
  }
  $caught=$false;$observed=$null
  try{$observed=Assert-CompleteHelperRegistration $target}catch{$caught=$true;if($case-in@('exited_and_stopped','valid_running')){throw}}
  if($caught-ne($case-notin@('exited_and_stopped','valid_running'))){throw ('registration_race_acceptance_failed:'+ $case)}
  if($case-eq'exited_and_stopped'-and($observed.State-ne'Stopped'-or$observed.ProcessId-ne0-or$script:raceReads-ne2)){throw 'fresh_stopped_identity_not_proved'}
  $checked++
 }
 [pscustomobject]@{suite='complete-helper-registration-race';passed=$checked;failed=0;serviceOperations=0;osBoundaries='stubbed'}|ConvertTo-Json -Compress
}
