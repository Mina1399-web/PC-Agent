function Get-CompleteFailureDiagnostic {
    param([Management.Automation.ErrorRecord]$Failure,
        [ValidateSet('precondition','scenario','cleanup','helper_cleanup')][string]$Stage)
    # No exception messages, target objects, stack traces, arguments or payloads.
    # Only fixed platform identifiers and known harness source filenames leave
    # the error record. Unknown identifiers are deliberately redacted.
    $type=$Failure.Exception.GetType().FullName
    if($type -notin @('System.Management.Automation.RuntimeException',
        'System.Management.Automation.CommandNotFoundException',
        'System.Management.Automation.MethodInvocationException',
        'System.Management.Automation.ParameterBindingException',
        'System.Management.Automation.PropertyNotFoundException',
        'System.InvalidOperationException','System.TimeoutException',
        'System.UnauthorizedAccessException','System.IO.IOException','System.ComponentModel.Win32Exception',
        'Microsoft.Management.Infrastructure.CimException')){$type='OtherException'}
    $errorId=[string]$Failure.FullyQualifiedErrorId
    $fixedFailureCode=[string]$Failure.Exception.Message
    if($Failure.Exception.Data['CompleteFailureBoundary'] -ceq 'protected_helper_query'){$errorId='protected_helper_query_failed'}
    elseif($fixedFailureCode -cin @('scenario_success_not_proved','three_planned_targets_required_for_cleanup','previous_helper_artifacts_changed',
        'safety_abort_latched','helper_setup_deadline','helper_target_output_contract','protected_helper_state_or_pid_changed','protected_helper_state_or_hash_changed',
        'isolated_target_process_mismatch','isolated_target_process_missing','isolated_target_process_image_mismatch',
        'isolated_target_process_command_mismatch','isolated_target_process_session_mismatch')){$errorId=$fixedFailureCode}
    elseif($type -in @('System.Management.Automation.RuntimeException','System.InvalidOperationException')){$errorId='runtime_exception'}
    elseif($errorId -notin @('CommandNotFoundException','PropertyNotFoundStrict',
        'ParameterBindingException','RuntimeException')){$errorId='unclassified'}
    $source=[IO.Path]::GetFileName([string]$Failure.InvocationInfo.ScriptName)
    if($source -notin @('run-complete-dev-resilience.ps1','CompleteResilience.psm1',
        'CompleteHelperTargets.psm1','CompleteCampaignPlan.psm1','CompletePriorHelperCleanup.psm1',
        'run-complete-scm-auto.ps1','Invoke-IsolatedScmAutoCrash.ps1',
        'IsolatedScmSafety.psm1','IsolatedScmCrashSafety.psm1')){$source='external_or_unknown'}
    $nativeCode=$null;$nativeStage=$null
    $inner=$Failure.Exception
    for($depth=0;$inner -and $depth -lt 8;$depth++) {
        if($inner -is [ComponentModel.Win32Exception]) {
            $nativeCode=$inner.NativeErrorCode
            if($inner.Data['IsolatedScmNativeStage'] -in @('open_scm','open_service',
                'failure_policy_write','failure_policy_query','failure_flag_write','disable_start',
                'protected_helper_open_scm','protected_helper_open_service','protected_helper_query_status',
                'protected_service_open_scm','protected_service_open_service','protected_service_query_status')) {
                $nativeStage=[string]$inner.Data['IsolatedScmNativeStage']
                if($nativeStage -like 'protected_helper_*'){$errorId='protected_helper_status_failed'}
            }
            break
        }
        $inner=$inner.InnerException
    }
    return [pscustomobject]@{stage=$Stage;exceptionType=$type;errorId=$errorId;
        source=$source;line=[int]$Failure.InvocationInfo.ScriptLineNumber;
        nativeStage=$nativeStage;nativeErrorCode=$nativeCode}
}
Export-ModuleMember -Function Get-CompleteFailureDiagnostic
