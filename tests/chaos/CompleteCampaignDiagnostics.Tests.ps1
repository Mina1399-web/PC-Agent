$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$module=Join-Path $repo 'scripts\chaos\CompleteCampaignPlan.psm1'
if(-not(Test-Path -LiteralPath $module)){$module=Join-Path $repo 'scripts\chaos\RemainingResilience.psm1'}
Import-Module $module -Force
$script:savedCampaignJson=$null
function New-TestPhase([string]$Id,[scriptblock]$Run){
    @{id=$Id;kind='Auto';scope='synthetic_controller_logic_only';limits=@{};logDirectory='not_written';manualStep=$null;run=$Run}
}
function Assert-Diagnostic([bool]$Condition,[string]$Code){if(-not$Condition){throw ('campaign_diagnostic_assertion_failed:'+ $Code)}}
Import-Module (Join-Path $repo 'scripts\chaos\CompleteDiagnostics.psm1')
$autoSource=Get-Content (Join-Path $repo 'scripts\chaos\run-complete-scm-auto.ps1') -Raw
Assert-Diagnostic ($autoSource -match '\$taskRunResult\.failureDiagnostic\s*=\s*Get-CompleteFailureDiagnostic') 'scm_failure_line_diagnostic_missing'
$sensitiveError=try{throw 'candidate ACL sensitive detail'}catch{$_}
$safeDiagnostic=Get-CompleteFailureDiagnostic $sensitiveError 'scenario'
Assert-Diagnostic ($safeDiagnostic.source -eq 'external_or_unknown' -and $safeDiagnostic.line -gt 0) 'scm_failure_location_not_safe'
Assert-Diagnostic (-not($safeDiagnostic|ConvertTo-Json -Compress|Select-String 'sensitive detail')) 'scm_failure_diagnostic_leaks_message'
$knownFailure=try{throw [InvalidOperationException]::new('scenario_success_not_proved')}catch{$_}
$knownDiagnostic=Get-CompleteFailureDiagnostic $knownFailure 'scenario'
Assert-Diagnostic ($knownDiagnostic.errorId -eq 'scenario_success_not_proved') 'known_campaign_failure_was_unclassified'
Assert-Diagnostic (-not($knownDiagnostic|ConvertTo-Json -Compress|Select-String 'sensitive')) 'known_campaign_failure_diagnostic_not_redacted'
$cleanupFailure=try{throw [InvalidOperationException]::new('three_planned_targets_required_for_cleanup')}catch{$_}
$cleanupDiagnostic=Get-CompleteFailureDiagnostic $cleanupFailure 'cleanup'
Assert-Diagnostic ($cleanupDiagnostic.errorId -eq 'three_planned_targets_required_for_cleanup') 'cleanup_cardinality_failure_was_unclassified'
$unknownRuntime=try{throw [InvalidOperationException]::new('sensitive operator-controlled detail')}catch{$_}
$unknownDiagnostic=Get-CompleteFailureDiagnostic $unknownRuntime 'scenario'
Assert-Diagnostic ($unknownDiagnostic.errorId -eq 'runtime_exception') 'runtime_exception_was_unclassified'
Assert-Diagnostic (-not($unknownDiagnostic|ConvertTo-Json -Compress|Select-String 'operator-controlled detail')) 'runtime_exception_message_was_not_redacted'
function Assert-SafeFailure($Failure,[string]$Stage){
    Assert-Diagnostic ($null -ne $Failure) 'failure_stage_metadata_missing'
    Assert-Diagnostic ($Failure.stage -eq $Stage) 'failure_stage_wrong'
    Assert-Diagnostic ($Failure.exceptionType -eq 'System.Management.Automation.RuntimeException') 'exception_type_wrong'
    Assert-Diagnostic ($Failure.errorId -eq 'runtime_exception') 'untrusted_error_id_not_redacted'
    Assert-Diagnostic ($Failure.source -eq 'external_or_unknown' -and $Failure.line -gt 0) 'safe_source_location_missing'
}
# Defect: the old plan loses failure stages and can persist arbitrary exception
# text. Real phase callbacks throw; only unsafe OS/service boundaries are absent.
$phase=New-TestPhase 'first' {throw 'synthetic_sensitive_payload'}
$next=New-TestPhase 'next' {@{ok=$true}}
$save={param($rows)$script:savedCampaignJson=$rows|ConvertTo-Json -Depth 12}
$rows=@(Invoke-RemainingResiliencePlan @($phase,$next) 'test' {} {} $save)
Assert-Diagnostic ($rows[0].status -eq 'FAIL' -and $rows[1].status -eq 'PASS') 'ordinary_failure_did_not_continue'
Assert-SafeFailure $rows[0].failure 'scenario'
Assert-Diagnostic ($script:savedCampaignJson -notmatch 'synthetic_sensitive_payload') 'sensitive_exception_text_saved'
$rows=@(Invoke-RemainingResiliencePlan @($phase,$next) 'test' {} {throw 'cleanup_sensitive_payload'} $save)
Assert-Diagnostic ($rows[0].status -eq 'FAIL' -and $rows[1].status -eq 'NOT RUN') 'cleanup_failure_did_not_latch'
Assert-SafeFailure $rows[0].failure 'scenario'
Assert-SafeFailure $rows[0].cleanupFailure 'cleanup'
Assert-Diagnostic ($script:savedCampaignJson -notmatch 'sensitive_payload') 'cleanup_sensitive_text_saved'
$rows=@(Invoke-RemainingResiliencePlan @($next) 'test' {throw 'precondition_sensitive_payload'} {} $save)
Assert-Diagnostic ($rows[0].status -eq 'NOT RUN') 'unsafe_precondition_ran_phase'
Assert-SafeFailure $rows[0].failure 'precondition'
Assert-Diagnostic ($script:savedCampaignJson -notmatch 'sensitive_payload') 'precondition_sensitive_text_saved'
# Defect: a helper cleanup exception in finally must not erase its setup error.
# Execute the real wrapper, replacing only privileged target setup/quiescence.
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts\chaos\run-complete-dev-resilience.ps1'),[ref]$tokens,[ref]$errors)
$wrapper=$ast.Find({param($node)$node-is[Management.Automation.Language.FunctionDefinitionAst]-and$node.Name-eq'Invoke-CompleteHelper'},$true)
Import-Module (Join-Path $repo 'scripts\chaos\CompleteDiagnostics.psm1')
. ([scriptblock]::Create($wrapper.Extent.Text))
$logRoot=$env:TEMP;$helperIds=@('test');$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$script:campaignCandidate='not_read';$script:campaignCandidateHash='not_read';$script:completeTargets=@()
function New-CompleteHelperTargets {
    $failure=[IO.IOException]::new('setup_sensitive_payload')
    $failure.Data['Targets']=@([pscustomobject]@{RunId='not_created'})
    throw $failure
}
function Confirm-CompleteHelperTargets {throw [InvalidOperationException]::new('cleanup_sensitive_payload')}
$helper=New-TestPhase 'helper' {Invoke-CompleteHelper}
try{$rows=@(Invoke-RemainingResiliencePlan @($helper) 'test' {} {} $save)}finally{$identity.Dispose()}
Assert-Diagnostic ($rows[0].failure.exceptionType -eq 'System.IO.IOException') 'helper_cleanup_erased_setup_failure'
Assert-Diagnostic ($rows[0].cleanupFailure.exceptionType -eq 'System.InvalidOperationException') 'helper_cleanup_stage_missing'
Assert-Diagnostic ($script:savedCampaignJson -notmatch 'sensitive_payload') 'helper_sensitive_text_saved'
'complete_campaign_diagnostics_passed:4_scenarios'
