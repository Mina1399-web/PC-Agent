$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/chaos/run-complete-dev-resilience.ps1'),[ref]$tokens,[ref]$errors)
$fast=$ast.Find({param($node)$node-is[Management.Automation.Language.FunctionDefinitionAst]-and$node.Name-eq'Assert-CampaignFastSafety'},$true)
# Only the native resource/status boundary is replaced. WMI deliberately fails
# as it did in campaign 02d720ab. All actual guard decisions remain intact.
$text=$fast.Extent.Text.Replace('[BoundedScmPressure]::FreeMiB()','2048').Replace('[CompleteProtectedServiceGuard]::Read()','(Get-TestNativeStatus)')
$text=$text.Replace('[CompleteProtectedServiceGuard]::ReadHelper()','(Get-TestHelperStatus)')
$text=$text.Replace("if('BoundedScmPressure' -as [type])",'if($true)')
. ([scriptblock]::Create($text))
function Get-CimInstance {throw 'wmi_provider_unavailable'}
function Get-TestNativeStatus {if($script:testStatusFailure){throw 'native_status_unavailable'}; $script:testStatus}
$script:campaignBaseline=@{pid=1234}
$script:testStatusFailure=$false
foreach($case in @(@{state='Running';pid=1234;reject=$false},@{state='Stopped';pid=0;reject=$true},@{state='Running';pid=9999;reject=$true})) {
    $script:campaignLastIdentityCheck=$null
    $script:testStatus=[pscustomobject]@{State=$case.state;ProcessId=$case.pid}
    $caught=$false
    try{Assert-CampaignFastSafety}catch{$caught=$true;if(-not $case.reject){throw}}
    if($caught -ne $case.reject){throw 'protected_native_identity_not_enforced'}
}
$script:testStatusFailure=$true;$script:campaignLastIdentityCheck=$null
$caught=$false;try{Assert-CampaignFastSafety}catch{$caught=$true}
if(-not$caught){throw 'native_status_failure_must_fail_closed'}
'complete_fast_service_guard_passed:4;service_operations=0'
$FocusS2Failures=$true
$script:campaignHelperBaseline=@{pid=3328;sha256=('A'*64)}
$script:testStatusFailure=$false
$script:testStatus=[pscustomobject]@{State='Running';ProcessId=1234}
function Get-CampaignProtectedHelper {throw 'wmi_provider_unavailable'}
function Get-TestHelperStatus {if($script:testHelperUnavailable){throw 'native_helper_status_unavailable'}; $script:testHelperStatus}
$script:testHelperUnavailable=$false
foreach($case in @(@{state='Running';pid=3328;reject=$false},@{state='Stopped';pid=0;reject=$true},@{state='Running';pid=9999;reject=$true})){
    $script:campaignLastIdentityCheck=$null
    $script:testHelperStatus=[pscustomobject]@{State=$case.state;ProcessId=$case.pid}
    $caught=$false
    try{Assert-CampaignFastSafety}catch{$caught=$true;if(-not$case.reject){throw}}
    if($caught-ne$case.reject){throw 'helper_native_identity_not_enforced'}
}
$script:testHelperUnavailable=$true
$caught=$false;try{Assert-CampaignFastSafety}catch{$caught=$true}
if(-not$caught){throw 'helper_native_failure_must_fail_closed'}
'complete_fast_helper_guard_passed:4;service_operations=0'
$snapshot=$ast.Find({param($node)$node-is[Management.Automation.Language.FunctionDefinitionAst]-and$node.Name-eq'Assert-CampaignHelperSnapshot'},$true)
. ([scriptblock]::Create($snapshot.Extent.Text))
function Get-CampaignProtectedHelper {$script:testHelperSnapshot}
foreach($case in @(@{pid=3328;sha256=('A'*64);reject=$false},@{pid=3328;sha256=('B'*64);reject=$true},@{pid=9999;sha256=('A'*64);reject=$true})){
 $script:testHelperSnapshot=$case
 $caught=$false;try{Assert-CampaignHelperSnapshot}catch{$caught=$true}
 if($caught-ne$case.reject){throw 'phase_helper_hash_and_pid_not_enforced'}
}
'complete_phase_helper_snapshot_passed:3;service_operations=0'
$cleanupFunction=$ast.Find({param($node)$node-is[Management.Automation.Language.FunctionDefinitionAst]-and$node.Name-eq'Assert-CampaignCleanupIdentity'},$true)
if($cleanupFunction){
 $nativeCleanup=$cleanupFunction.Extent.Text.Replace('[CompleteProtectedServiceGuard]::Read()','(Get-TestNativeStatus)').Replace('[CompleteProtectedServiceGuard]::ReadHelper()','(Get-TestHelperStatus)')
 . ([scriptblock]::Create($nativeCleanup))
}
$wrapper=$ast.Find({param($node)$node-is[Management.Automation.Language.FunctionDefinitionAst]-and$node.Name-eq'Invoke-CompleteHelper'},$true)
$cleanupCommand=$wrapper.Find({param($node)$node-is[Management.Automation.Language.CommandAst]-and$node.GetCommandName()-eq'Confirm-CompleteHelperTargets'},$true)
$cleanupGuardAst=@($cleanupCommand.CommandElements|Where-Object {$_-is[Management.Automation.Language.ScriptBlockExpressionAst]})[0]
$cleanupGuard=[scriptblock]::Create($cleanupGuardAst.ScriptBlock.EndBlock.Extent.Text)
function Assert-CampaignVm {throw 'wmi_provider_unavailable'}
function Get-CampaignProtected {throw 'wmi_provider_unavailable'}
$script:testHelperUnavailable=$false
foreach($case in @('stable','protected_stopped','protected_pid_changed','protected_unavailable','helper_stopped','helper_pid_changed','helper_unavailable')){
 $script:testStatusFailure=$case-eq'protected_unavailable'
 $script:testHelperUnavailable=$case-eq'helper_unavailable'
 $script:testStatus=[pscustomobject]@{State='Running';ProcessId=1234}
 $script:testHelperStatus=[pscustomobject]@{State='Running';ProcessId=3328}
 if($case-eq'protected_stopped'){$script:testStatus.State='Stopped'}
 if($case-eq'protected_pid_changed'){$script:testStatus.ProcessId=9999}
 if($case-eq'helper_stopped'){$script:testHelperStatus.State='Stopped'}
 if($case-eq'helper_pid_changed'){$script:testHelperStatus.ProcessId=9999}
 $caught=$false;try{& $cleanupGuard}catch{$caught=$true;if($case-eq'stable'){throw}}
 if($caught-ne($case-ne'stable')){throw ('cleanup_identity_not_enforced:'+ $case)}
}
'complete_cleanup_native_guard_passed:7;service_operations=0'
$FocusS2Failures=$false;$FocusS3=$true
$script:testStatusFailure=$false
$script:testStatus=[pscustomobject]@{State='Running';ProcessId=1234}
foreach($case in @('stable','stopped','pid_changed','unavailable')){
 $script:testHelperUnavailable=$case-eq'unavailable'
 $script:testHelperStatus=[pscustomobject]@{State='Running';ProcessId=3328}
 if($case-eq'stopped'){$script:testHelperStatus.State='Stopped'}
 if($case-eq'pid_changed'){$script:testHelperStatus.ProcessId=9999}
 $caught=$false;try{Assert-CampaignFastSafety}catch{$caught=$true;if($case-eq'stable'){throw}}
 if($caught-ne($case-ne'stable')){throw 's3_helper_identity_guard_not_preserved'}
}
'complete_s3_helper_native_guard_passed:4;service_operations=0'
