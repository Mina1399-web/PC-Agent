$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$runner=Join-Path $repo 'scripts\chaos\run-manual-reboot-persistence.ps1'
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($runner,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'runner_parse_error'}
$main=@($ast.EndBlock.Statements|Where-Object {$_ -is [Management.Automation.Language.TryStatementAst]})
if($main.Count-ne1){throw 'runner_single_failure_boundary_required'}
function Definition([string]$Name){$node=@($ast.FindAll({param($n)$n-is[Management.Automation.Language.FunctionDefinitionAst]-and$n.Name-ceq$Name},$false));if($node.Count-ne1){throw 'runner_function_not_found'};return $node[0].Extent.Text}
$Stage='Verify';$Execute=$true;$taskId='a7e26ca9929b48b5b36d1b2556c41bda';$taskMonitor=$null;$taskOwnCreated=$false;$taskCreateAttempted=$false
$taskEvidenceDirectory=Join-Path $env:TEMP ('HostAgent-nonexistent-'+[guid]::NewGuid().ToString('N'));$taskPackageRoot='not_used'
$script:stops=0
function Stop-RebootFixture{$script:stops++}
function Assert-RebootEnvironment{$script:taskOwnCreated=$true;throw 'injected_post_preflight_failure'}
$taskRunResult=[ordered]@{outcome='NOT RUN';failureCode=$null;cleanup='not_needed'}
. ([scriptblock]::Create($main[0].Extent.Text))|Out-Null
if($stops-ne1-or$taskRunResult.outcome-cne'FAIL'-or$taskRunResult.cleanup-cne'GUID_service_Stopped_Disabled_all_evidence_preserved'){throw 'owned_verify_precheck_failure_did_not_cleanup'}
$checks=1
$script:stops=0;$taskOwnCreated=$false
function Assert-RebootEnvironment{throw 'untrusted_identity'}
$taskRunResult=[ordered]@{outcome='NOT RUN';failureCode=$null;cleanup='not_needed'}
. ([scriptblock]::Create($main[0].Extent.Text))|Out-Null
if($stops-ne0-or$taskRunResult.outcome-cne'FAIL'){throw 'untrusted_precheck_controlled_service'};$checks++
$script:stops=0;$taskOwnCreated=$true;$Stage='Prepare'
$taskRunResult=[ordered]@{outcome='PREPARED_MANUAL_RESTART_REQUIRED';failureCode=$null;cleanup='retained'}
$taskMonitor=[pscustomobject]@{FailureCode='soft_free_memory_floor_768_mib'}
$taskMonitor|Add-Member ScriptMethod Dispose {}
$finally=($main[0].Finally.Statements|ForEach-Object {$_.Extent.Text})-join"`n"
. ([scriptblock]::Create($finally))|Out-Null
if($stops-ne1-or$taskRunResult.outcome-cne'FAIL'-or$taskRunResult.cleanup-cne'GUID_service_Stopped_Disabled_all_evidence_preserved'){throw 'late_monitor_failure_did_not_cleanup'};$checks++
Import-Module (Join-Path $repo 'scripts\chaos\RebootPersistence.psm1') -Force
. ([scriptblock]::Create((Definition 'Assert-RebootEnvironment')))
$Stage='Verify';$script:stops=0;$taskOwnCreated=$false;$taskMonitor=$null
$taskServiceExe='C:\ProgramData\HostAgent-DEV-SCM-'+$taskId+'\app\HostAgent.PipeProbe.exe'
$taskExpectedManifestSha256='candidate';$taskCatalogHash='catalog'
$temp=Join-Path $env:TEMP ('HostAgent-reboot-boundary-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($temp)
$taskPlanPath=Join-Path $temp 'prepared.json'
[IO.File]::WriteAllText($taskPlanPath,([ordered]@{runId=$taskId;candidateManifestSha256='candidate';catalogSha256='catalog';runnerSha256='fixture-runner'}|ConvertTo-Json))
function Get-FileHash([string]$LiteralPath,[string]$Algorithm){[pscustomobject]@{Hash='fixture-runner'}}
function Get-CimInstance([string]$ClassName,[string]$Filter,[int]$OperationTimeoutSec){
 if($ClassName-ceq'Win32_ComputerSystem'){return [pscustomobject]@{Model='VirtualBox'}}
 if($ClassName-ceq'Win32_ComputerSystemProduct'){return [pscustomobject]@{UUID='0bb543d1-7bb0-43ed-9c94-596d63ef3310'}}
 return [pscustomobject]@{Name='HostAgentDevScm_'+$taskId;StartName='NT SERVICE\HostAgentDevScm_'+$taskId;StartMode='Auto';PathName='"'+$taskServiceExe+'" isolated-scm-service '+$taskId+' '+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value+' reboot-proof'}
}
function Get-Service{[pscustomobject]@{Status='Running'}}
function Get-TaskFreeMiB{return 900}
function Get-TaskCFreeBytes{return 21GB}
$taskRunResult=[ordered]@{outcome='NOT RUN';failureCode=$null;cleanup='not_needed'}
try{
 . ([scriptblock]::Create($main[0].Extent.Text))|Out-Null
 if($stops-ne1-or$taskRunResult.failureCode-cne'preflight_free_memory_below_1024_mib'){throw ('real_low_memory_precheck_stranded_owned_service:'+ $stops+':'+$taskRunResult.failureCode+':'+$taskRunResult.failureLine)};$checks++
}finally{[IO.File]::Delete($taskPlanPath);[IO.Directory]::Delete($temp)}
. ([scriptblock]::Create((Definition 'Stop-RebootFixture')))
$taskServiceName='HostAgentDevScm_'+$taskId;$taskExpectedImage='expected'
function Get-CimInstance{[pscustomobject]@{Name='HostAgent';StartName='NT SERVICE\HostAgent';StartMode='Auto';PathName='expected'}}
$seen=$null;try{Stop-RebootFixture}catch{$seen=$_.Exception.Message}
if($seen-cne'reboot_service_identity_mismatch'){throw 'cleanup_did_not_reject_protected_registration'};$checks++
Import-Module (Join-Path $repo 'scripts\chaos\IsolatedScmSafety.psm1') -Force
. ([scriptblock]::Create((Definition 'Invoke-RebootChild')))
function Assert-RebootSafety{}
$python='C:\Users\vmdev\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
$clock=[Diagnostics.Stopwatch]::StartNew();$seen=$null
try{Invoke-RebootChild $python @('-I','-B','-') 1 "import time`ntime.sleep(30)`n"|Out-Null}catch{$seen=$_.Exception.Message}
if($seen-cne'reboot_child_deadline'-or$clock.Elapsed.TotalSeconds-gt8){throw 'bounded_child_timeout_failed'};$checks++
$taskExpectedImage='"C:\ProgramData\HostAgent-DEV-SCM-'+$taskId+'\app\HostAgent.PipeProbe.exe" isolated-scm-service '+$taskId+' S-1-5-21-123 reboot-proof'
$native=@(Get-IsolatedScmCreateArguments $taskServiceName $taskExpectedImage ('HostAgent DEV reboot '+$taskId))
$echo=Invoke-RebootChild $python (@('-I','-B','-')+$native) 8 "import json,sys`nprint(json.dumps(sys.argv[1:]))`n"
$decoded=$echo|ConvertFrom-Json
if(@($decoded).Count-ne12-or$decoded[1]-cne$taskServiceName-or$decoded[3]-cne$taskExpectedImage-or$decoded[7]-cne('NT SERVICE\'+$taskServiceName)){throw 'native_scm_argv_roundtrip_failed'};$checks++
. ([scriptblock]::Create((Definition 'Wait-RebootDatabase')))
function Get-RebootService{[pscustomobject]@{State='Running'}}
$script:reads=0
function Invoke-RebootProbe([string]$Mode,[int]$DeadlineSeconds){$script:reads++;if($reads-eq1){$taskRunResult.childExitCode=1;throw 'reboot_child_failed'};return [pscustomobject]@{snapshotCount=6}}
$ready=Wait-RebootDatabase 1 3
if($reads-ne2-or$ready.snapshotCount-ne6){throw 'initial_schema_readiness_was_not_waited_for'};$checks++
$script:reads=0
function Invoke-RebootProbe([string]$Mode,[int]$DeadlineSeconds){$script:reads++;$taskRunResult.childExitCode=2;throw 'reboot_child_failed'}
$seen=$null;try{Wait-RebootDatabase 1 3}catch{$seen=$_.Exception.Message}
if($seen-cne'reboot_child_failed'-or$reads-ne1){throw 'nontransient_probe_error_was_retried'};$checks++
Write-Output ('PASS: '+$checks+' real runner failure/cleanup/native timeout boundaries; no SCM or reboot operations.')
