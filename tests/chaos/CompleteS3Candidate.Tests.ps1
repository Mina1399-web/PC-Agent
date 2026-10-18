$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/chaos/run-complete-dev-resilience.ps1'),[ref]$tokens,[ref]$errors)
if(@($errors).Count){throw 'runner_parse_failed'}
foreach($name in @('Assert-CampaignPlainPath','Assert-CampaignCandidate','Get-CompleteProbeCandidate')){
 $fn=$ast.Find({param($n)$n-is[Management.Automation.Language.FunctionDefinitionAst]-and$n.Name-eq$name},$true)
 if(-not$fn){throw ('candidate_boundary_missing_'+$name)}
 . ([scriptblock]::Create($fn.Extent.Text))
}
$script:campaignCandidate=[IO.Path]::GetFullPath((Join-Path $repo '..\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261017-complete-r6'))
$script:campaignCandidateHash='76B09BD10A479B55EC98F4073A4F76E10CC753309FD6F36F85594374CB0F2737'
$script:campaignS3Candidate=[IO.Path]::GetFullPath((Join-Path $repo '..\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261018-s3-r7'))
$script:campaignS3CandidateHash='5517CA5B758076E44E080B60059B4E43FF824FFD98075756F3AF7E11A6089E20'
$FocusS3=$false;$old=Get-CompleteProbeCandidate
if($old.path-cne$script:campaignCandidate-or$old.manifestSha256-cne$script:campaignCandidateHash){throw 'legacy_candidate_selection_changed'}
$FocusS3=$true;$selected=Get-CompleteProbeCandidate
if($selected.path-cne$script:campaignS3Candidate-or$selected.manifestSha256-cne$script:campaignS3CandidateHash){throw 's3_uses_stale_mixed_candidate'}
Assert-CampaignCandidate $selected.path $selected.manifestSha256
$rejected=$false;try{Assert-CampaignCandidate $selected.path ('A'*64)}catch{$rejected=$true}
if(-not$rejected){throw 'untrusted_s3_manifest_accepted'}
$wrapper=$ast.Find({param($n)$n-is[Management.Automation.Language.FunctionDefinitionAst]-and$n.Name-eq'Invoke-CompleteProbe'},$true)
. ([scriptblock]::Create($wrapper.Extent.Text))
$evidenceRoot=$env:TEMP;$logRoot=$env:TEMP;$script:observedFile=$null
function Test-Path {param($LiteralPath) $false}
function Invoke-CompleteBoundedProcess {param($File,$Arguments,$TimeoutSeconds,$LogDirectory,$Guard) $script:observedFile=$File;[pscustomobject]@{exitCode=0;stdoutPath='synthetic_result'}}
function Get-Content {param($LiteralPath) '{"ok":true}'}
function Assert-CompleteChildSafety {}
$result=Invoke-CompleteProbe @('isolated-complete-wpf') 'S3-real-WPF-private-IPC' 195 'complete-wpf'
if($script:observedFile-cne(Join-Path $selected.path 'HostAgent.PipeProbe.exe')-or$result.executedCandidate.manifestSha256-cne$selected.manifestSha256){throw 'actual_child_or_evidence_uses_wrong_candidate'}
'complete_s3_candidate_passed:5;service_operations=0;candidate_writes=0'
