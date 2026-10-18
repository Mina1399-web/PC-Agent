$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/chaos/run-complete-dev-resilience.ps1'),[ref]$tokens,[ref]$errors)
if(@($errors).Count){throw 'runner_parse_failed'}
if(@($ast.ParamBlock.Parameters|Where-Object {$_.Name.VariablePath.UserPath-eq'FocusSoakFailure'}).Count-ne1){throw 'soak_only_focus_missing'}
$filter=@($ast.EndBlock.Statements|Where-Object {$_.Extent.Text.StartsWith('if($FocusSoakFailure){$plan=')})
if($filter.Count-ne1){throw 'soak_only_filter_missing'}
$FocusSoakFailure=$true;$FocusS3=$true
$plan=@('S3-real-WPF-private-IPC','S3-thirty-minute-fault-soak'|ForEach-Object {@{id=$_}})
. ([scriptblock]::Create($filter[0].Extent.Text))
if(($plan.id-join',')-cne'S3-thirty-minute-fault-soak'){throw 'passed_wpf_would_repeat'}
foreach($name in @('Assert-CampaignPlainPath','Get-CompleteWpfGateProof')){
 $fn=$ast.Find({param($n)$n-is[Management.Automation.Language.FunctionDefinitionAst]-and$n.Name-eq$name},$true)
 if(-not$fn){throw 'wpf_evidence_gate_missing'}
 . ([scriptblock]::Create($fn.Extent.Text))
}
$proof=Get-CompleteWpfGateProof
if($proof.status-cne'PASS'-or$proof.campaignRunId-cne'a0e02d2045724bcfa5a797d8334fc8b3'){throw 'actual_wpf_proof_rejected'}
foreach($name in @('Assert-CampaignCandidate','Get-CompleteProbeCandidate','Invoke-CompleteProbe')){
 $fn=$ast.Find({param($n)$n-is[Management.Automation.Language.FunctionDefinitionAst]-and$n.Name-eq$name},$true)
 if(-not$fn){throw 'candidate_execution_boundary_missing'}
 . ([scriptblock]::Create($fn.Extent.Text))
}
$script:campaignSoakCandidate=[IO.Path]::GetFullPath((Join-Path $repo '..\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261018-soak-r8'))
$script:campaignSoakCandidateHash='AEEA44CBEA5F46C0216A11AC0BF1A9E59B3C82EC050B826DAD38F505D716BA61'
$selected=Get-CompleteProbeCandidate
if($selected.path-cne$script:campaignSoakCandidate){throw 'soak_would_execute_old_candidate'}
Assert-CampaignCandidate $selected.path $selected.manifestSha256
$evidenceRoot=$env:TEMP;$logRoot=$env:TEMP;$script:observedExe=$null
function Test-Path {param($LiteralPath) $false}
function Invoke-CompleteBoundedProcess {param($File,$Arguments,$TimeoutSeconds,$LogDirectory,$Guard) $script:observedExe=$File;[pscustomobject]@{exitCode=0;stdoutPath='synthetic_result'}}
function Get-Content {param($LiteralPath) '{"ok":true}'}
function Assert-CompleteChildSafety {}
$result=Invoke-CompleteProbe @('isolated-complete-soak') 'S3-thirty-minute-fault-soak' 1960 'complete-soak'
if($script:observedExe-cne(Join-Path $selected.path 'HostAgent.PipeProbe.exe')-or$result.executedCandidate.manifestSha256-cne$script:campaignSoakCandidateHash){throw 'soak_child_or_result_identity_mismatch'}
Remove-Item Function:Test-Path,Function:Get-Content
function Get-FileHash {param($LiteralPath,$Algorithm) [pscustomobject]@{Hash=('A'*64)}}
$rejected=$false;try{Get-CompleteWpfGateProof|Out-Null}catch{$rejected=$true}
if(-not$rejected){throw 'changed_wpf_proof_accepted'}
'complete_soak_focus_passed:6;old_evidence_read_only=1;service_operations=0'
