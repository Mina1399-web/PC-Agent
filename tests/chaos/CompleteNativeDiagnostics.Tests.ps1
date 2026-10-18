$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'scripts/chaos/CompleteDiagnostics.psm1') -Force
$native=[ComponentModel.Win32Exception]::new(5,'must_not_log_private_payload')
$native.Data['IsolatedScmNativeStage']='failure_policy_write'
$wrapped=[InvalidOperationException]::new('also_private',$native)
$errorRecord=[Management.Automation.ErrorRecord]::new($wrapped,'untrusted_error_id',[Management.Automation.ErrorCategory]::NotSpecified,$null)
$safe=Get-CompleteFailureDiagnostic $errorRecord 'scenario'
if($safe.nativeStage -ne 'failure_policy_write' -or $safe.nativeErrorCode -ne 5){throw 'nested_native_failure_not_diagnosable'}
if(($safe|ConvertTo-Json -Compress) -match 'private|payload|untrusted_error_id'){throw 'sensitive_native_text_leaked'}
$fake=[InvalidOperationException]::new('private')
$fake.Data['IsolatedScmNativeStage']='failure_policy_write';$fake.Data['NativeErrorCode']=5
$safe=Get-CompleteFailureDiagnostic ([Management.Automation.ErrorRecord]::new($fake,'x',0,$null)) 'scenario'
if($null-ne$safe.nativeErrorCode){throw 'untrusted_exception_data_accepted_as_native_code'}
'complete_native_diagnostics_passed:3'
$tokens=$null;$errors=$null
$runnerAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/chaos/run-complete-dev-resilience.ps1'),[ref]$tokens,[ref]$errors)
$snapshot=$runnerAst.Find({param($node)$node-is[Management.Automation.Language.FunctionDefinitionAst]-and$node.Name-eq'Get-CampaignProtectedHelper'},$true)
. ([scriptblock]::Create($snapshot.Extent.Text))
function Get-CimInstance {throw 'sensitive_provider_detail'}
$failure=try{Get-CampaignProtectedHelper}catch{$_}
$safe=Get-CompleteFailureDiagnostic $failure 'scenario'
if($safe.errorId-ne'protected_helper_query_failed'){throw 'protected_helper_query_failure_unclassified'}
if(($safe|ConvertTo-Json -Compress)-match'sensitive_provider_detail'){throw 'provider_message_leaked'}
foreach($stage in @('protected_helper_open_scm','protected_helper_open_service','protected_helper_query_status')){
 $nativeFailure=[ComponentModel.Win32Exception]::new(5,'sensitive_native_detail')
 $nativeFailure.Data['IsolatedScmNativeStage']=$stage
 $safe=Get-CompleteFailureDiagnostic ([Management.Automation.ErrorRecord]::new($nativeFailure,'unknown',0,$null)) 'scenario'
 if($safe.nativeStage-cne$stage -or $safe.nativeErrorCode-ne5 -or $safe.errorId-ne'protected_helper_status_failed'){throw 'protected_helper_native_failure_unclassified'}
}
'complete_helper_diagnostics_passed:4'
