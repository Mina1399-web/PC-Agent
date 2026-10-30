param([switch]$Execute)
$ErrorActionPreference='Stop'

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
function Assert-CampaignVm {
    if($env:COMPUTERNAME -ne 'AGENTPC' -or (Get-CimInstance Win32_ComputerSystem -OperationTimeoutSec 5).Model -ne 'VirtualBox' -or
        (Get-CimInstance Win32_ComputerSystemProduct -OperationTimeoutSec 5).UUID -ne '0bb543d1-7bb0-43ed-9c94-596d63ef3310' -or
        (Get-Service VBoxService).Status -ne 'Running' -or
        [Security.Principal.WindowsIdentity]::GetCurrent().Name -ine 'AgentPC\vmdev') {throw 'approved_vm_or_account_mismatch'}
    if([IO.Path]::GetFullPath($env:ProgramData) -ine 'C:\ProgramData' -or [IO.Path]::GetFullPath($env:WINDIR) -ine 'C:\Windows'){throw 'unexpected_vm_system_paths'}
}
$script:campaignCandidate=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261017-r5'))
$script:campaignCandidateHash='EBBAD0807899BB8243AE71B2877C4C02B4C3F6024024EE748A179342428CAAAB'
$script:campaignCatalogPath=Join-Path $PSScriptRoot 'dev-resilience-campaign-manifest.json'
$script:campaignCatalogHash='6085248AC53F52E132A91DF8F9BA738407B37327CED213448FF47D548B98C6A9'
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
function Assert-CampaignArtifacts {
    Assert-CampaignPlainPath $script:campaignCatalogPath
    if((Get-FileHash -LiteralPath $script:campaignCatalogPath -Algorithm SHA256).Hash -ne $script:campaignCatalogHash){throw 'campaign_catalog_hash_mismatch'}
    $catalog=Get-Content -LiteralPath $script:campaignCatalogPath -Raw | ConvertFrom-Json
    if($catalog.pythonExecutable) {
        $pythonPath='C:\Users\vmdev\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
        if($catalog.pythonExecutable.path -cne $pythonPath -or (Get-FileHash -LiteralPath $pythonPath -Algorithm SHA256).Hash -ne $catalog.pythonExecutable.sha256){throw 'verified_python_identity_mismatch'}
    }
    foreach($entry in $catalog.helpers) {
        if($entry.path -notmatch '^[A-Za-z0-9_.-]+$'){throw 'unsafe_helper_manifest_path'}
        $path=Join-Path $PSScriptRoot $entry.path
        Assert-CampaignPlainPath $path
        if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256){throw 'campaign_helper_hash_mismatch'}
    }
    Assert-CampaignPlainPath $script:campaignCandidate
    $manifestPath=Join-Path $script:campaignCandidate 'candidate-manifest.json'
    if((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $script:campaignCandidateHash){throw 'candidate_manifest_mismatch'}
    $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if($manifest.format -ne 1 -or @($manifest.files).Count -ne 73){throw 'candidate_inventory_mismatch'}
    $seen=@{}
    foreach($entry in $manifest.files) {
        if([IO.Path]::IsPathRooted($entry.path) -or $entry.path -match '(^|[/\\])\.\.([/\\]|$)' -or $entry.path -match ':'){throw 'unsafe_candidate_manifest_path'}
        $path=Join-Path $script:campaignCandidate $entry.path
        Assert-CampaignPlainPath $path
        if($seen.ContainsKey($entry.path)){throw 'duplicate_manifest_entry'}
        $seen[$entry.path.Replace('/','\')]=$true
        if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256){throw 'candidate_file_hash_mismatch'}
    }
    $queue=[Collections.Generic.Queue[string]]::new(); $queue.Enqueue($script:campaignCandidate); $actualCount=0
    while($queue.Count -gt 0) {
        foreach($item in Get-ChildItem -LiteralPath $queue.Dequeue() -Force) {
            if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'candidate_reparse_entry'}
            if($item.PSIsContainer){$queue.Enqueue($item.FullName);continue}
            if($item.FullName -eq $manifestPath){continue}
            $relative=$item.FullName.Substring($script:campaignCandidate.Length+1)
            if(-not $seen.ContainsKey($relative)){throw 'candidate_unlisted_file'}
            $actualCount++
        }
    }
    if($actualCount -ne 73){throw 'candidate_extra_or_missing_files'}
}
function Assert-CampaignFastSafety {
    $free=if('BoundedScmPressure' -as [type]){[BoundedScmPressure]::FreeMiB()}else{(Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 5).FreePhysicalMemory/1024}
    if([double]::IsNaN($free) -or $free -le 512){throw 'hard_free_memory_floor_512_mib'}
    if($free -lt 768){throw 'soft_free_memory_floor_768_mib'}
    if(([IO.DriveInfo]::new('C:\')).AvailableFreeSpace -lt 20GB){throw 'disk_free_below_20_gib'}
    if(-not $script:campaignLastIdentityCheck -or ([DateTimeOffset]::UtcNow-$script:campaignLastIdentityCheck).TotalSeconds -ge 5) {
        $service=Get-CimInstance Win32_Service -Filter "Name='HostAgent'" -OperationTimeoutSec 5 -ErrorAction Stop
        if($service.State -ne 'Running' -or [int]$service.ProcessId -ne $script:campaignBaseline.pid){throw 'protected_state_or_pid_changed'}
        $script:campaignLastIdentityCheck=[DateTimeOffset]::UtcNow
    }
}
function Assert-CampaignPhase {
    Assert-CampaignVm
    Assert-CampaignArtifacts
    Assert-CampaignFastSafety
    $now=Get-CampaignProtected
    if($now.pid -ne $script:campaignBaseline.pid){throw 'protected_pid_changed'}
}

Assert-CampaignVm
Assert-CampaignArtifacts
$script:campaignBaseline=Get-CampaignProtected
if((Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 5).FreePhysicalMemory/1024 -lt 1024){throw 'initial_free_memory_below_1024_mib'}
if((Get-PSDrive C).Free -lt 20GB){throw 'initial_disk_free_below_20_gib'}
$id=[guid]::NewGuid().ToString('N')
$evidenceRoot=Join-Path $env:LOCALAPPDATA 'HostAgent-DEV-evidence'
if([IO.Path]::GetFullPath($env:LOCALAPPDATA) -ine 'C:\Users\vmdev\AppData\Local'){throw 'unexpected_local_evidence_root'}
$logRoot=Join-Path $evidenceRoot ('campaign-'+$id)
Assert-CampaignPlainPath $logRoot
if(Test-Path -LiteralPath $logRoot){throw 'campaign_path_already_exists'}
$commonLimits=@{initialFreeMiB=1024;abortFreeMiB=768;hardAbortFreeMiB=512;cFreeGiB=20;pollMilliseconds=250;protectedIdentityPollSeconds=5;cleanupGraceSeconds=12;terminationWaitSeconds=5;cimOperationTimeoutSeconds=5;outputCapBytesPerStream=524288}
$plan=@(
    @{id='S0-harness-ready';kind='Auto';limits=$commonLimits;run={
        Add-Type -Path (Join-Path $PSScriptRoot 'BoundedScmPressure.cs')
        return @{ok=$true;memoryPressureEnabled=$false}
    }},
    @{id='S1-isolated-scm-crash';kind='Auto';limits=@{newCrashes=0;newRecoveryStarts=0;maximumEvidenceBytes=524288};run={
        # The latest verified record already proves this gate for r5. Revalidate, do not repeat.
        $subject='3f161b1bbfde4f7e855615b16b9d7b72'
        $resultPath=Join-Path $evidenceRoot ('scm-candidate-'+$subject+'\result.json')
        Assert-CampaignPlainPath $resultPath
        if((Get-Item -LiteralPath $resultPath).Length -gt 524288 -or
            (Get-FileHash -LiteralPath $resultPath -Algorithm SHA256).Hash -ne '09C000F0582BF3B29119C04BF7314BED11B790DA7596D39A5C0FDF24DECC2FA5'){throw 'existing_crash_evidence_missing_or_changed'}
        $result=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        $service=Get-CimInstance Win32_Service -OperationTimeoutSec 5 -Filter ("Name='HostAgentDevScm_"+$subject+"'")
        if(-not (Test-DevCrashProofResult $result $subject $script:campaignCandidateHash) -or
            -not $service -or $service.State -ne 'Stopped' -or $service.ProcessId -ne 0){throw 'existing_crash_success_state_not_confirmed'}
        return @{ok=$true;execution='read_only_revalidation_of_existing_live_run';runId=$subject;resultPath=$resultPath;resultSha256=(Get-FileHash -LiteralPath $resultPath -Algorithm SHA256).Hash;crashRecovery=$result.crash;protectedServiceUnchanged=$result.protectedServiceUnchanged}
    };manualStep='If stored evidence or state differs, stop and investigate read-only; do not silently repeat the crash.'},
    @{id='S1-automatic-scm-recovery';kind='Manual';limits=@{automaticServiceActions=0};reason='Verified controlled crash used controller_start; automatic SCM failure-action recovery is not established.';manualStep='Prepare a separate reviewed GUID service with bounded SCM failure actions and no controller fallback start, then validate new PID and persistence. Do not alter protected HostAgent recovery settings.'},
    @{id='S2-storage-live-faults';kind='Manual';limits=@{maxProposedFixtureMiB=32};reason='No reviewed live storage-failure injector for the current runtime; physical C: exhaustion is prohibited.';manualStep='Prepare a bounded disposable database/volume fixture and validate runtime error/recovery observations; do not fill C: or modify preserved data.'},
    @{id='S2-collector-faults';kind='Manual';limits=@{};reason='Current SCM candidate has real collectors but no private per-collector fault switch.';manualStep='Add and review a test-host-only collector substitution seam; never disable global WMI/EventLog or change OS security.'},
    @{id='S2-remediation-faults';kind='Manual';limits=@{helperCallsAllowed=0};reason='Candidate deliberately uses DenyHelper; no reviewed GUID-isolated real remediation helper exists.';manualStep='Prepare a separate allowlisted helper and harmless GUID test service for bounded failure/timeout/retry; never call the protected helper.'},
    @{id='S3-view-transport-recovery';kind='Auto';limits=@{cycles=1;requestsPerCycle=6;expectedTimeoutsPerCycle=1;serverLifetimeSeconds=60;roundDeadlineSeconds=70;clientDeadlineSeconds=45;serverWorkingSetMiB=256;fixtureMiB=16};run={
        Invoke-DevViewCycles $script:campaignCandidate $evidenceRoot (Join-Path $logRoot 'S3-view-transport-recovery') 1 {Assert-CampaignFastSafety}
    }},
    @{id='S3-bounded-transport-endurance';kind='Auto';limits=@{cycles=3;requestsPerCycle=6;expectedTimeoutsPerCycle=1;roundDeadlineSeconds=70;maximumActiveSeconds=210;serverWorkingSetMiB=256;fixtureMiBPerCycle=16};run={
        Invoke-DevViewCycles $script:campaignCandidate $evidenceRoot (Join-Path $logRoot 'S3-bounded-transport-endurance') 3 {Assert-CampaignFastSafety}
    }},
    @{id='S3-live-wpf-and-full-runtime-endurance';kind='Manual';limits=@{};reason='Transport cycles do not prove live WPF stale/outage behavior or long-running full-runtime leak stability.';manualStep='Prepare a dashboard connected only to a reviewed GUID endpoint and a separately bounded full-runtime fault schedule; observe UI and resource trends.'},
    @{id='S4-physical-512-mib-watchdog';kind='Manual';limits=@{softAbortMiB=768;hardAbortMiB=512;memoryPressureEnabled=$false};reason='Reaching 512 MiB with physical pressure would cross the unchanged 768 MiB safety stop.';manualStep='Do not lower floors or force live memory depletion. A separately reviewed synthetic memory-source boundary test may validate logic, not a physical low-memory run.'},
    @{id='S5-manual-vm-reboot-or-poweroff';kind='Manual';limits=@{guestPowerOperationsAllowed=0};reason='Guest script must never trigger reboot or hard power-off; fresh host-side rollback verification is required.';manualStep='On the VirtualBox host verify HostAgent-DEV identity, snapshot pre chaos snapshot save / dc8af832-31d6-49ac-a8c3-f00d9381597f and clean-win-base preservation. Capture fresh host evidence and agree a disposable persistence fixture before any separate manual power action.'}
)
foreach($phase in $plan){
    $phase.logDirectory=Join-Path $logRoot $phase.id
    if($phase.kind -eq 'Manual'){$phase.limits.automaticActions=0; $phase.limits.executed=$false}
}
if(-not $Execute) {
    [pscustomobject]@{mode='READ_ONLY_PLAN';candidate=$script:campaignCandidate;manifest=$script:campaignCandidateHash;protected=$script:campaignBaseline;limits=$commonLimits;phases=@($plan | ForEach-Object{[pscustomobject]@{id=$_.id;kind=$_.kind;limits=$_.limits;manualStep=$_['manualStep']}})} | ConvertTo-Json -Depth 8
    return
}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if(-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'administrator_token_required'}
Import-Module (Join-Path $PSScriptRoot 'IsolatedScmSafety.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'DevResilienceCampaign.psm1') -Force
New-Item -ItemType Directory -Path $logRoot | Out-Null
foreach($phase in $plan){New-Item -ItemType Directory -Path $phase.logDirectory | Out-Null}
$summaryPath=Join-Path $logRoot 'summary.json'
[pscustomobject]@{campaignRunId=$id;evidenceDirectory=$logRoot;summaryPath=$summaryPath} | ConvertTo-Json
$script:campaignLastIdentityCheck=$null
$save={param($rows)
    $report=[ordered]@{campaignRunId=$id;vm='AGENTPC';account=$identity.Name;candidate=$script:campaignCandidate;candidateManifestSha256=$script:campaignCandidateHash;protectedBaseline=$script:campaignBaseline;commonLimits=$commonLimits;phases=@($rows);evidenceDirectory=$logRoot;updatedUtc=[DateTimeOffset]::UtcNow.ToString('O');overall=$(if(@($rows | Where-Object status -eq 'FAIL').Count){'FAIL'}elseif(@($rows | Where-Object status -eq 'NOT RUN').Count){'INCOMPLETE'}else{'PASS'})}
    [IO.File]::WriteAllText($summaryPath,($report | ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    foreach($row in $rows){
        [IO.File]::WriteAllText((Join-Path $row.logDirectory 'phase.json'),($row | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
    }
}
$rows=Invoke-DevResiliencePlan $plan $id {Assert-CampaignPhase} $save
[pscustomobject]@{campaignRunId=$id;summaryPath=$summaryPath;summarySha256=(Get-FileHash -LiteralPath $summaryPath -Algorithm SHA256).Hash;phases=@($rows | Select-Object id,status,reason,logDirectory)} | ConvertTo-Json -Depth 5
if(@($rows | Where-Object status -eq 'FAIL').Count){throw 'campaign_stopped_after_failure_review_summary'}
