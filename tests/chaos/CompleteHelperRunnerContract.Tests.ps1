$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'scripts/chaos/CompleteHelperTargets.psm1') -Force
Import-Module (Join-Path $repo 'scripts/chaos/CompleteDiagnostics.psm1') -Force
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/chaos/run-complete-dev-resilience.ps1'),[ref]$tokens,[ref]$errors)
$wrapper=$ast.Find({param($node)$node-is[Management.Automation.Language.FunctionDefinitionAst]-and$node.Name-eq'Invoke-CompleteHelper'},$true)
. ([scriptblock]::Create($wrapper.Extent.Text))
$logRoot=Join-Path $env:LOCALAPPDATA ('HostAgent-DEV-evidence\runner-contract-regression-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $logRoot 'S2-real-helper-remediation')|Out-Null
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$helperIds=@('11111111111111111111111111111111','22222222222222222222222222222222','33333333333333333333333333333333')
$script:campaignCandidate='C:\candidate-fixture';$script:campaignCandidateHash='A'*64
$script:testOwned=@(Get-CompleteHelperTargetPlan @{Path=$script:campaignCandidate;ManifestSha256=$script:campaignCandidateHash} $helperIds $identity.User.Value)
foreach($target in $script:testOwned){$target.RootCreated=$true;$target.CreationAttempted=$true;$target.Created=$true;$target.CopyCompleted=$true}
$stdout=Join-Path $logRoot 'stub-child.json';[IO.File]::WriteAllText($stdout,'{"ok":true}')
function New-CompleteHelperTargets {
 param($Candidate,$RunIds,$OperatorSid,$Guard,[ref]$TargetState)
 if($TargetState){$TargetState.Value=$script:testOwned}
 $script:testOwned
 if($script:testDirty){[pscustomobject]@{Unexpected='incidental_copy_completion'}}
}
function Assert-CompleteFast {}
function Assert-CompleteChildSafety {}
function Get-Service {param($Name,$ErrorAction) $null}
function Invoke-CompleteBoundedProcess {$script:testChildStarts++;[pscustomobject]@{exitCode=0;stdoutPath=$stdout}}
function Confirm-CompleteHelperTargets {
 param($Targets,$Guard)
 if(@($Targets).Count-ne3){throw 'test_cleanup_did_not_receive_owned_three'}
 foreach($target in $Targets){if($script:testOwned-notcontains$target){throw 'test_cleanup_received_unowned_target'}}
 $script:testCleanupCount=@($Targets).Count
 @($Targets|ForEach-Object {[pscustomobject]@{Name=$_.Name;Quiescent=$true;State='Stopped';StartMode='Disabled'}})
}
foreach($dirty in @($true,$false)){
 $script:testDirty=$dirty;$script:testChildStarts=0;$script:testCleanupCount=0;$script:completeTargets=@();$script:completeSafetyLatched=$false
 $caught=$null;try{Invoke-CompleteHelper|Out-Null}catch{$caught=$_}
 if($dirty){
  if($script:testChildStarts-ne0){throw 'contaminated_factory_output_launched_helper_child'}
  if(-not$caught-or$caught.Exception.Message-ne'helper_target_output_contract'){throw 'contaminated_factory_output_not_rejected'}
 }else{if($caught){throw $caught};if($script:testChildStarts-ne1){throw 'valid_three_targets_did_not_launch_child'}}
 if($script:testCleanupCount-ne3){throw 'owned_cleanup_targets_not_retained'}
}
'complete_helper_runner_contract_passed:2;native_service_operations=0;child_boundary=stubbed'
