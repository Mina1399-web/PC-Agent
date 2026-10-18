$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'scripts/chaos/CompletePriorHelperCleanup.psm1') -Force
$module=Get-Module CompletePriorHelperCleanup
$candidate=@{Path=[IO.Path]::GetFullPath((Join-Path $repo '..\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261017-complete-r6'));ManifestSha256='76B09BD10A479B55EC98F4073A4F76E10CC753309FD6F36F85594374CB0F2737'}
$operatorSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
function Assert-Prior([bool]$Condition,[string]$Code){if(-not$Condition){throw ('prior_helper_cleanup_assertion_failed:'+ $Code)}}
# The real read-only pinned evidence must prove exactly the known three owned
# registrations; the old 228 incidental output records must never be targets.
$owned=@(Get-CompletePriorHelperCleanupPlan -Candidate $candidate -OperatorSid $operatorSid)
Assert-Prior ($owned.Count-eq3) 'three_owned_targets_not_proved'
Assert-Prior (($owned.RunId-join',')-ceq'b75a0b684dc84d9ea88190cc702828d1,f6b56e621cb140b8ae65975f385b317a,66dea5c190424838915e802850350655') 'unrelated_target_authorized'
Assert-Prior (@($owned|Where-Object {-not$_.RootCreated-or-not$_.CreationAttempted-or-not$_.Created-or-not$_.CopyCompleted}).Count-eq0) 'ownership_not_recorded'
$cases=1
& $module {
 $script:testProofDrift=$null
 $script:realDataSnapshot=${function:Get-CompletePriorHelperDataSnapshot}
 function script:Get-FileHash {
  param([string]$LiteralPath,[string]$Algorithm)
  if($script:testProofDrift-eq'summary_hash'-and$LiteralPath-like'*summary.json'){return [pscustomobject]@{Hash=('0'*64)}}
  Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $LiteralPath -Algorithm $Algorithm
 }
 function script:Get-Content {
  param([string]$LiteralPath,[switch]$Raw)
  $content=Microsoft.PowerShell.Management\Get-Content -LiteralPath $LiteralPath -Raw
  if($LiteralPath-like'*targets.json'-and$script:testProofDrift){
   $rows=$content|ConvertFrom-Json;$valid=@($rows|Where-Object RunId)
   if($script:testProofDrift-eq'ownership'){$valid[0].Created=$false}
   if($script:testProofDrift-eq'path'){$valid[0].Root='C:\ProgramData\HostAgent'}
   if($script:testProofDrift-eq'duplicate'){$rows+=@($valid[0])}
   return ($rows|ConvertTo-Json -Depth 6)
  }
  return $content
 }
}
foreach($drift in @('summary_hash','ownership','path','duplicate')){
 & $module {param($value)$script:testProofDrift=$value} $drift
 $caught=$false;try{Get-CompletePriorHelperCleanupPlan -Candidate $candidate -OperatorSid $operatorSid|Out-Null}catch{$caught=$true}
 Assert-Prior $caught ('evidence_drift_accepted:'+ $drift);$cases++
}
& $module {
 $script:testProofDrift=$null
 $script:testDrift=$null;$script:testConfirmCalls=0;$script:testManifestCalls=0;$script:testDataCalls=0;$script:testEnvironmentCalls=0
 function script:Assert-CompletePriorHelperEnvironment {param([string]$OperatorSid)$script:testEnvironmentCalls++}
 function script:Assert-CompleteHelperRegistration {
  param($Target)
  if($script:testDrift-eq'config'){throw 'isolated_target_configuration_mismatch'}
  [pscustomobject]@{Name=$Target.Name;State=$(if($script:testDrift-eq'running'){'Running'}else{'Stopped'});ProcessId=$(if($script:testDrift-eq'running'){99999}else{0});StartMode='Manual'}
 }
 function script:Assert-CompleteHelperManifest {
  param([string]$Path,[string]$ManifestSha256,[scriptblock]$Guard,[switch]$WithCanaries)
  & $Guard|Out-Null;$script:testManifestCalls++
  if($script:testDrift-eq'manifest'){throw 'candidate_file_hash_mismatch'}
 }
 function script:Get-CompletePriorHelperDataSnapshot {
  param($Target,[scriptblock]$Guard)
  & $Guard|Out-Null;$script:testDataCalls++
  if($script:testDrift-eq'data_limit'){throw 'prior_cleanup_data_inventory_limit'}
  [pscustomobject]@{fileCount=1;totalBytes=10;files=@([pscustomobject]@{name='fixture-metadata.json';bytes=10;sha256=$(if($script:testDrift-eq'data_changed'-and$script:testDataCalls-gt3){'B'*64}else{'A'*64})})}
 }
 function script:Confirm-CompleteHelperTargets {
  param([object[]]$Targets,[scriptblock]$Guard)
  if($script:testEnvironmentCalls-lt1-or$script:testManifestCalls-ne3-or$script:testDataCalls-ne3){throw 'control_before_all_prechecks'}
  $script:testConfirmCalls++
  $emitted=0
  foreach($target in $Targets){
   & $Guard|Out-Null
   if($script:testDrift-eq'cleanup_count'-and$emitted-eq2){continue}
   [pscustomobject]@{RunId=$target.RunId;Name=$target.Name;State=$(if($script:testDrift-eq'cleanup_state'){'Running'}else{'Stopped'});ProcessId=0;StartMode='Disabled';Quiescent=$true;Preserved=$true;PackageVerified=($script:testDrift-ne'cleanup_package');Root=$target.Root;Data=$target.Data;GuardFailed=$false;ResourceAbort=$false}
   $emitted++
  }
 }
}
foreach($drift in @($null,'config','running','manifest','data_limit','data_changed','guard','cleanup_count','cleanup_state','cleanup_package','cleanup_guard')){
 & $module {param($value)$script:testDrift=$value;$script:testConfirmCalls=0;$script:testManifestCalls=0;$script:testDataCalls=0;$script:testEnvironmentCalls=0} $drift
 $guard=if($drift-eq'guard'){{throw 'protected_helper_state_or_pid_changed'}}else{{}}
 $cleanupGuard=if($drift-eq'cleanup_guard'){{throw 'protected_helper_state_or_pid_changed'}}else{{}}
 $caught=$false;$result=$null
 try{$result=Invoke-CompletePriorHelperCleanup -Candidate $candidate -OperatorSid $operatorSid -Guard $guard -CleanupGuard $cleanupGuard}catch{$caught=$true}
 if(-not$drift){
  Assert-Prior (-not$caught-and$result.ok-eq$true-and@($result.targets).Count-eq3-and$result.dataUnchanged-eq$true) 'verified_cleanup_not_proved'
 }else{Assert-Prior $caught ('execute_drift_accepted:'+ $drift)}
 $calls=& $module {$script:testConfirmCalls}
 $expectedCalls=if(-not$drift-or$drift-in@('data_changed','cleanup_count','cleanup_state','cleanup_package','cleanup_guard')){1}else{0}
 Assert-Prior ($calls-eq$expectedCalls) ('unexpected_control_boundary:'+ $drift)
 $cases++
}
'complete_prior_helper_cleanup_execute_passed:'+ $cases
# Exercise the real inventory/cap code with synthetic filesystem metadata;
# no fixture files or protected service data are created, changed or read.
& $module {
 function script:Assert-CompleteHelperPlainPath {param([string]$Path)}
 function script:Get-CompleteHelperTreeFiles {
  param([string]$Path,[scriptblock]$Guard)
  for($i=0;$i-lt$script:inventoryCount;$i++){
   [pscustomobject]@{FullName=($Path+'\fixture-'+$i+'.json');Length=$script:inventoryBytes}
  }
 }
 function script:Get-FileHash {param([string]$LiteralPath,[string]$Algorithm)[pscustomobject]@{Hash=('A'*64)}}
}
foreach($case in @(@{count=128;bytes=65536;reject=$false},@{count=129;bytes=1;reject=$true},@{count=1;bytes=(8MB+1);reject=$true})){
 & $module {param($count,$bytes)$script:inventoryCount=$count;$script:inventoryBytes=$bytes} $case.count $case.bytes
 $caught=$false;$snapshot=$null
 try{$snapshot=& $module {param($target)& $script:realDataSnapshot $target {}} $owned[0]}catch{$caught=$true}
 Assert-Prior ($caught-eq$case.reject) 'data_inventory_limit_not_enforced'
 if(-not$case.reject){Assert-Prior ($snapshot.fileCount-eq128-and$snapshot.totalBytes-eq8MB) 'exact_inventory_boundary_changed'}
 $cases++
}
'complete_prior_helper_cleanup_passed:'+ $cases+';real_service_operations=0;pinned_evidence=read_only;execute_boundaries=synthetic'
