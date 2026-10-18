$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$main=Join-Path $repo 'scripts/chaos/run-complete-dev-resilience.ps1'
$module=Join-Path $repo 'scripts/chaos/CompleteResilience.psm1'
$tests=0
function Assert-CompleteTest([bool]$Condition,[string]$Code) {
    if(-not $Condition){throw ('complete_campaign_test_failed:'+ $Code)}
}
function Read-CompleteAst([string]$Path) {
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($Path,[ref]$tokens,[ref]$errors)
    Assert-CompleteTest ($errors.Count -eq 0) 'source_parse'
    return $ast
}
function Find-CompleteEntry($Map,[string]$Name) {
    foreach($pair in $Map.KeyValuePairs){
        if($pair.Item1 -is [Management.Automation.Language.StringConstantExpressionAst] -and $pair.Item1.Value -eq $Name){return $pair.Item2}
    }
    return $null
}
function Read-CompleteConstant($Map,[string]$Name) {
    $value=Find-CompleteEntry $Map $Name
    Assert-CompleteTest ($null -ne $value) ('missing_constant_'+$Name)
    # Hashtable values are PipelineAst in Windows PowerShell 5.1 and may be
    # wrapped in a StatementBlockAst by other supported parser versions.
    $pipeline=if($value -is [Management.Automation.Language.PipelineAst]){$value}else{
        Assert-CompleteTest ($value -is [Management.Automation.Language.StatementBlockAst] -and $value.Statements.Count -eq 1) 'single_constant_statement'
        $value.Statements[0]
    }
    Assert-CompleteTest ($pipeline -is [Management.Automation.Language.PipelineAst] -and $pipeline.PipelineElements.Count -eq 1) 'constant_pipeline'
    $expression=$pipeline.PipelineElements[0]
    Assert-CompleteTest ($expression -is [Management.Automation.Language.CommandExpressionAst] -and
        $expression.Expression -is [Management.Automation.Language.StringConstantExpressionAst]) 'constant_string'
    return $expression.Expression.Value
}

# Parse only: never dot-source the main script or execute its plan.
$ast=Read-CompleteAst $main
$moduleAst=Read-CompleteAst $module
$tests++
$planAssignment=$ast.Find({param($node)
    $node -is [Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
        $node.Left.VariablePath.UserPath -eq 'plan'
},$true)
Assert-CompleteTest ($null -ne $planAssignment) 'plan_assignment_present'
# Limit discovery to the actual plan, excluding its read-only projection.
$phases=@($planAssignment.Right.FindAll({param($node)
    $node -is [Management.Automation.Language.HashtableAst] -and $null -ne (Find-CompleteEntry $node 'id') -and
        $null -ne (Find-CompleteEntry $node 'kind')
},$true))
$automatic=@($phases|Where-Object {(Read-CompleteConstant $_ 'kind') -eq 'Auto'})
$manual=@($phases|Where-Object {(Read-CompleteConstant $_ 'kind') -eq 'Manual'})
Assert-CompleteTest ($phases.Count -eq 10 -and $automatic.Count -eq 9 -and $manual.Count -eq 1) 'nine_auto_one_manual'
Assert-CompleteTest ((Read-CompleteConstant $manual[0] 'id') -eq 'S5-manual-VM-reboot-power') 'only_manual_reboot'
$expected=@('S1-automatic-scm-recovery','S2-storage-unavailable','S2-storage-full','S2-storage-corrupt',
    'S2-collector-failure-recovery','S2-real-helper-remediation','S3-real-WPF-private-IPC','S3-thirty-minute-fault-soak','S4-hard-floor-safe-equivalent')
Assert-CompleteTest ((@($automatic|ForEach-Object{Read-CompleteConstant $_ 'id'}) -join '|') -ceq ($expected -join '|')) 'exact_auto_gate_inventory'
$tests++
Assert-CompleteTest ($null -eq (Find-CompleteEntry $manual[0] 'run')) 'manual_has_no_run_callback'
foreach($phase in $automatic){Assert-CompleteTest ($null -ne (Find-CompleteEntry $phase 'run')) 'auto_has_run_callback'}
$tests++

$forbidden=@('Restart-Computer','Stop-Computer','shutdown','shutdown.exe','Stop-Service','Start-Service','Restart-Service',
    'Set-Service','Remove-Service','New-Service','Remove-Item','Clear-Content','Set-ExecutionPolicy','Invoke-Expression','iex',
    'Set-MpPreference','bcdedit','bcdedit.exe','powercfg','powercfg.exe','taskkill','taskkill.exe')
foreach($sourceAst in @($ast,$moduleAst)){
    foreach($command in $sourceAst.FindAll({param($node)$node -is [Management.Automation.Language.CommandAst]},$true)){
        Assert-CompleteTest ($command.GetCommandName() -notin $forbidden) 'no_guest_power_or_generic_mutating_command'
    }
}
$tests++

# The one direct controller Stop remains in a function that first proves the
# exact GUID service path/account/hash; all helper actions use the scoped API.
$quiescence=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Confirm-CompleteQuiescent'},$true)
Assert-CompleteTest ($null -ne $quiescence) 'quiescence_function'
$text=$quiescence.Extent.Text
foreach($guard in @('HostAgentDevScm_','PathName -cne $expected','StartName -cne','isolated_cleanup_identity_mismatch',
    '[IsolatedScmRecovery]::Set($autoId,$false)','protected_changed_before_cleanup','isolated_quiescence_unconfirmed')){
    Assert-CompleteTest ($text.Contains($guard)) 'scm_cleanup_identity_guards'
}
Assert-CompleteTest ($ast.Extent.Text.Contains('New-CompleteHelperTargets') -and $ast.Extent.Text.Contains('Confirm-CompleteHelperTargets')) 'scoped_helper_api'
$tests++

$fast=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-CompleteFast'},$true)
Assert-CompleteTest ($null -ne $fast) 'sticky_guard_present'
Import-Module $module -Force
# Mock only the native physical-memory observation. Keep the actual decision,
# exception handling and sticky-latch implementation from the main source.
$nativeRead='[BoundedScmPressure]::FreeMiB()'
Assert-CompleteTest ($fast.Extent.Text.Contains($nativeRead)) 'native_memory_read_present'
. ([scriptblock]::Create($fast.Extent.Text.Replace($nativeRead,'$script:completeMockFreeMiB')))
$script:completeSafetyLatched=$false;$script:completeFootprint=$null;$script:completeMonitor=$null;$script:mockGuardCalls=0;$script:completeMockFreeMiB=767.9
function Assert-CampaignFastSafety {
    $script:mockGuardCalls++
}
$firstThrew=$false;$secondThrew=$false
try{Assert-CompleteFast}catch{$firstThrew=$true}
$script:completeMockFreeMiB=2048
try{Assert-CompleteFast}catch{$secondThrew=$true}
Assert-CompleteTest ($firstThrew -and $secondThrew -and $script:completeSafetyLatched -and $script:mockGuardCalls -eq 0) 'transient_guard_is_sticky'
$tests++

 $childGuard=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-CompleteChildSafety'},$true)
Assert-CompleteTest ($null -ne $childGuard) 'child_guard_present'
. ([scriptblock]::Create($childGuard.Extent.Text))
foreach($case in @(
    @{resourceAbort=$true},@{resourceGuardFailed=$true},@{aborted=$true},
    @{resourceFailureCode='hard_free_memory_floor_512_mib'},@{errorCode='soft_free_memory_floor_768_mib'},
    @{errorCode='disk_free_below_20_gib'},@{minimumFreeMiB=767.9}
)){
    $data=@{resourceAbort=$false;resourceGuardFailed=$false;aborted=$false;resourceFailureCode=$null;errorCode=$null;minimumFreeMiB=2048}
    foreach($key in $case.Keys){$data[$key]=$case[$key]}
    $script:completeSafetyLatched=$false;$threw=$false
    try{Assert-CompleteChildSafety ([pscustomobject]$data)}catch{$threw=$true}
    Assert-CompleteTest ($threw -and $script:completeSafetyLatched) 'child_abort_evidence_is_sticky'
}
$script:completeSafetyLatched=$false
Assert-CompleteChildSafety ([pscustomobject]@{resourceAbort=$false;resourceGuardFailed=$false;aborted=$false;resourceFailureCode=$null;errorCode='sqlite_13';minimumFreeMiB=2048})
Assert-CompleteTest (-not $script:completeSafetyLatched) 'ordinary_sqlite_failure_is_not_resource_abort'
$tests++

Import-Module (Join-Path $repo 'scripts/chaos/RemainingResilience.psm1') -Force
$script:laterCalls=0
$plan=@(@{id='ordinary_failure';kind='Auto';run={@{ok=$false}}},@{id='independent';kind='Auto';run={$script:laterCalls++;@{ok=$true}}})
$rows=@(Invoke-RemainingResiliencePlan $plan 'pure-fixture' {} { @{safe=$true} } {})
Assert-CompleteTest (($rows.status -join ',') -eq 'FAIL,PASS' -and $script:laterCalls -eq 1) 'ordinary_failure_continues'
$tests++
$script:laterCalls=0
$rows=@(Invoke-RemainingResiliencePlan $plan 'pure-fixture' {} {throw 'owned_child_quiescence_unconfirmed'} {})
Assert-CompleteTest (($rows.status -join ',') -eq 'FAIL,NOT RUN' -and $script:laterCalls -eq 0) 'quiescence_failure_aborts_successors'
$tests++

Import-Module $module -Force
foreach($case in @(@(512,'hard_abort'),@(511.9,'hard_abort'),@(767.9,'soft_abort'),@(768,'continue'),@([double]::NaN,'hard_abort'),@([double]::PositiveInfinity,'hard_abort'))){
    Assert-CompleteTest ((Get-CompleteMemoryDecision $case[0]) -eq $case[1]) 'exact_memory_boundaries'
}
$tests++
[pscustomobject]@{suite='complete-campaign-pure-safety';passed=$tests;failed=0;mainExecuted=$false;serviceOperations=0;physicalPressure=$false}|ConvertTo-Json -Compress
