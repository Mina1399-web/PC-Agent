param([switch]$Execute,[switch]$FocusS2Failures,[switch]$FocusHelperFailure,[switch]$RecoverPriorHelperCleanup,[switch]$FocusS3,[switch]$FocusSoakFailure)
$ErrorActionPreference='Stop'
if($FocusSoakFailure){$FocusS3=$true}
if($Execute-and$FocusS3-and-not$FocusSoakFailure){throw 'completed_wpf_gate_requires_soak_only_rerun'}
if($FocusS3-and($FocusS2Failures-or$FocusHelperFailure-or$RecoverPriorHelperCleanup)){throw 's3_focus_conflicts_with_s2_or_recovery'}
if($RecoverPriorHelperCleanup-and-not$FocusHelperFailure){throw 'prior_cleanup_recovery_requires_helper_only_focus'}
if($FocusHelperFailure){$FocusS2Failures=$true}

# Operator only. No reboot, power-off, security-policy changes or protected-service control.
function Assert-CampaignPlainPath([string]$Path) {
    $current=[IO.Path]::GetFullPath($Path)
    while($current) {
        if(Test-Path -LiteralPath $current) {
            if((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'reparse_path_rejected'}
        }
        $current=[IO.Path]::GetDirectoryName($current)
    }
}
function Get-CompleteS2GateProof {
 $required=@(
  @{run='59f607f3d6d9409cb7c6edcee69858d1';hash='BE3E3D161D0A67D2EB3271CDA363C2CF375C87AFB17647EEB52F8947DFB89D80';phase='S2-collector-failure-recovery'},
  @{run='cdab506f11d149b3a62d65e4b2811767';hash='79A73E210494AE2A53D60F9716B25B37121F00E3CF972992BC1E515FD39448F8';phase='S2-real-helper-remediation'}
 )
 foreach($item in $required){
  $path=Join-Path $env:LOCALAPPDATA ('HostAgent-DEV-evidence\complete-campaign-'+$item.run+'\summary.json')
  Assert-CampaignPlainPath $path
  if(-not(Test-Path -LiteralPath $path -PathType Leaf)-or(Get-Item -LiteralPath $path).Length-gt2MB-or
     (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash-cne$item.hash){throw 's2_gate_evidence_identity_mismatch'}
  $summary=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
  $phase=@($summary.phases|Where-Object {$_.id-ceq$item.phase})
  if($summary.campaignRunId-cne$item.run-or$summary.candidateManifest-cne$script:campaignCandidateHash-or
     $phase.Count-ne1-or$phase[0].status-cne'PASS'-or$phase[0].failure-or$phase[0].cleanupFailure-or$phase[0].details.ok-ne$true){throw 's2_gate_not_satisfied'}
  $result=$phase[0].details.result
  if($item.phase-ceq'S2-collector-failure-recovery'){
   if($result.finished-ne$true-or$result.aborted-eq$true-or$result.injectedCollectorFailures-ne2-or$result.collectorRecoveries-ne2-or
      $result.failures-ne0-or$result.helperCalls-ne0-or$phase[0].details.databaseQuickChecks.ok-ne$true){throw 'collector_gate_proof_incomplete'}
  }else{
   if($summary.safetyAbort-eq$true-or$summary.previousHelperArtifactsUnchanged-ne$true-or$summary.priorCleanupRecovery.ok-ne$true-or
      $result.ok-ne$true-or@($result.checks).Count-ne14-or@($result.checks|Where-Object {$_.ok-ne$true}).Count-or
      $result.nativeCalls-ne5-or$result.journalQuickCheck-cne'ok'){throw 'helper_gate_proof_incomplete'}
  }
  [pscustomobject]@{phase=$item.phase;status='PASS';campaignRunId=$item.run;summarySha256=$item.hash;candidateManifestSha256=$summary.candidateManifest}
 }
}
function Get-CompleteWpfGateProof {
 $run='a0e02d2045724bcfa5a797d8334fc8b3'
 $hash='8EA4F3C41D04EBA0A2A00BC708CA9C6F67DC65D24C17F4A2A3A1B2E6AF7461E9'
 $path=Join-Path $env:LOCALAPPDATA ('HostAgent-DEV-evidence\complete-campaign-'+$run+'\summary.json')
 Assert-CampaignPlainPath $path
 if(-not(Test-Path -LiteralPath $path -PathType Leaf)-or(Get-Item -LiteralPath $path).Length-gt2MB-or
    (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash-cne$hash){throw 'wpf_gate_evidence_identity_mismatch'}
 $summary=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
 $phase=@($summary.phases|Where-Object {$_.id-ceq'S3-real-WPF-private-IPC'})
 if($summary.campaignRunId-cne$run-or$summary.safetyAbort-eq$true-or$summary.previousHelperArtifactsUnchanged-ne$true-or
    $phase.Count-ne1-or$phase[0].status-cne'PASS'-or$phase[0].failure-or$phase[0].cleanupFailure-or
    $phase[0].details.ok-ne$true-or$phase[0].details.native.exitCode-ne0-or
    $phase[0].details.executedCandidate.manifestSha256-cne'5517CA5B758076E44E080B60059B4E43FF824FFD98075756F3AF7E11A6089E20'){
  throw 'wpf_gate_not_satisfied'
 }
 $proof=$phase[0].details.result.proof
 if($proof.PartialCleared-ne$true-or$proof.FreshMetrics-ne$true-or$proof.StaleCleared-ne$true-or
    $proof.OutageRecoveryCycles-lt3-or$proof.MinimumOutageSeconds-lt10-or$proof.ActionsAlwaysDisabled-ne$true-or
    $proof.WindowClosed-ne$true-or$proof.QuickCheckHealthy-ne$true){throw 'wpf_gate_proof_incomplete'}
 [pscustomobject]@{phase='S3-real-WPF-private-IPC';status='PASS';campaignRunId=$run;summarySha256=$hash;candidateManifestSha256=$phase[0].details.executedCandidate.manifestSha256}
}
function Assert-CampaignVm {
    if($env:COMPUTERNAME -ne 'AGENTPC' -or (Get-CimInstance Win32_ComputerSystem -OperationTimeoutSec 5).Model -ne 'VirtualBox' -or
        (Get-CimInstance Win32_ComputerSystemProduct -OperationTimeoutSec 5).UUID -ne '46543236-8C5E-49DA-953C-DB17F9B1E402' -or
        (Get-Service VBoxService).Status -ne 'Running' -or
        [Security.Principal.WindowsIdentity]::GetCurrent().Name -ine 'AgentPC\AgentPCdevtest') {throw 'approved_vm_or_account_mismatch'}
    if([IO.Path]::GetFullPath($env:ProgramData) -ine 'C:\ProgramData' -or [IO.Path]::GetFullPath($env:WINDIR) -ine 'C:\Windows'){throw 'unexpected_vm_system_paths'}
}
$script:campaignCandidate=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261017-complete-r6'))
$script:campaignCandidateHash='76B09BD10A479B55EC98F4073A4F76E10CC753309FD6F36F85594374CB0F2737'
$script:campaignS3Candidate=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261018-s3-r7'))
$script:campaignS3CandidateHash='5517CA5B758076E44E080B60059B4E43FF824FFD98075756F3AF7E11A6089E20'
$script:campaignSoakCandidate=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261018-soak-r8'))
$script:campaignSoakCandidateHash='AEEA44CBEA5F46C0216A11AC0BF1A9E59B3C82EC050B826DAD38F505D716BA61'
$script:campaignCatalogPath=Join-Path $PSScriptRoot 'complete-resilience-manifest.json'
$script:campaignCatalogHash='AA78A1D15A67B472A1B3A2157C0948B11039982CDF60BBE8B40CF69067ED101C'
$script:campaignRuntimeRoot='C:\Program Files\dotnet'
$script:campaignRuntimeHostSha256='ABCE6623A6729059CF39BB0F728C4A7804F625895099C88C17E6E704661E09EB'
$script:campaignProtectedPath='C:\Program Files\HostAgent\Service'
$script:campaignProtectedHashes=@{
    'HostAgent.Service.exe'='04DF669D662BB08BDBD76653E46158E3D701D7E0D48653439F8F836BB417CB87'
    'HostAgent.Core.dll'='E7F156748E61AB8AEBE49B80E2B9A379EAC4588559EA55BF0B806D5229100080'
    'HostAgent.Infrastructure.dll'='B86C9673CBDE1EDC755B1AD8FFA498373517EF1E3A2CD673261970931594A8AE'
    'HostAgent.Service.dll'='19772D28E83DB3704419F2C6EB1450F3FF51BD1FCD8ED2B9D63A8A32695CA840'
}
function Get-CampaignProtected {
    $service=Get-CimInstance Win32_Service -Filter "Name='HostAgent'" -OperationTimeoutSec 5 -ErrorAction Stop
    if(-not $service -or $service.State -ne 'Running' -or (Get-Service HostAgent).Status -ne 'Running' -or
        $service.StartName -ne 'NT SERVICE\HostAgent' -or $service.StartMode -ne 'Auto' -or
        $service.PathName -ne '"C:\Program Files\HostAgent\Service\HostAgent.Service.exe"' -or $service.ProcessId -le 0){throw 'protected_identity_or_state_mismatch'}
    $hashes=[ordered]@{}
    foreach($name in $script:campaignProtectedHashes.Keys) {
        $hash=(Get-FileHash -LiteralPath (Join-Path $script:campaignProtectedPath $name) -Algorithm SHA256).Hash
        if($hash -ne $script:campaignProtectedHashes[$name]){throw 'protected_binary_hash_mismatch'}
        $hashes[$name]=$hash
    }
    return [pscustomobject]@{name=$service.Name;state=$service.State;startMode=$service.StartMode;account=$service.StartName;path=$service.PathName;pid=[int]$service.ProcessId;hashes=$hashes}
}
function Get-CampaignProtectedHelper {
    $path='C:\Program Files\HostAgent\Helper\HostAgent.Helper.exe'
    try{$service=Get-CimInstance Win32_Service -Filter "Name='HostAgent.Helper'" -OperationTimeoutSec 5 -ErrorAction Stop}
    catch{$_.Exception.Data['CompleteFailureBoundary']='protected_helper_query';throw}
    if(-not $service -or $service.State -ne 'Running' -or $service.StartMode -ne 'Auto' -or
        $service.StartName -ne 'LocalSystem' -or $service.PathName -cne ('"'+$path+'"') -or $service.ProcessId -le 0){
        throw 'protected_helper_identity_or_state_mismatch'
    }
    return [pscustomobject]@{name=$service.Name;state=$service.State;startMode=$service.StartMode;account=$service.StartName;
        path=$service.PathName;pid=[int]$service.ProcessId;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash}
}
function Assert-CampaignArtifacts {
    $runtimeHost=Join-Path $script:campaignRuntimeRoot 'dotnet.exe'
    Assert-CampaignPlainPath $runtimeHost
    if(-not (Test-Path -LiteralPath $runtimeHost -PathType Leaf)){throw 'system_runtime_host_missing'}
    if((Get-FileHash -LiteralPath $runtimeHost -Algorithm SHA256).Hash -ne $script:campaignRuntimeHostSha256){throw 'system_runtime_host_identity_mismatch'}
    Assert-CampaignPlainPath $script:campaignCatalogPath
    if((Get-FileHash -LiteralPath $script:campaignCatalogPath -Algorithm SHA256).Hash -ne $script:campaignCatalogHash){throw 'campaign_catalog_hash_mismatch'}
    $catalog=Get-Content -LiteralPath $script:campaignCatalogPath -Raw | ConvertFrom-Json
    if($catalog.pythonExecutable) {
        $pythonPath='C:\Users\AgentPCdevtest\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
        if($catalog.pythonExecutable.path -cne $pythonPath -or (Get-FileHash -LiteralPath $pythonPath -Algorithm SHA256).Hash -ne $catalog.pythonExecutable.sha256){throw 'verified_python_identity_mismatch'}
    }
    foreach($entry in $catalog.helpers) {
        if($entry.path -notmatch '^[A-Za-z0-9_.-]+$'){throw 'unsafe_helper_manifest_path'}
        $path=Join-Path $PSScriptRoot $entry.path
        Assert-CampaignPlainPath $path
        if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256){throw 'campaign_helper_hash_mismatch'}
    }
    Assert-CampaignCandidate $script:campaignCandidate $script:campaignCandidateHash
    if($FocusS3){Assert-CampaignCandidate $script:campaignS3Candidate $script:campaignS3CandidateHash}
    if($FocusSoakFailure){Assert-CampaignCandidate $script:campaignSoakCandidate $script:campaignSoakCandidateHash}
}
function Get-CompleteProbeCandidate {
    if($FocusSoakFailure){return [pscustomobject]@{path=$script:campaignSoakCandidate;manifestSha256=$script:campaignSoakCandidateHash}}
    if($FocusS3){return [pscustomobject]@{path=$script:campaignS3Candidate;manifestSha256=$script:campaignS3CandidateHash}}
    return [pscustomobject]@{path=$script:campaignCandidate;manifestSha256=$script:campaignCandidateHash}
}
function Assert-CampaignCandidate([string]$Candidate,[string]$ManifestHash) {
    Assert-CampaignPlainPath $Candidate
    $manifestPath=Join-Path $Candidate 'candidate-manifest.json'
    if((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $ManifestHash){throw 'candidate_manifest_mismatch'}
    $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if($manifest.format -ne 1 -or @($manifest.files).Count -ne 75){throw 'candidate_inventory_mismatch'}
    $seen=@{}
    foreach($entry in $manifest.files) {
        if([IO.Path]::IsPathRooted($entry.path) -or $entry.path -match '(^|[/\\])\.\.([/\\]|$)' -or $entry.path -match ':'){throw 'unsafe_candidate_manifest_path'}
        $path=Join-Path $Candidate $entry.path
        Assert-CampaignPlainPath $path
        if($seen.ContainsKey($entry.path)){throw 'duplicate_manifest_entry'}
        $seen[$entry.path.Replace('/','\')]=$true
        if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256){throw 'candidate_file_hash_mismatch'}
    }
    $queue=[Collections.Generic.Queue[string]]::new(); $queue.Enqueue($Candidate); $actualCount=0
    while($queue.Count -gt 0) {
        foreach($item in Get-ChildItem -LiteralPath $queue.Dequeue() -Force) {
            if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'candidate_reparse_entry'}
            if($item.PSIsContainer){$queue.Enqueue($item.FullName);continue}
            if($item.FullName -eq $manifestPath){continue}
            $relative=$item.FullName.Substring($Candidate.Length+1)
            if(-not $seen.ContainsKey($relative)){throw 'candidate_unlisted_file'}
            $actualCount++
        }
    }
    if($actualCount -ne 75){throw 'candidate_extra_or_missing_files'}
    $runtime=Get-Content -LiteralPath (Join-Path $Candidate 'HostAgent.PipeProbe.runtimeconfig.json') -Raw | ConvertFrom-Json
    $frameworks=@($runtime.runtimeOptions.frameworks)
    if(@($frameworks|Where-Object name -eq 'Microsoft.NETCore.App').Count -ne 1 -or
       @($frameworks|Where-Object name -eq 'Microsoft.WindowsDesktop.App').Count -ne 1){throw 'candidate_wpf_runtime_framework_unresolved'}
    foreach($name in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')){
        $minimum=[version](@($frameworks|Where-Object name -eq $name)[0].version)
        $available=@(Get-ChildItem -LiteralPath ('C:\Program Files\dotnet\shared\'+$name) -Directory | Where-Object { $_.Name -match '^10\.0\.\d+$' -and [version]$_.Name -ge $minimum })
        if(-not $available.Count){throw 'system_service_runtime_missing'}
    }
}
function Assert-CampaignFastSafety {
    $free=if('BoundedScmPressure' -as [type]){[BoundedScmPressure]::FreeMiB()}else{(Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 5).FreePhysicalMemory/1024}
    if([double]::IsNaN($free) -or $free -le 512){throw 'hard_free_memory_floor_512_mib'}
    if($free -lt 768){throw 'soft_free_memory_floor_768_mib'}
    if(([IO.DriveInfo]::new('C:\')).AvailableFreeSpace -lt 20GB){throw 'disk_free_below_20_gib'}
    if(-not $script:campaignLastIdentityCheck -or ([DateTimeOffset]::UtcNow-$script:campaignLastIdentityCheck).TotalSeconds -ge 5) {
        $service=[CompleteProtectedServiceGuard]::Read()
        if($service.State -ne 'Running' -or [int]$service.ProcessId -ne $script:campaignBaseline.pid){throw 'protected_state_or_pid_changed'}
        $script:campaignLastIdentityCheck=[DateTimeOffset]::UtcNow
    }
    if(($FocusS2Failures -or $FocusS3) -and $script:campaignHelperBaseline){
        $helper=[CompleteProtectedServiceGuard]::ReadHelper()
        if($helper.State -ne 'Running' -or [int]$helper.ProcessId -ne $script:campaignHelperBaseline.pid){
            throw 'protected_helper_state_or_pid_changed'
        }
    }
}
function Assert-CampaignHelperSnapshot {
 if(($FocusS2Failures -or $FocusS3) -and $script:campaignHelperBaseline){
  $helper=Get-CampaignProtectedHelper
  if($helper.pid -ne $script:campaignHelperBaseline.pid -or $helper.sha256 -cne $script:campaignHelperBaseline.sha256){
   throw 'protected_helper_state_or_hash_changed'
  }
 }
}
function Assert-CampaignCleanupIdentity {
 $protected=[CompleteProtectedServiceGuard]::Read()
 if($protected.State-ne'Running'-or[int]$protected.ProcessId-ne$script:campaignBaseline.pid){throw 'protected_changed_before_guid_cleanup'}
 if(($FocusS2Failures-or$FocusS3)-and$script:campaignHelperBaseline){
  $helper=[CompleteProtectedServiceGuard]::ReadHelper()
  if($helper.State-ne'Running'-or[int]$helper.ProcessId-ne$script:campaignHelperBaseline.pid){throw 'protected_helper_state_or_pid_changed'}
 }
}
function Assert-CampaignPhase {
    Assert-CampaignVm
    Assert-CampaignArtifacts
    $decision=Get-CompleteMemoryDecision ([BoundedScmPressure]::FreeMiB())
  if($decision -eq 'hard_abort'){throw 'hard_free_memory_floor_512_mib'}
  if($decision -eq 'soft_abort'){throw 'soft_free_memory_floor_768_mib'}
  Assert-CampaignFastSafety
    $now=Get-CampaignProtected
    if($now.pid -ne $script:campaignBaseline.pid){throw 'protected_pid_changed'}
    Assert-CampaignHelperSnapshot
}


$script:completeSafetyLatched=$false
$script:completeFootprint=$null
$script:completeTargets=@()
$script:completeFixtureId=$null
$script:priorCleanupRecovery=$null
function Get-CompleteChildIds {
 @(Get-CimInstance Win32_Process -OperationTimeoutSec 5 -Filter "Name='HostAgent.PipeProbe.exe' OR Name='testhost.exe'" | ForEach-Object {[int]$_.ProcessId})
}
function Assert-CompleteFast {
 if($script:completeSafetyLatched){throw 'safety_abort_latched'}
 try {
  if($script:completeMonitor -and $script:completeMonitor.FailureCode){throw $script:completeMonitor.FailureCode}
  $decision=Get-CompleteMemoryDecision ([BoundedScmPressure]::FreeMiB())
  if($decision -eq 'hard_abort'){throw 'hard_free_memory_floor_512_mib'}
  if($decision -eq 'soft_abort'){throw 'soft_free_memory_floor_768_mib'}
  Assert-CampaignFastSafety
  if($script:completeMonitor -and $script:completeMonitor.FailureCode){throw $script:completeMonitor.FailureCode}
  if($script:completeFootprint -and (Test-Path -LiteralPath $script:completeFootprint) -and
     (-not $script:completeLastFootprintCheck -or ([DateTimeOffset]::UtcNow-$script:completeLastFootprintCheck).TotalSeconds -ge 2)) {
   Assert-CampaignPlainPath $script:completeFootprint
   $pending=[Collections.Generic.Stack[string]]::new();$pending.Push($script:completeFootprint);$count=0;$bytes=0L
   while($pending.Count){foreach($item in Get-ChildItem -LiteralPath $pending.Pop() -Force){
    if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'runtime_reparse_rejected'}
    $count++;if($count -gt 2000){throw 'runtime_entry_cap'}
    if($item.PSIsContainer){$pending.Push($item.FullName)}else{$bytes+=$item.Length;if($bytes -gt 128MB){throw 'runtime_storage_cap'}}
   }}
   $script:completeLastFootprintCheck=[DateTimeOffset]::UtcNow
  }
 }catch{$script:completeSafetyLatched=$true;throw}
}
function Assert-CompletePhase {
 try{Assert-CampaignPhase;Assert-CompleteFast}catch{$script:completeSafetyLatched=$true;throw}
}
function Assert-CompleteChildSafety($Result) {
 if($Result.resourceAbort -eq $true -or $Result.resourceGuardFailed -eq $true -or $Result.aborted -eq $true -or
    $Result.resourceFailureCode -match 'free_memory_floor|disk_free_below|memory_reading' -or
    $Result.errorCode -match 'free_memory_floor|disk_free_below|memory_reading' -or
    ($Result.PSObject.Properties.Name -contains 'minimumFreeMiB' -and $Result.minimumFreeMiB -lt 768)) {
  $script:completeSafetyLatched=$true;throw 'child_resource_abort'
 }
}
function Invoke-CompleteProbe([string[]]$Arguments,[string]$PhaseId,[int]$Deadline,[string]$Prefix) {
 $fixtureId=[guid]::NewGuid().ToString('N');$script:completeFixtureId=$fixtureId
 $script:completeFootprint=Join-Path $evidenceRoot ($Prefix+'-'+$fixtureId)
 if(Test-Path -LiteralPath $script:completeFootprint){throw 'new_fixture_already_exists'}
 $logs=Join-Path $logRoot $PhaseId
 try {
  $argv=@($Arguments[0],$fixtureId);if($Arguments.Count -gt 1){$argv+=@($Arguments|Select-Object -Skip 1)}
  $executedCandidate=Get-CompleteProbeCandidate
  $child=Invoke-CompleteBoundedProcess (Join-Path $executedCandidate.path 'HostAgent.PipeProbe.exe') $argv $Deadline $logs {Assert-CompleteFast}
  $last=Get-Content -LiteralPath $child.stdoutPath | Where-Object {$_ -match '^\{'} | Select-Object -Last 1 | ConvertFrom-Json
  if(-not $last){throw 'probe_result_missing'}
  Assert-CompleteChildSafety $last
  if($last.resourceAbort -eq $true -or $last.resourceGuardFailed -eq $true -or $last.aborted -eq $true -or
     ($last.PSObject.Properties.Name -contains 'minimumFreeMiB' -and $last.minimumFreeMiB -lt 768)){
     $script:completeSafetyLatched=$true;throw 'child_resource_abort'
  }
  return @{ok=($child.exitCode -eq 0 -and $last.ok -eq $true);fixtureId=$fixtureId;fixtureDirectory=$script:completeFootprint;executedCandidate=$executedCandidate;native=$child;result=$last}
 }finally{$script:completeFootprint=$null}
}
function Invoke-CompleteCollector {
 $phase='S2-collector-failure-recovery';$fixtureId=[guid]::NewGuid().ToString('N');$logs=Join-Path $logRoot $phase
 $script:completeFootprint=Join-Path $evidenceRoot ('runtime-candidate-'+$fixtureId)
 try{
  $child=Invoke-CompleteBoundedProcess (Join-Path $script:campaignCandidate 'HostAgent.PipeProbe.exe') @('isolated-runtime',$fixtureId,'resilience') 250 $logs {Assert-CompleteFast}
  $last=Get-Content -LiteralPath $child.stdoutPath | Where-Object {$_ -match '^\{'} | Select-Object -Last 1 | ConvertFrom-Json
  Assert-CompleteChildSafety $last
  if($last.aborted -eq $true -or $last.minimumFreeMiB -lt 768){$script:completeSafetyLatched=$true;throw 'collector_resource_abort'}
  $ok=$child.exitCode -eq 0 -and $last.finished -eq $true -and $last.failures -eq 0 -and $last.helperCalls -eq 0 -and
     $last.injectedCollectorFailures -eq 2 -and $last.collectorRecoveries -eq 2 -and $last.views -ge 10 -and $last.cpuSamples -ge $last.collectorRecoveries
  $checks=Join-Path $logs 'readonly-database-check';New-Item -ItemType Directory -Path $checks | Out-Null
  $python='C:\Users\AgentPCdevtest\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
  $locks=@()
  try {
   foreach($name in @('fixture.db','notices.db')){$locks += [IO.File]::Open((Join-Path $script:completeFootprint $name),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)}
   $dbChild=Invoke-CompleteBoundedProcess $python @('-I','-B',(Join-Path $PSScriptRoot 'check-remaining-runtime.py'),$fixtureId) 25 $checks {Assert-CompleteFast}
  }finally{foreach($handle in $locks){$handle.Dispose()}}
  $db=Get-Content -LiteralPath $dbChild.stdoutPath -Raw | ConvertFrom-Json
  return @{ok=($ok -and $dbChild.exitCode -eq 0 -and $db.ok -eq $true);fixtureId=$fixtureId;native=$child;result=$last;databaseQuickChecks=$db;scope='real_private_runtime_two_injected_collector_failures'}
 }finally{$script:completeFootprint=$null}
}
function Get-CompletePreviousHelperState {
 Assert-CampaignHelperSnapshot
 $priorRun='7df9273fc92f4002888aa4eea543080c'
 $priorIds=@('de0d788a83d44c118e8433a846394960','39a242cd86284048989872baa8a5fa78','2dc31f6ea2db4e41905c222dea176c1f')
 $priorCandidate=Join-Path $PSScriptRoot '..\..\..\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261017-complete-r5'
 $priorCandidate=[IO.Path]::GetFullPath($priorCandidate)
 $priorManifestHash='BE7E98D62B3C673EDC409DE08A6B36F2AB0D06D7435468ED2C9882EA6188DF8B'
 Assert-CampaignPlainPath $priorCandidate
 if((Get-FileHash -LiteralPath (Join-Path $priorCandidate 'candidate-manifest.json') -Algorithm SHA256).Hash -cne $priorManifestHash){throw 'prior_helper_candidate_manifest_mismatch'}
 Assert-CompleteHelperManifest $priorCandidate $priorManifestHash {Assert-CompleteFast}|Out-Null
 $planned=@(Get-CompleteHelperTargetPlan -Candidate @{Path=$priorCandidate;ManifestSha256=$priorManifestHash} -RunIds $priorIds -OperatorSid $identity.User.Value)
 $targets=[Collections.Generic.List[object]]::new()
 foreach($target in $planned){
  Assert-CompleteFast
  Assert-CompleteHelperPlainPath $target.Root
  if(-not(Test-Path -LiteralPath $target.Root -PathType Container) -or -not(Test-Path -LiteralPath $target.Data -PathType Container)){
   throw 'prior_helper_package_or_data_missing'
  }
  $service=Assert-CompleteHelperRegistration $target
  if($service.State -ne 'Stopped' -or [int]$service.ProcessId -ne 0 -or $service.StartMode -notin @('Manual','Disabled')){
   throw 'prior_helper_target_not_quiescent'
  }
  Assert-CompleteHelperManifest $target.App $priorManifestHash {Assert-CompleteFast} -WithCanaries|Out-Null
  $dataFiles=@(Get-CompleteHelperTreeFiles $target.Data {Assert-CompleteFast})
  $dataBytes=0L;$dataEntries=@()
  foreach($file in $dataFiles){$dataBytes+=[long]$file.Length;$dataEntries+=@{name=$file.FullName.Substring($target.Data.Length+1);bytes=[long]$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}}
  if($dataFiles.Count -gt 128 -or $dataBytes -gt 8MB){throw 'prior_helper_data_inventory_limit'}
  $targetRecord=[pscustomobject]@{runId=$target.RunId;service=$service.Name;state=$service.State;startMode=$service.StartMode;
   processId=[int]$service.ProcessId;image=$service.PathName;root=$target.Root;packageManifestVerified=$true;
   dataDirectory=$target.Data;dataFileCount=$dataFiles.Count;dataBytes=$dataBytes;dataInventory=$dataEntries;operation='read_only_preserved'}
  $targets.Add($targetRecord)
 }
 # Retain and compare the most recent failed setup as well as the older fixtures.
 # Its third package is partial and must never be launched or silently completed.
 $recentIds=@('ce729bac8e954244820f1afc369f7279','c25e837c5a2f4179920828fa9eaff7b4','0c66d84903534ab28030e83886629bda')
 $recentGroups=@(@{ids=$recentIds;completeCount=2})
 if($RecoverPriorHelperCleanup -or $FocusS3){$recentGroups+=@{ids=@('b75a0b684dc84d9ea88190cc702828d1','f6b56e621cb140b8ae65975f385b317a','66dea5c190424838915e802850350655');completeCount=3}}
 if($FocusS3){$recentGroups+=@{ids=@('3be4c2beacfa443b932434e11de16c27','e5a9d5416ea94b80ad03084b948c2eda','a29fde758f3747e38074e3d4427cdaa7');completeCount=3}}
 $recentTargets=[Collections.Generic.List[object]]::new()
 foreach($group in $recentGroups){
 $recentIndex=0
 foreach($target in @(Get-CompleteHelperTargetPlan -Candidate @{Path=$script:campaignCandidate;ManifestSha256=$script:campaignCandidateHash} -RunIds $group.ids -OperatorSid $identity.User.Value)){
  Assert-CompleteFast
  $service=Assert-CompleteHelperRegistration $target
  if($service.State-ne'Stopped'-or[int]$service.ProcessId-ne0-or$service.StartMode-ne'Disabled'){throw 'recent_failed_target_not_quiescent'}
  if(-not(Test-Path -LiteralPath $target.Data -PathType Container)){throw 'recent_failed_target_data_missing'}
  $complete=$recentIndex-lt$group.completeCount
  if($complete){Assert-CompleteHelperManifest $target.App $script:campaignCandidateHash {Assert-CompleteFast} -WithCanaries|Out-Null}
  $inventory=@();$bytes=0L
  foreach($file in @(Get-CompleteHelperTreeFiles $target.Root {Assert-CompleteFast}|Sort-Object FullName)){
   $bytes+=[long]$file.Length
   if($inventory.Count-ge512-or$bytes-gt128MB){throw 'recent_failed_target_inventory_limit'}
   $inventory+=@{name=$file.FullName.Substring($target.Root.Length+1);bytes=[long]$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
  }
  $recentTargets.Add([pscustomobject]@{runId=$target.RunId;root=$target.Root;state=$service.State;pid=[int]$service.ProcessId;
   startMode=$service.StartMode;packageComplete=$complete;inventory=$inventory;totalBytes=$bytes;preserved=$true})
  $recentIndex++
 }
 }
 $fixture=Join-Path $env:LOCALAPPDATA ('HostAgent-DEV-evidence\complete-helper-bb53a38989824834be23c24806fb1d41')
 $journal=Join-Path $fixture 'helper-journal.db'
 Assert-CampaignPlainPath $fixture
 if(-not(Test-Path -LiteralPath $journal -PathType Leaf)){throw 'prior_helper_journal_missing'}
 $python='C:\Users\AgentPCdevtest\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
 $journalLock=[IO.File]::Open($journal,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 try{
  $check=@'
import pathlib, sqlite3, sys, time
path=pathlib.Path(sys.argv[1])
for suffix in ("-wal","-journal"):
 sidecar=pathlib.Path(str(path)+suffix)
 if sidecar.exists() and sidecar.stat().st_size: sys.exit("nonempty_sidecar_check_not_run")
connection=None
try:
 connection=sqlite3.connect(path.as_uri()+"?mode=ro&immutable=1",uri=True,timeout=3)
 connection.execute("PRAGMA query_only=ON")
 connection.execute("PRAGMA temp_store=MEMORY")
 deadline=time.monotonic()+10
 connection.set_progress_handler(lambda: int(time.monotonic()>=deadline),1000)
 rows=connection.execute("PRAGMA quick_check").fetchall()
 print("quick_check="+("ok" if rows==[("ok",)] else "not_ok"))
 sys.exit(0 if rows==[("ok",)] else 1)
except sqlite3.Error: sys.exit("quick_check_failed")
finally:
 if connection is not None: connection.close()
'@
  $checkOutput=@($check|& $python -I -B - $journal)
  if($LASTEXITCODE -ne 0 -or $checkOutput -notcontains 'quick_check=ok'){throw 'prior_helper_journal_quick_check_failed'}
 }finally{$journalLock.Dispose()}
 return [pscustomobject]@{campaignRunId=$priorRun;candidateManifestSha256=$priorManifestHash;targets=$targets.ToArray();
  privateJournalPath=$journal;privateJournalSha256=(Get-FileHash -LiteralPath $journal -Algorithm SHA256).Hash;
  privateJournalQuickCheck='ok';privateFixturePreserved=$true;oldEvidenceDeleted=$false;helperService=$script:campaignHelperBaseline;
  recentFailedCampaigns=@('59f607f3d6d9409cb7c6edcee69858d1';if($RecoverPriorHelperCleanup-or$FocusS3){'901779e42bc548b1a54746172e681981'};if($FocusS3){'cdab506f11d149b3a62d65e4b2811767'});recentFailedTargets=$recentTargets.ToArray()}
}
function Invoke-CompleteAutoScm {
 $logs=Join-Path $logRoot 'S1-automatic-scm-recovery'
 $child=Invoke-CompleteBoundedProcess $powershell @('-NoProfile','-NonInteractive','-File',(Join-Path $PSScriptRoot 'run-complete-scm-auto.ps1'),'-RunId',$autoId,'-Execute') 600 $logs {Assert-CompleteFast}
 $path=Join-Path $evidenceRoot ('scm-candidate-'+$autoId+'\result.json');Assert-CampaignPlainPath $path
 if((Get-Item -LiteralPath $path).Length -gt 524288){throw 'scm_result_oversized'}
 $result=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
 if($result.failureCode -match 'memory_floor_breached' -or $result.failureReason -match 'free_memory_floor|c_free_floor' -or $result.memoryGuard -isnot [string]){$script:completeSafetyLatched=$true}
 return @{ok=($child.exitCode -eq 0 -and (Test-RemainingAutoScmResult $result $autoId $script:campaignCandidateHash));native=$child;resultPath=$path;resultSha256=(Get-FileHash -LiteralPath $path).Hash;result=$result}
}
function Invoke-CompleteHelper {
 $logs=Join-Path $logRoot 'S2-real-helper-remediation'
 $scenarioFailure=$null
 $ownedTargets=$null
 $script:completeHelperSetupClock=[Diagnostics.Stopwatch]::StartNew()
 try{
   try{
    $returnedTargets=@(New-CompleteHelperTargets -Candidate @{Path=$script:campaignCandidate;ManifestSha256=$script:campaignCandidateHash} -RunIds $helperIds -OperatorSid $identity.User.Value -TargetState ([ref]$ownedTargets) -Guard {Assert-CompleteFast;if($script:completeHelperSetupClock.Elapsed.TotalSeconds -ge 180){throw 'helper_setup_deadline'}})
    $script:completeTargets=@($ownedTargets)
    Assert-CompleteHelperFactoryResult -ReturnedTargets $returnedTargets -OwnedTargets $script:completeTargets -RunIds $helperIds
   }
  catch{if($_.Exception.Data.Contains('Targets')){$script:completeTargets=@($_.Exception.Data['Targets'])}elseif($ownedTargets){$script:completeTargets=@($ownedTargets)};throw}
  [IO.File]::WriteAllText((Join-Path $logs 'targets.json'),($script:completeTargets|Select-Object RunId,Name,Root,Data,InitialState,Created,CopyCompleted|ConvertTo-Json -Depth 4))
  $missing=[guid]::NewGuid().ToString('N');$helperRun=[guid]::NewGuid().ToString('N')
  if(Get-Service ('HostAgentDevScm_'+$missing) -ErrorAction SilentlyContinue){throw 'missing_target_collision'}
  $child=Invoke-CompleteBoundedProcess (Join-Path $script:campaignCandidate 'HostAgent.PipeProbe.exe') @('isolated-complete-helper',$helperRun,$helperIds[0],$helperIds[1],$helperIds[2],$missing) 240 $logs {Assert-CompleteFast}
  $last=Get-Content -LiteralPath $child.stdoutPath | Where-Object {$_ -match '^\{'} | Select-Object -Last 1 | ConvertFrom-Json
  Assert-CompleteChildSafety $last
  return @{ok=($child.exitCode -eq 0 -and $last.ok -eq $true);fixtureId=$helperRun;native=$child;result=$last;targetIds=$helperIds;scope='real_native_GUID_services_private_helper_stack_not_SYSTEM_bootstrap'}
 }catch{
  $scenarioFailure=Get-CompleteFailureDiagnostic $_ 'scenario'
  throw
 }finally{
  if($script:completeTargets.Count){
   try {
     $cleanup=@(Confirm-CompleteHelperTargets -Targets $script:completeTargets -Guard {Assert-CampaignCleanupIdentity})
    [IO.File]::WriteAllText((Join-Path $logs 'target-quiescence.json'),($cleanup|ConvertTo-Json -Depth 6))
   }catch{
    $script:completeSafetyLatched=$true
    if($scenarioFailure){$_.Exception.Data['CompleteScenarioFailure']=$scenarioFailure}
    $_.Exception.Data['CompleteCleanupFailure']=Get-CompleteFailureDiagnostic $_ 'cleanup'
    if($_.Exception.Data.Contains('TargetResults')){[IO.File]::WriteAllText((Join-Path $logs 'target-quiescence.json'),($_.Exception.Data['TargetResults']|ConvertTo-Json -Depth 6))}
    throw
   }
  }
 }
}
function Confirm-CompleteQuiescent($Phase) {
 try {
 $additional=$false
 if($Phase.id -eq 'S1-automatic-scm-recovery'){
  $service=Get-CimInstance Win32_Service -OperationTimeoutSec 5 -Filter ("Name='HostAgentDevScm_"+$autoId+"'")
  if($service){
   Assert-CampaignVm
   if((Get-CampaignProtected).pid -ne $script:campaignBaseline.pid){throw 'protected_changed_before_cleanup'}
   $image='C:\ProgramData\HostAgent-DEV-SCM-'+$autoId+'\app\HostAgent.PipeProbe.exe';Assert-CampaignPlainPath $image
   $expected='"'+$image+'" isolated-scm-service '+$autoId+' '+$identity.User.Value+' crash-proof'
   if($service.PathName -cne $expected -or $service.StartName -cne ('NT SERVICE\HostAgentDevScm_'+$autoId) -or $service.StartMode -notin @('Manual','Disabled') -or
      (Get-FileHash -LiteralPath $image).Hash -ne (Get-FileHash -LiteralPath (Join-Path $script:campaignCandidate 'HostAgent.PipeProbe.exe')).Hash){throw 'isolated_cleanup_identity_mismatch'}
   if($service.StartMode -ne 'Disabled' -or -not [IsolatedScmRecovery]::IsDisabled($autoId) -or $service.State -ne 'Stopped'){
    [IsolatedScmRecovery]::Set($autoId,$false);$additional=$true
    $controller=[ServiceProcess.ServiceController]::new($service.Name)
    try{if($controller.Status -ne 'Stopped'){$controller.Stop();$controller.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(15))}}finally{$controller.Dispose()}
   }
   $now=Get-CimInstance Win32_Service -OperationTimeoutSec 5 -Filter ("Name='"+$service.Name+"'")
   if($now.State -ne 'Stopped' -or $now.ProcessId -ne 0 -or $now.StartMode -ne 'Disabled' -or -not [IsolatedScmRecovery]::IsDisabled($autoId)){throw 'isolated_quiescence_unconfirmed'}
  }
 }
 if($Phase.id -eq 'S2-real-helper-remediation' -and $script:completeTargets.Count){
  Confirm-CompleteHelperTargets -Targets $script:completeTargets -Guard {Assert-CampaignCleanupIdentity} | Out-Null
 }
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do{$new=@(Get-CompleteChildIds|Where-Object {$_ -notin $script:completeInitialChildren});if($new.Count){Start-Sleep -Milliseconds 250}}while($new.Count -and $clock.Elapsed.TotalSeconds -lt 5)
 if($new.Count){throw 'new_child_process_still_running'}
 Assert-CampaignVm
 if((Get-CampaignProtected).pid-ne$script:campaignBaseline.pid){throw 'protected_changed_before_guid_cleanup'}
 Assert-CampaignHelperSnapshot
 return @{safe=$true;scenarioFailed=$additional}
 }catch{$script:completeSafetyLatched=$true;throw}
}
Assert-CampaignVm;Assert-CampaignArtifacts
$script:campaignBaseline=Get-CampaignProtected
if((Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 5).FreePhysicalMemory/1024 -lt 1024){throw 'initial_free_memory_below_1024_mib'}
if(([IO.DriveInfo]::new('C:\')).AvailableFreeSpace -lt 20GB){throw 'initial_disk_free_below_20_gib'}
if([IO.Path]::GetFullPath($env:LOCALAPPDATA) -ine 'C:\Users\AgentPCdevtest\AppData\Local'){throw 'unexpected_local_evidence_root'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$id=[guid]::NewGuid().ToString('N');$autoId=[guid]::NewGuid().ToString('N');$helperIds=@(1..3|ForEach-Object {[guid]::NewGuid().ToString('N')})
$evidenceRoot=Join-Path $env:LOCALAPPDATA 'HostAgent-DEV-evidence';$logRoot=Join-Path $evidenceRoot ('complete-campaign-'+$id)
Assert-CampaignPlainPath $logRoot;if(Test-Path -LiteralPath $logRoot){throw 'campaign_already_exists'}
$powershell='C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
$commonLimits=@{initialFreeMiB=1024;abortFreeMiB=768;hardAbortFreeMiB=512;diskFreeGiB=20;pollMilliseconds=250;oldEvidenceDeleted=$false;noPhysicalPressure=$true;continueAfterScenarioFailure=$true;continueAfterSafetyFailure=$false}
$plan=@(
 @{id='S1-automatic-scm-recovery';kind='Auto';scope='one_automatic_restart_current_release_GUID_SCM_two_DB_committed_pending_proofs';limits=@{deadlineSeconds=600;restartDelayMs=5000;maximumAutomaticRestarts=1};run={Invoke-CompleteAutoScm}},
 @{id='S2-storage-unavailable';kind='Auto';scope='live_private_runtime_actual_exclusive_DB_lock_then_recovery';limits=@{deadlineSeconds=240;lockSeconds=37;fixtureMiB=32};run={Invoke-CompleteProbe @('isolated-complete-storage','unavailable') 'S2-storage-unavailable' 240 'complete-storage'}},
 @{id='S2-storage-full';kind='Auto';scope='real_SQLite_64KiB_quota_and_live_write_boundary_FULL_equivalent_no_C_fill';limits=@{deadlineSeconds=240;faultSeconds=25;physicalVolumeFull=$false;fixtureMiB=32};run={Invoke-CompleteProbe @('isolated-complete-storage','full') 'S2-storage-full' 240 'complete-storage'}},
 @{id='S2-storage-corrupt';kind='Auto';scope='real_corrupt_disposable_DB_preserve_and_recover_verified_backup_to_new_directory';limits=@{deadlineSeconds=240;corruptBytes=4096;inPlaceRepair=$false;fixtureMiB=32};run={Invoke-CompleteProbe @('isolated-complete-storage','corrupt') 'S2-storage-corrupt' 240 'complete-storage'}},
 @{id='S2-collector-failure-recovery';kind='Auto';scope='real_runtime_private_injected_collector_no_OS_service_disabling';limits=@{deadlineSeconds=250;activeSeconds=190;failures=2;helperCalls=0};run={Invoke-CompleteCollector}},
 @{id='S2-real-helper-remediation';kind='Auto';scope='real_private_helper_IPC_policy_approvals_journal_native_GUID_service_success_failure_timeout_replay_cooldown';limits=@{setupDeadlineSeconds=180;deadlineSeconds=240;targetLeaseSeconds=300;targets=3;maximumNativeCalls=5};run={Invoke-CompleteHelper}},
 @{id='S3-real-WPF-private-IPC';kind='Auto';scope='actual_MainWindow_controller_authenticated_private_View_dispatcher_SQLite_partial_stale_outage_recovery';limits=@{deadlineSeconds=195;outageCycles=3;outageSeconds=10;fixtureMiB=32;PCOperations=0};run={Invoke-CompleteProbe @('isolated-complete-wpf') 'S3-real-WPF-private-IPC' 195 'complete-wpf'}},
 @{id='S3-thirty-minute-fault-soak';kind='Auto';scope='real_private_production_runtime_six_fault_cycles_CPU_collector_write_FULL_equivalent_View_and_concurrent_recovery';limits=@{activeSeconds=1800;deadlineSeconds=1960;fixtureMiB=128;fixtureEntries=256;maximumWorkingSetMiB=512;maximumPrivateMiB=768;helperCalls=0};run={Invoke-CompleteProbe @('isolated-complete-soak') 'S3-thirty-minute-fault-soak' 1960 'complete-soak'}},
 @{id='S4-hard-floor-safe-equivalent';kind='Auto';scope='synthetic_512_boundary_logic_no_physical_pressure_no_threshold_changes';limits=@{softMiB=768;hardMiB=512;physicalPressure=$false};run={
  $observed=@();$ok=$true
  foreach($case in @(@(512,'hard_abort'),@(511.9,'hard_abort'),@(767.9,'soft_abort'),@(768,'continue'),@([double]::NaN,'hard_abort'))){
   $actual=Get-CompleteMemoryDecision $case[0];$continuous=[CompleteCampaignMonitor]::Evaluate($case[0]);$expectedContinuous=$(if($case[1] -eq 'hard_abort'){'hard_free_memory_floor_512_mib'}elseif($case[1] -eq 'soft_abort'){'soft_free_memory_floor_768_mib'}else{$null})
   $ok=$ok -and $actual -eq $case[1] -and $continuous -ceq $expectedContinuous
   $observed+=@{input=$(if([double]::IsNaN($case[0])){'NaN'}else{$case[0]});decision=$actual;continuousMonitorCode=$continuous;expected=$case[1]}
  }
  @{ok=$ok;cases=$observed;physical512Reached=$false;thresholdsChanged=$false}
 }},
 @{id='S5-manual-VM-reboot-power';kind='Manual';scope='separate_manual_gate_only';limits=@{guestPowerOperations=0};reason='fresh_host_side_rollback_verification_and_separate_manual_reboot_required';manualStep='Separately verify VirtualBox VM UUID and rollback snapshot dc8af832-31d6-49ac-a8c3-f00d9381597f, preserve clean-win-base, agree disposable fixture and manual power operation. This script never reboots or powers off.'}
)
foreach($phase in $plan){$phase.logDirectory=Join-Path $logRoot $phase.id}
if($FocusS2Failures){$plan=@($plan|Where-Object {$_.id -in @('S2-collector-failure-recovery','S2-real-helper-remediation')})}
if($FocusHelperFailure){$plan=@($plan|Where-Object {$_.id -eq 'S2-real-helper-remediation'})}
if($FocusS3){$plan=@($plan|Where-Object {$_.id -in @('S3-real-WPF-private-IPC','S3-thirty-minute-fault-soak')})}
if($FocusSoakFailure){$plan=@($plan|Where-Object {$_.id -eq 'S3-thirty-minute-fault-soak'})}
if($FocusSoakFailure){
 $plan[0].limits.scheduler='observed_recovery_between_faults'
 $plan[0].limits.observationBudgetSeconds=40
 $plan[0].limits.recoveryBudgetSeconds=100
 $plan[0].limits.cleanupBudgetSeconds=40
}
$s2GateProof=@()
if($FocusS3){$s2GateProof=@(Get-CompleteS2GateProof)}
$wpfGateProof=$null
if($FocusSoakFailure){$wpfGateProof=Get-CompleteWpfGateProof}
$priorCleanupPlan=@()
if($RecoverPriorHelperCleanup-and-not$Execute){
 Import-Module (Join-Path $PSScriptRoot 'CompletePriorHelperCleanup.psm1')
 $priorCleanupPlan=@(Get-CompletePriorHelperCleanupPlan -Candidate @{Path=$script:campaignCandidate;ManifestSha256=$script:campaignCandidateHash} -OperatorSid $identity.User.Value)
}
if(-not $Execute){
 [pscustomobject]@{mode='READ_ONLY_PREFLIGHT';priorCleanupTargets=@($priorCleanupPlan|Select-Object RunId,Name,Root,Data);focusS2Failures=[bool]$FocusS2Failures;focusHelperFailure=[bool]$FocusHelperFailure;focusS3=[bool]$FocusS3;focusSoakFailure=[bool]$FocusSoakFailure;wpfGateProof=$wpfGateProof;s2GateProof=$s2GateProof;recoverPriorHelperCleanup=[bool]$RecoverPriorHelperCleanup;executedCandidate=(Get-CompleteProbeCandidate);vm=$env:COMPUTERNAME;account=$identity.Name;runtimeRoot=$script:campaignRuntimeRoot;runtimeHostSha256=$script:campaignRuntimeHostSha256;freeMemoryMiB=((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1024);cFreeGiB=(([IO.DriveInfo]::new('C:\')).AvailableFreeSpace/1GB);candidate=$script:campaignCandidate;candidateManifestSha256=$script:campaignCandidateHash;catalogSha256=$script:campaignCatalogHash;protected=$script:campaignBaseline;limits=$commonLimits;phases=@($plan|ForEach-Object{[pscustomobject]@{id=$_.id;kind=$_.kind;scope=$_.scope;limits=$_.limits;manualStep=$_['manualStep']}})}|ConvertTo-Json -Depth 9
 return
}
if(-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'administrator_token_required'}
# Published apphosts use the same verified system runtime as SCM; no build SDK.
$env:DOTNET_ROOT=$script:campaignRuntimeRoot
$env:DOTNET_ROOT_X64=$script:campaignRuntimeRoot
Import-Module (Join-Path $PSScriptRoot 'IsolatedScmSafety.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'CompleteDiagnostics.psm1')
Import-Module (Join-Path $PSScriptRoot 'CompleteCampaignPlan.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'CompleteResilience.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'CompleteHelperTargets.psm1') -Force
if($RecoverPriorHelperCleanup){Import-Module (Join-Path $PSScriptRoot 'CompletePriorHelperCleanup.psm1')}
Add-Type -Path (Join-Path $PSScriptRoot 'CompleteCampaignMonitor.cs')
if($FocusS2Failures -or $FocusS3){$script:campaignHelperBaseline=Get-CampaignProtectedHelper}
$script:completeMonitor=[CompleteCampaignMonitor]::new()
try {
 Add-Type -Path (Join-Path $PSScriptRoot 'CompleteProtectedServiceGuard.cs')
 Add-Type -Path (Join-Path $PSScriptRoot 'BoundedScmPressure.cs')
 Add-Type -Path (Join-Path $PSScriptRoot 'IsolatedScmRecovery.cs')
 Assert-CompleteFast
}catch{$script:completeMonitor.Dispose();throw}
$script:completeInitialChildren=@(Get-CompleteChildIds)
New-Item -ItemType Directory -Path $logRoot | Out-Null
foreach($phase in $plan){New-Item -ItemType Directory -Path $phase.logDirectory | Out-Null}
if($RecoverPriorHelperCleanup){
 try{
  $script:priorCleanupRecovery=Invoke-CompletePriorHelperCleanup -Candidate @{Path=$script:campaignCandidate;ManifestSha256=$script:campaignCandidateHash} -OperatorSid $identity.User.Value -Guard {Assert-CompleteFast} -CleanupGuard {Assert-CampaignCleanupIdentity}
  [IO.File]::WriteAllText((Join-Path $logRoot 'prior-helper-cleanup-recovery.json'),($script:priorCleanupRecovery|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
 }catch{
  $script:completeSafetyLatched=$true
  $failure=[pscustomobject]@{ok=$false;sourceCampaignRunId='901779e42bc548b1a54746172e681981';failure=(Get-CompleteFailureDiagnostic $_ 'cleanup');targets=$_.Exception.Data['TargetResults'];oldEvidenceDeleted=$false}
  [IO.File]::WriteAllText((Join-Path $logRoot 'prior-helper-cleanup-recovery.json'),($failure|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
  $script:completeMonitor.Dispose();throw
 }
}
if($FocusS2Failures -or $FocusS3){
 $script:previousHelperState=Get-CompletePreviousHelperState
 [IO.File]::WriteAllText((Join-Path $logRoot 'previous-helper-state.json'),($script:previousHelperState|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
}
$summaryPath=Join-Path $logRoot 'summary.json'
[pscustomobject]@{campaignRunId=$id;summaryPath=$summaryPath;evidenceDirectory=$logRoot;soakActiveSeconds=1800}|ConvertTo-Json
$save={param($rows)
 $report=@{campaignRunId=$id;vm='AGENTPC';account=$identity.Name;candidateManifest=$script:campaignCandidateHash;executedCandidate=(Get-CompleteProbeCandidate);catalogSha256=$script:campaignCatalogHash;protectedBaseline=$script:campaignBaseline;protectedHelperBaseline=$script:campaignHelperBaseline;previousHelperArtifacts=$script:previousHelperState;priorCleanupRecovery=$script:priorCleanupRecovery;previousHelperArtifactsAfter=$script:previousHelperStateAfter;previousHelperArtifactsUnchanged=$script:previousHelperArtifactsUnchanged;previousHelperPostCheckFailure=$script:previousHelperPostCheckFailure;focusS2Failures=[bool]$FocusS2Failures;focusHelperFailure=[bool]$FocusHelperFailure;focusS3=[bool]$FocusS3;focusSoakFailure=[bool]$FocusSoakFailure;wpfGateProof=$wpfGateProof;s2GateProof=$s2GateProof;recoverPriorHelperCleanup=[bool]$RecoverPriorHelperCleanup;limits=$commonLimits;autoScmRunId=$autoId;helperTargetIds=$helperIds;phases=@($rows);overall=$(if(@($rows|Where-Object status -eq FAIL).Count -or $script:completeSafetyLatched -or (($FocusS2Failures -or $FocusS3) -and $script:previousHelperArtifactsUnchanged -ne $true)){'FAIL'}elseif(@($rows|Where-Object {$_.status -eq 'NOT RUN' -and $_.id -ne 'S5-manual-VM-reboot-power'}).Count){'INCOMPLETE'}elseif($FocusSoakFailure){'FOCUSED_SOAK_PASS_FINAL_GATES_PENDING'}elseif($FocusS3){'FOCUSED_S3_PASS_FINAL_GATES_PENDING'}elseif($FocusS2Failures){'FOCUSED_S2_PASS_REMAINING_GATES_PENDING'}else{'AUTOMATIC_GATES_PASS_REBOOT_PENDING'});safetyAbort=$script:completeSafetyLatched;updatedUtc=[DateTimeOffset]::UtcNow.ToString('O')}
 [IO.File]::WriteAllText($summaryPath,($report|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
 foreach($row in $rows){[IO.File]::WriteAllText((Join-Path $row.logDirectory 'phase.json'),($row|ConvertTo-Json -Depth 18),[Text.UTF8Encoding]::new($false))}
}
try{
 $rows=Invoke-RemainingResiliencePlan $plan $id {Assert-CompletePhase} {param($phase) Confirm-CompleteQuiescent $phase} $save
 if(($FocusS2Failures -or $FocusS3) -and -not $script:completeSafetyLatched){
  try{
   $script:previousHelperStateAfter=Get-CompletePreviousHelperState
   $before=ConvertTo-Json -InputObject $script:previousHelperState -Depth 10 -Compress
   $after=ConvertTo-Json -InputObject $script:previousHelperStateAfter -Depth 10 -Compress
   if($before -cne $after){throw 'previous_helper_artifacts_changed'}
   $script:previousHelperArtifactsUnchanged=$true
  }catch{
   $script:previousHelperArtifactsUnchanged=$false
   $script:previousHelperPostCheckFailure=Get-CompleteFailureDiagnostic $_ 'cleanup'
  }
  & $save $rows | Out-Null
 }
}
finally{$script:completeMonitor.Dispose()}
$finalSummary=Get-Content -LiteralPath $summaryPath -Raw|ConvertFrom-Json
[pscustomobject]@{campaignRunId=$id;overall=$finalSummary.overall;executedCandidate=$finalSummary.executedCandidate;wpfGateProof=$finalSummary.wpfGateProof;priorCleanupRecovered=$script:priorCleanupRecovery.ok;previousHelperArtifactsUnchanged=$script:previousHelperArtifactsUnchanged;previousHelperPostCheckFailure=$script:previousHelperPostCheckFailure;summaryPath=$summaryPath;summarySha256=(Get-FileHash -LiteralPath $summaryPath).Hash;phases=@($rows|Select-Object id,status,reason,failure,cleanupFailure,scope,logDirectory)}|ConvertTo-Json -Depth 6
if(@($rows|Where-Object status -eq FAIL).Count -or $script:completeSafetyLatched -or (($FocusS2Failures -or $FocusS3) -and $script:previousHelperArtifactsUnchanged -ne $true)){exit 1}






