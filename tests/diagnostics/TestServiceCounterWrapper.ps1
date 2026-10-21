# DEV-only read-only validation of the actual operator artifact's guards.
$ErrorActionPreference='Stop'
if((Get-CimInstance Win32_ComputerSystemProduct).UUID -ne '46543236-8C5E-49DA-953C-DB17F9B1E402'){throw 'approved_dev_vm_required'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$path=Join-Path $repo 'scripts\diagnostics\Read-ServiceDiskCounters.ps1'
$tokens=$null;$parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$parseErrors)
if($parseErrors.Count){throw 'operator_script_syntax_invalid'}
$definitions=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in @('Assert-PlainPath','Read-InstalledIdentity','Invoke-FixedProbe','Complete-ProbeMonitor','Open-PinnedInput')},$false))
if($definitions.Count -ne 5){throw 'operator_guard_definition_missing'}
foreach($definition in $definitions){. ([scriptblock]::Create($definition.Extent.Text))}
$manifest=Join-Path (Split-Path $repo) 'HostAgent-DEV-releases\HostAgent-1.2.5-dev-view-summary-r1\manifest.psd1'
if((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -cne '6D302315BE3AE5762D06C34B6D54530730BA35A5406E6F7FC52F5E5B32F184C2'){throw 'validation_manifest_mismatch'}
$manifestText=Get-Content -LiteralPath $manifest -Raw
$critical=@('Service/HostAgent.Service.exe','Service/HostAgent.Service.dll','Service/HostAgent.Core.dll','Service/HostAgent.Infrastructure.dll','Helper/HostAgent.Helper.exe','Helper/HostAgent.Helper.dll')
$valid=Read-InstalledIdentity
if($valid.hashes.Count -ne 6){throw 'installed_hash_count_incorrect'}
$original=$manifestText
$manifestText=$original.Replace("Path='Service/HostAgent.Core.dll'","Path='Other/HostAgent.Core.dll'")
$denied=$false
try{Read-InstalledIdentity|Out-Null}catch{if($_.Exception.Message -cne 'critical_entry_missing'){throw};$denied=$true}
if(-not $denied){throw 'missing_manifest_entry_accepted'}
$manifestText=$original.Replace([string]$valid.hashes['Service/HostAgent.Core.dll'],('0'*64))
$denied=$false
try{Read-InstalledIdentity|Out-Null}catch{if($_.Exception.Message -cne 'installed_critical_hash_mismatch'){throw};$denied=$true}
if(-not $denied){throw 'incorrect_payload_hash_accepted'}
$runnerCases=0
if($PSVersionTable.PSVersion.Major -ge 7){
 Add-Type -LiteralPath (Join-Path $repo 'scripts\chaos\BoundedScmPressure.cs')
 $python=Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
 $scratch=Join-Path $env:LOCALAPPDATA ('HostAgent-DEV-evidence\counter-wrapper-validation-'+[Guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path $scratch|Out-Null
 $pinFixture=Join-Path $scratch 'pinned-input.txt'
 [IO.File]::WriteAllText($pinFixture,'abc',[Text.UTF8Encoding]::new($false))
 $denied=$false
 try{(Open-PinnedInput $pinFixture ('0'*64)).Dispose()}catch{if($_.Exception.Message -cne 'pinned_input_mismatch'){throw};$denied=$true}
 if(-not $denied){throw 'unverified_input_accepted'}
 $pinned=Open-PinnedInput $pinFixture 'BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD'
 try{
  $writeDenied=$false
  try{[IO.File]::WriteAllText($pinFixture,'changed')}catch [IO.IOException]{$writeDenied=$true}
  if(-not $writeDenied){throw 'pinned_input_not_write_locked'}
 }finally{$pinned.Dispose()}
 $probe=Join-Path $repo 'scripts\diagnostics\Read-ServiceDiskCounters.py'
 $locks=@()
 try{
  $inputDefinition=$ast.Find({param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and $node.Left.VariablePath.UserPath -ceq 'inputs'},$true)
  $pinLoop=$ast.Find({param($node) $node -is [Management.Automation.Language.ForEachStatementAst] -and $node.Variable.VariablePath.UserPath -ceq 'inputFile'},$true)
  if(-not $inputDefinition -or -not $pinLoop){throw 'actual_pin_stage_missing'}
  . ([scriptblock]::Create($inputDefinition.Extent.Text))
  . ([scriptblock]::Create($pinLoop.Extent.Text))
  if($locks.Count -ne 4){throw 'actual_pin_inputs_not_verified'}
 }finally{foreach($handle in $locks){$handle.Dispose()}}
 $normal=Join-Path $scratch 'normal-child.py'
 'print(''{"completed":true}'')'|Set-Content -LiteralPath $normal
 $normalLogs=Join-Path $scratch 'normal';New-Item -ItemType Directory -Path $normalLogs|Out-Null
 $monitor=[pscustomobject]@{FailureCode=$null}
 $normalRun=Invoke-FixedProbe @('-I','-B',$normal) $normalLogs
 if($normalRun.exitCode -ne 0 -or (Get-Content -LiteralPath $normalRun.stdoutPath -Raw|ConvertFrom-Json).completed -ne $true){throw 'normal_child_completion_not_proved'}
 $runnerCases++
 $childScript=Join-Path $scratch 'bounded-child.py'
 @('import os,pathlib,sys,time','pathlib.Path(sys.argv[1]).write_text(str(os.getpid()))','time.sleep(30)')|Set-Content -LiteralPath $childScript
 foreach($code in @('soft_free_memory_floor_768_mib','hard_free_memory_floor_512_mib')){
  $logs=Join-Path $scratch $code;New-Item -ItemType Directory -Path $logs|Out-Null
  $marker=Join-Path $logs 'own-child-pid.txt'
  $monitor=[pscustomobject]@{Watch=[Diagnostics.Stopwatch]::StartNew();Code=$code}
  $monitor|Add-Member ScriptProperty FailureCode {if($this.Watch.ElapsedMilliseconds -ge 3000){$this.Code}else{$null}}
  $denied=$false
  try{Invoke-FixedProbe @('-I','-B',$childScript,$marker) $logs|Out-Null}catch{if($_.Exception.Message -cne $code){throw};$denied=$true}
  if(-not $denied -or -not(Test-Path -LiteralPath $marker)){throw 'running_child_abort_not_proved'}
  $childId=[int](Get-Content -LiteralPath $marker)
  if(Get-Process -Id $childId -ErrorAction SilentlyContinue){throw 'own_child_survived_abort'}
  $runnerCases++
 }
}
$late=[pscustomobject]@{FailureCode=$null}
$late|Add-Member ScriptMethod Dispose {$this.FailureCode='soft_free_memory_floor_768_mib'}
if((Complete-ProbeMonitor $late) -cne 'soft_free_memory_floor_768_mib'){throw 'late_memory_latch_not_propagated'}
[pscustomobject]@{syntax='PASS';installedCriticalHashes=6;guardCasesPassed=3;runnerCasesPassed=$runnerCases;lateMonitorLatch='PASS';pinnedInputTests=if($runnerCases){'PASS'}else{'NOTRUN_PS7_ONLY'};protectedFilesModified=$false;serviceControlPerformed=$false;psVersion=$PSVersionTable.PSVersion.ToString()}|ConvertTo-Json
