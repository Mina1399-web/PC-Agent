$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/chaos/run-complete-dev-resilience.ps1'),[ref]$tokens,[ref]$errors)
if(@($errors).Count){throw 'runner_parse_failed'}
if(@($ast.ParamBlock.Parameters|Where-Object {$_.Name.VariablePath.UserPath-eq'FocusS3'}).Count-ne1){throw 's3_only_focus_not_supported'}
$filter=@($ast.EndBlock.Statements|Where-Object {$_.Extent.Text.StartsWith('if($FocusS3){$plan=')})
if($filter.Count-ne1){throw 's3_only_filter_missing'}
foreach($enabled in @($false,$true)){
 $FocusS3=$enabled
 $plan=@('S1-automatic-scm-recovery','S2-collector-failure-recovery','S2-real-helper-remediation','S3-real-WPF-private-IPC','S3-thirty-minute-fault-soak','S4-hard-floor-safe-equivalent','S5-manual-VM-reboot-power'|ForEach-Object {@{id=$_}})
 . ([scriptblock]::Create($filter[0].Extent.Text))
 if($enabled-and(($plan.id-join',')-cne'S3-real-WPF-private-IPC,S3-thirty-minute-fault-soak')){throw 's3_filter_repeats_closed_or_manual_gate'}
 if(-not$enabled-and$plan.Count-ne7){throw 'default_plan_changed'}
}
$proofFunction=$ast.Find({param($node)$node-is[Management.Automation.Language.FunctionDefinitionAst]-and$node.Name-eq'Get-CompleteS2GateProof'},$true)
if(-not$proofFunction){throw 's2_evidence_gate_missing'}
. ([scriptblock]::Create($proofFunction.Extent.Text))
$pathFunction=$ast.Find({param($node)$node-is[Management.Automation.Language.FunctionDefinitionAst]-and$node.Name-eq'Assert-CampaignPlainPath'},$true)
. ([scriptblock]::Create($pathFunction.Extent.Text))
$script:campaignCandidateHash='76B09BD10A479B55EC98F4073A4F76E10CC753309FD6F36F85594374CB0F2737'
$proof=@(Get-CompleteS2GateProof)
if($proof.Count-ne2-or@($proof|Where-Object {$_.status-ne'PASS'}).Count){throw 'fresh_s2_evidence_not_proved'}
$script:campaignCandidateHash='A'*64
$rejected=$false;try{Get-CompleteS2GateProof|Out-Null}catch{$rejected=$true}
if(-not$rejected){throw 'different_candidate_s2_evidence_accepted'}
$script:campaignCandidateHash='76B09BD10A479B55EC98F4073A4F76E10CC753309FD6F36F85594374CB0F2737'
function Get-FileHash {param($LiteralPath,$Algorithm) [pscustomobject]@{Hash=('B'*64)}}
$rejected=$false;try{Get-CompleteS2GateProof|Out-Null}catch{$rejected=$true}
if(-not$rejected){throw 'modified_s2_evidence_accepted'}
'complete_s3_focus_passed:5;service_operations=0;prior_evidence=read_only'
