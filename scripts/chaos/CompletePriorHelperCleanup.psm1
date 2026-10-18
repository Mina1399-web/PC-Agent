Set-StrictMode -Version Latest
$script:priorHelperTargetsModule=Import-Module (Join-Path $PSScriptRoot 'CompleteHelperTargets.psm1') -PassThru
$script:priorHelperRun='901779e42bc548b1a54746172e681981'
$script:priorHelperSummaryHash='3838E9A27DDFA118BE71D6C801CDBD3D5DE39AF346FEED7CAB381D87BC5A3571'
$script:priorHelperManifest='76B09BD10A479B55EC98F4073A4F76E10CC753309FD6F36F85594374CB0F2737'
$script:priorHelperIds=@('b75a0b684dc84d9ea88190cc702828d1','f6b56e621cb140b8ae65975f385b317a','66dea5c190424838915e802850350655')

function Get-CompletePriorHelperCleanupPlan {
 param($Candidate,[string]$OperatorSid)
 $expectedCandidate=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261017-complete-r6'))
 if(-not$Candidate-or[string]$Candidate.Path-cne$expectedCandidate-or[string]$Candidate.ManifestSha256-cne$script:priorHelperManifest){throw 'prior_cleanup_candidate_identity_mismatch'}
 if([IO.Path]::GetFullPath($env:LOCALAPPDATA)-ine'C:\Users\AgentPCdevtest\AppData\Local'){throw 'prior_cleanup_evidence_root_mismatch'}
 $root=Join-Path $env:LOCALAPPDATA ('HostAgent-DEV-evidence\complete-campaign-'+$script:priorHelperRun)
 $summaryPath=Join-Path $root 'summary.json'
 $targetsPath=Join-Path $root 'S2-real-helper-remediation\targets.json'
 foreach($path in @($summaryPath,$targetsPath)){
  Assert-CompleteHelperPlainPath $path|Out-Null
  if(-not(Test-Path -LiteralPath $path -PathType Leaf)-or(Get-Item -LiteralPath $path).Length-gt1MB){throw 'prior_cleanup_evidence_missing_or_oversized'}
 }
 if((Get-FileHash -LiteralPath $summaryPath -Algorithm SHA256).Hash-cne$script:priorHelperSummaryHash){throw 'prior_cleanup_summary_hash_mismatch'}
 $summary=Get-Content -LiteralPath $summaryPath -Raw|ConvertFrom-Json
 if($summary.campaignRunId-cne$script:priorHelperRun-or$summary.candidateManifest-cne$script:priorHelperManifest-or
    $summary.overall-cne'FAIL'-or(@($summary.helperTargetIds)-join',')-cne($script:priorHelperIds-join',')){throw 'prior_cleanup_summary_identity_mismatch'}
 $records=Get-Content -LiteralPath $targetsPath -Raw|ConvertFrom-Json
 $identified=@($records|Where-Object {$_.RunId})
 if($identified.Count-ne3){throw 'prior_cleanup_three_owned_records_required'}
 $planned=@(Get-CompleteHelperTargetPlan -Candidate $Candidate -RunIds $script:priorHelperIds -OperatorSid $OperatorSid)
 foreach($target in $planned){
  $matches=@($identified|Where-Object {$_.RunId-ceq$target.RunId})
  if($matches.Count-ne1){throw 'prior_cleanup_target_ownership_ambiguous'}
  $row=$matches[0]
  if($row.Created-isnot[bool]-or$row.Created-ne$true-or$row.CopyCompleted-isnot[bool]-or$row.CopyCompleted-ne$true-or
     $row.Name-cne$target.Name-or$row.Root-cne$target.Root-or$row.Data-cne$target.Data-or$row.InitialState-cne$target.InitialState){throw 'prior_cleanup_target_ownership_mismatch'}
  # Authorize only the three independently reconstructed registrations whose
  # creation and completed package are present in the pinned failed run.
  $target.RootCreated=$true;$target.CreationAttempted=$true;$target.Created=$true;$target.CopyCompleted=$true
 }
 return $planned
}

function Assert-CompletePriorHelperEnvironment {
 param([string]$OperatorSid)
 # Reuse the private module boundary that checks the elevated operator, exact
 # VirtualBox UUID, guest additions and fixed Windows paths. No elevation here.
 & $script:priorHelperTargetsModule {param($sid) Assert-CompleteHelperEnvironment $sid} $OperatorSid|Out-Null
}

function Get-CompletePriorHelperDataSnapshot {
 param($Target,[scriptblock]$Guard)
 Assert-CompleteHelperPlainPath $Target.Data|Out-Null
 $files=[Collections.Generic.List[object]]::new();$bytes=0L
 foreach($file in @(Get-CompleteHelperTreeFiles $Target.Data $Guard|Sort-Object FullName)){
  & $Guard|Out-Null
  $bytes+=[long]$file.Length
  if($files.Count-ge128-or$bytes-gt8MB){throw 'prior_cleanup_data_inventory_limit'}
  $files.Add([pscustomobject][ordered]@{name=$file.FullName.Substring($Target.Data.Length+1);bytes=[long]$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash})
 }
 return [pscustomobject][ordered]@{fileCount=$files.Count;totalBytes=$bytes;files=@($files.ToArray())}
}

function Invoke-CompletePriorHelperCleanup {
 param($Candidate,[string]$OperatorSid,[scriptblock]$Guard,[scriptblock]$CleanupGuard)
 if(-not$Guard){throw 'prior_cleanup_guard_required'}
 if(-not$CleanupGuard){$CleanupGuard=$Guard}
 Assert-CompletePriorHelperEnvironment $OperatorSid
 & $Guard|Out-Null
 $targets=@(Get-CompletePriorHelperCleanupPlan -Candidate $Candidate -OperatorSid $OperatorSid)
 $before=[ordered]@{}
 # Finish every registration/package/data precheck before the first disable.
 foreach($target in $targets){
  & $Guard|Out-Null
  $service=Assert-CompleteHelperRegistration $target
  if($service.State-ne'Stopped'-or[int]$service.ProcessId-ne0-or$service.StartMode-notin@('Manual','Disabled')){throw 'prior_cleanup_target_not_stopped'}
  Assert-CompleteHelperManifest $target.App $script:priorHelperManifest $Guard -WithCanaries|Out-Null
  $before[$target.RunId]=Get-CompletePriorHelperDataSnapshot $target $Guard
 }
 & $Guard|Out-Null
 $results=@(Confirm-CompleteHelperTargets -Targets $targets -Guard $CleanupGuard)
 if($results.Count-ne3){throw 'prior_cleanup_three_confirmations_required'}
 foreach($target in $targets){
  $matched=@($results|Where-Object {$_.RunId-ceq$target.RunId})
  if($matched.Count-ne1){throw 'prior_cleanup_confirmation_identity_mismatch'}
  $result=$matched[0]
  if($result.Name-cne$target.Name-or$result.Root-cne$target.Root-or$result.Data-cne$target.Data-or
     $result.State-ne'Stopped'-or[int]$result.ProcessId-ne0-or$result.StartMode-ne'Disabled'-or
     $result.Quiescent-ne$true-or$result.Preserved-ne$true-or$result.PackageVerified-ne$true-or$result.GuardFailed-eq$true-or$result.ResourceAbort-eq$true){throw 'prior_cleanup_quiescence_unconfirmed'}
 }
 $after=[ordered]@{}
 foreach($target in $targets){
  & $Guard|Out-Null
  $after[$target.RunId]=Get-CompletePriorHelperDataSnapshot $target $Guard
  if((ConvertTo-Json -InputObject $before[$target.RunId] -Depth 6 -Compress)-cne(ConvertTo-Json -InputObject $after[$target.RunId] -Depth 6 -Compress)){throw 'prior_cleanup_data_changed'}
 }
 & $Guard|Out-Null
 return [pscustomobject]@{ok=$true;scope='known_failed_campaign_cleanup_only';sourceCampaignRunId=$script:priorHelperRun;
  sourceCampaignOutcome='FAIL';sourceSummarySha256=$script:priorHelperSummaryHash;failedCampaignReclassified=$false;
  candidateManifestSha256=$script:priorHelperManifest;targets=$results;dataBefore=$before;dataAfter=$after;dataUnchanged=$true;oldEvidenceDeleted=$false}
}
Export-ModuleMember -Function Get-CompletePriorHelperCleanupPlan,Invoke-CompletePriorHelperCleanup
