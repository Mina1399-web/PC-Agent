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
        (Get-CimInstance Win32_ComputerSystemProduct -OperationTimeoutSec 5).UUID -ne '46543236-8C5E-49DA-953C-DB17F9B1E402' -or
        (Get-Service VBoxService).Status -ne 'Running' -or
        [Security.Principal.WindowsIdentity]::GetCurrent().Name -ine 'AgentPC\AgentPCdevtest') {throw 'approved_vm_or_account_mismatch'}
    if([IO.Path]::GetFullPath($env:ProgramData) -ine 'C:\ProgramData' -or [IO.Path]::GetFullPath($env:WINDIR) -ine 'C:\Windows'){throw 'unexpected_vm_system_paths'}
}
$script:campaignCandidate=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261017-r5'))
$script:campaignCandidateHash='EBBAD0807899BB8243AE71B2877C4C02B4C3F6024024EE748A179342428CAAAB'
$script:campaignCatalogPath=Join-Path $PSScriptRoot 'remaining-resilience-manifest.json'
$script:campaignCatalogHash='8D16115F74017C33DBFBA5CF94D594E60EE02A53D00A9A0A71ACDF713CFD22C6'
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
        $pythonPath='C:\Users\AgentPCdevtest\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
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

$bundle=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\HostAgent-DEV-SCM-candidates\resilience-test-fixtures-20261017-r2'))
$bundleManifestHash='ECC5B168850D7B10A1F6A0156508D38DDE467D71CA0411C27B92D221D3B43514'
$sdk='C:\Users\AgentPCdevtest\AppData\Local\HostAgent-dev-sdk\10.0.401\dotnet.exe'
function Assert-RemainingBundle {
    Assert-CampaignPlainPath $bundle
    $manifestPath=Join-Path $bundle 'fixture-manifest.json'
    if((Get-FileHash -LiteralPath $manifestPath).Hash -ne $bundleManifestHash){throw 'fixture_bundle_manifest_mismatch'}
    $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if($manifest.format -ne 1 -or $manifest.files.Count -lt 100 -or $manifest.files.Count -gt 2000){throw 'invalid_fixture_inventory'}
    $seen=@{}
    foreach($entry in $manifest.files){
        if($entry.path -notmatch '^(infrastructure|desktop)[/\\]' -or $entry.path -match '(^|[/\\])\.\.([/\\]|$)|:' -or $seen.ContainsKey($entry.path)){throw 'unsafe_fixture_path'}
        $path=Join-Path $bundle $entry.path;Assert-CampaignPlainPath $path
        if((Get-FileHash -LiteralPath $path).Hash -ne $entry.sha256){throw 'fixture_file_hash_mismatch'}
        $seen[$entry.path.Replace('/','\')]=$true
    }
    $queue=[Collections.Generic.Queue[string]]::new();$queue.Enqueue($bundle);$count=0
    while($queue.Count){foreach($item in Get-ChildItem -LiteralPath $queue.Dequeue() -Force){
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'fixture_reparse_point'}
        if($item.PSIsContainer){$queue.Enqueue($item.FullName);continue}
        if($item.FullName -eq $manifestPath){continue}
        if(-not $seen.ContainsKey($item.FullName.Substring($bundle.Length+1))){throw 'unlisted_fixture_file'}
        $count++
    }}
    if($count -ne $manifest.files.Count){throw 'fixture_inventory_changed'}
    Assert-CampaignPlainPath $sdk
    if((Get-FileHash -LiteralPath $sdk).Hash -ne '21A46F1E5235CF4E844B9DE5429F0E198B9C97A41F0503A66442F1D639CA3EE6'){throw 'sdk_host_hash_mismatch'}
}
$script:remainingSafetyLatched=$false
$script:remainingFootprint=$null
function Assert-RemainingFast {
    if($script:remainingSafetyLatched){throw 'safety_abort_latched'}
    try {
        Assert-CampaignFastSafety
        if($script:remainingFootprint -and (Test-Path -LiteralPath $script:remainingFootprint)){
            Assert-CampaignPlainPath $script:remainingFootprint
            $files=@(Get-ChildItem -LiteralPath $script:remainingFootprint -Force -Recurse)
            if(@($files | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0){throw 'runtime_reparse_point'}
            if($files.Count -gt 64 -or ($files | Measure-Object Length -Sum).Sum -gt 32MB){throw 'runtime_fixture_over_32_mib_or_64_entries'}
        }
    } catch {$script:remainingSafetyLatched=$true;throw}
}
function Assert-RemainingPhase {
    try {Assert-CampaignPhase;Assert-RemainingBundle;Assert-RemainingFast}
    catch {$script:remainingSafetyLatched=$true;throw}
}
function Get-RemainingChildIds {
    @(Get-CimInstance Win32_Process -OperationTimeoutSec 5 -Filter "Name='dotnet.exe' OR Name='testhost.exe' OR Name='HostAgent.PipeProbe.exe'" | ForEach-Object {[int]$_.ProcessId})
}
function Confirm-RemainingQuiescent($Phase) {
    # Resource aborts stay latched. This cleanup may only stop this new GUID service.
    $fallback=$false
    if($Phase.id -eq 'S1-automatic-scm-recovery') {
        $service=Get-CimInstance Win32_Service -OperationTimeoutSec 5 -Filter ("Name='HostAgentDevScm_"+$autoId+"'")
        if($service){
            Assert-CampaignVm
            if((Get-CampaignProtected).pid -ne $script:campaignBaseline.pid){throw 'protected_changed_before_cleanup'}
            $image='C:\ProgramData\HostAgent-DEV-SCM-'+$autoId+'\app\HostAgent.PipeProbe.exe'
            Assert-CampaignPlainPath $image
            $expected='"'+$image+'" isolated-scm-service '+$autoId+' '+$identity.User.Value+' crash-proof'
            if($service.PathName -cne $expected -or $service.StartName -cne ('NT SERVICE\HostAgentDevScm_'+$autoId) -or
               $service.StartMode -notin @('Manual','Disabled') -or
               (Get-FileHash -LiteralPath $image).Hash -ne '6E270A54D2FC3B6E5B69DEEECA9090203BBF8D3BB032FD345CDAA68B2106E39D'){throw 'isolated_cleanup_identity_mismatch'}
            $fallback=$false
            if($service.StartMode -ne 'Disabled' -or -not [IsolatedScmRecovery]::IsDisabled($autoId) -or $service.State -ne 'Stopped'){
                [IsolatedScmRecovery]::Set($autoId,$false)
                $fallback=$true
                $controller=[ServiceProcess.ServiceController]::new($service.Name)
                try{if($controller.Status -ne 'Stopped'){$controller.Stop();$controller.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(15))}}
                finally{$controller.Dispose()}
            }
            $now=Get-CimInstance Win32_Service -OperationTimeoutSec 5 -Filter ("Name='"+$service.Name+"'")
            if($now.State -ne 'Stopped' -or $now.ProcessId -ne 0 -or $now.StartMode -ne 'Disabled' -or -not [IsolatedScmRecovery]::IsDisabled($autoId)){throw 'isolated_quiescence_not_proved'}
            [IO.File]::WriteAllText((Join-Path $Phase.logDirectory 'quiescence.json'),(@{service=$now.Name;state=$now.State;pid=0;startMode='Disabled';failureActions='none';controllerCleanupStopUsed=$fallback;dataPreserved=$true}|ConvertTo-Json))
            # A cleanup stop cannot turn the failed scenario into PASS.
        }
    }
    $wait=[Diagnostics.Stopwatch]::StartNew()
    do{
        $new=@(Get-RemainingChildIds | Where-Object {$_ -notin $script:remainingInitialChildren})
        if($new.Count){Start-Sleep -Milliseconds 250}
    }while($new.Count -and $wait.Elapsed.TotalSeconds -lt 5)
    if($new.Count){throw 'new_child_process_still_running'}
    return @{scenarioFailed=$fallback;safe=$true}
}
function Invoke-RemainingFixture([string]$Scenario,[string]$PhaseId,[int]$Minimum) {
    $logs=Join-Path $logRoot $PhaseId
    $child=Invoke-DevBoundedProcess $powershell @('-NoProfile','-NonInteractive','-File',(Join-Path $PSScriptRoot 'Invoke-RemainingFixtureTests.ps1'),'-Scenario',$Scenario,'-Bundle',$bundle,'-LogDirectory',$logs) 180 $logs {Assert-RemainingFast}
    $test=Read-RemainingTrx (Join-Path $logs 'tests.trx') $Minimum
    return @{ok=($child.exitCode -eq 0 -and $test.ok);scope='fixture_tests_only';native=$child;tests=$test;livePrivilegedRemediationVerified=$false}
}
function Invoke-RemainingAutoScm {
    $logs=Join-Path $logRoot 'S1-automatic-scm-recovery'
    $child=Invoke-DevBoundedProcess $powershell @('-NoProfile','-NonInteractive','-File',(Join-Path $PSScriptRoot 'run-isolated-scm-auto-candidate.ps1'),'-RunId',$autoId,'-Execute') 600 $logs {Assert-RemainingFast}
    $path=Join-Path $evidenceRoot ('scm-candidate-'+$autoId+'\result.json')
    Assert-CampaignPlainPath $path
    if((Get-Item -LiteralPath $path).Length -gt 524288){throw 'scm_result_oversized'}
    $result=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if($result.failureCode -match 'memory_floor_breached' -or $result.failureReason -match 'free_memory_floor|c_free_floor' -or
       $result.memoryGuard -eq 'breach_marker_unreadable' -or $result.memoryGuard -isnot [string]){$script:remainingSafetyLatched=$true}
    $ok=Test-RemainingAutoScmResult $result $autoId $script:campaignCandidateHash
    return @{ok=($child.exitCode -eq 0 -and $ok);resultPath=$path;resultSha256=(Get-FileHash -LiteralPath $path).Hash;native=$child;result=$result}
}
function Invoke-RemainingRuntime {
    $logs=Join-Path $logRoot 'S3-private-runtime-endurance'
    $script:remainingFootprint=Join-Path $evidenceRoot ('runtime-candidate-'+$runtimeId)
    if(Test-Path -LiteralPath $script:remainingFootprint){throw 'runtime_fixture_already_exists'}
    try {
        $child=Invoke-DevBoundedProcess (Join-Path $bundle 'infrastructure\HostAgent.PipeProbe.exe') @('isolated-runtime',$runtimeId,'resilience') 240 $logs {Assert-RemainingFast}
        $last=Get-Content -LiteralPath $child.stdoutPath | Select-Object -Last 1 | ConvertFrom-Json
        if($last.aborted -eq $true -or $last.minimumFreeMiB -lt 768){$script:remainingSafetyLatched=$true;throw 'runtime_safety_abort_reported'}
        $healthy=($last.finished -eq $true -and $last.aborted -eq $false -and $last.failures -eq 0 -and $last.helperCalls -eq 0 -and
            $last.injectedCollectorFailures -eq 2 -and $last.collectorRecoveries -eq 2 -and $last.views -ge 10 -and $last.cpuSamples -ge 3)
        $checks=Join-Path $logs 'readonly-database-check';New-Item -ItemType Directory $checks | Out-Null
        $python='C:\Users\AgentPCdevtest\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
        $readLocks=@()
        try{
            foreach($name in @('fixture.db','notices.db')){$readLocks += [IO.File]::Open((Join-Path $script:remainingFootprint $name),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)}
            $dbChild=Invoke-DevBoundedProcess $python @('-I','-B',(Join-Path $PSScriptRoot 'check-remaining-runtime.py'),$runtimeId) 25 $checks {Assert-RemainingFast}
        }finally{foreach($handle in $readLocks){$handle.Dispose()}}
        $check=Get-Content -LiteralPath $dbChild.stdoutPath -Raw | ConvertFrom-Json
        return @{ok=($child.exitCode -eq 0 -and $healthy -and $dbChild.exitCode -eq 0 -and $check.ok -eq $true);scope='private_full_runtime_190_seconds_two_collector_faults';fixtureId=$runtimeId;runtime=$last;native=$child;databaseQuickChecks=$check;longTermLeakQualification=$false}
    }finally{$script:remainingFootprint=$null}
}

Assert-CampaignVm;Assert-CampaignArtifacts;Assert-RemainingBundle
$script:campaignBaseline=Get-CampaignProtected
if((Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 5).FreePhysicalMemory/1024 -lt 1024){throw 'initial_free_memory_below_1024_mib'}
if(([IO.DriveInfo]::new('C:\')).AvailableFreeSpace -lt 20GB){throw 'initial_disk_free_below_20_gib'}
if([IO.Path]::GetFullPath($env:LOCALAPPDATA) -ine 'C:\Users\AgentPCdevtest\AppData\Local'){throw 'unexpected_local_evidence_root'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$id=[guid]::NewGuid().ToString('N');$autoId=[guid]::NewGuid().ToString('N');$runtimeId=[guid]::NewGuid().ToString('N')
$evidenceRoot=Join-Path $env:LOCALAPPDATA 'HostAgent-DEV-evidence'
$logRoot=Join-Path $evidenceRoot ('remaining-campaign-'+$id)
Assert-CampaignPlainPath $logRoot
if(Test-Path -LiteralPath $logRoot){throw 'campaign_path_exists'}
$powershell='C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
$commonLimits=@{initialFreeMiB=1024;abortFreeMiB=768;hardAbortFreeMiB=512;diskFreeGiB=20;pollMilliseconds=250;noPhysicalPressure=$true;continueOnScenarioFailure=$true;continueOnSafetyFailure=$false;oldEvidenceDeleted=$false}
$plan=@(
 @{id='S1-automatic-scm-recovery';kind='Auto';scope='live_new_GUID_SCM_only';limits=@{maximumAutoRestarts=1;restartDelayMilliseconds=5000;deadlineSeconds=600};run={Invoke-RemainingAutoScm}},
 @{id='S2-storage-fixtures';kind='Auto';scope='real_SQLite_fixtures_not_live_storage_failure';limits=@{deadlineSeconds=180;physicalDiskFill=$false};run={Invoke-RemainingFixture storage S2-storage-fixtures 3}},
 @{id='S2-collector-contracts';kind='Auto';scope='real_coordinator_injected_collectors';limits=@{deadlineSeconds=180;osServicesModified=$false};run={Invoke-RemainingFixture collector S2-collector-contracts 4}},
 @{id='S2-remediation-contracts';kind='Auto';scope='real_dispatcher_journal_fake_executor';limits=@{deadlineSeconds=180;realHelperCalls=0};run={Invoke-RemainingFixture remediation S2-remediation-contracts 10}},
 @{id='S3-WPF-outage-recovery';kind='Auto';scope='real_WPF_synthetic_transport_three_cycles';limits=@{deadlineSeconds=180;realPCOperations=0};run={Invoke-RemainingFixture wpf S3-WPF-outage-recovery 1}},
 @{id='S3-private-runtime-endurance';kind='Auto';scope='real_collectors_SQLite_private_View_injected_CPU_collector';limits=@{activeSeconds=190;deadlineSeconds=240;fixtureMiB=32;fixtureEntries=64;injectedCollectorFailures=2;minimumRecoveries=2;realHelperCalls=0};run={Invoke-RemainingRuntime}},
 @{id='S2-live-storage-and-real-helper';kind='Manual';scope='not_proved_by_fixture_tests';limits=@{realHelperCalls=0};reason='live_runtime_storage_failure_and_real_privileged_remediation_not_automated';manualStep='Review a bounded disposable store/volume injector and GUID allowlisted helper separately. Fixture PASS does not close these live gates.'},
 @{id='S3-live-WPF-private-IPC';kind='Manual';scope='not_proved_by_synthetic_WPF_transport';limits=@{productionEndpoints=0};reason='combined_WPF_to_GUID_pipe_outage_not_automated';manualStep='Prepare a reviewed test-only WPF bootstrap connected exclusively to the private GUID pipe. WPF synthetic-transport coverage and runtime View coverage are separate proofs.'},
 @{id='S3-long-duration-endurance';kind='Manual';scope='190_seconds_is_not_long_term_qualification';limits=@{};reason='long_duration_leak_stability_not_proved';manualStep='Review trends from the short private runtime gate before a separately bounded longer run.'},
 @{id='S4-physical-512-boundary';kind='Manual';scope='physical_pressure_prohibited';limits=@{softAbortMiB=768;hardAbortMiB=512};reason='physical_512_test_would_cross_768_stop';manualStep='Keep thresholds; only existing synthetic logic coverage applies. No physical allocation test.'},
 @{id='S5-manual-vm-power';kind='Manual';scope='host_side_manual_gate';limits=@{guestPowerOperations=0};reason='fresh_host_rollback_verification_required';manualStep='Verify VM UUID and snapshot dc8af832-31d6-49ac-a8c3-f00d9381597f on VirtualBox host, preserve clean-win-base, then separately agree disposable persistence fixture and manual power action.'}
)
foreach($p in $plan){$p.logDirectory=Join-Path $logRoot $p.id}
if(-not $Execute){
 [pscustomobject]@{mode='READ_ONLY_PREFLIGHT';freeMemoryMiB=((Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 5).FreePhysicalMemory/1024);cFreeGiB=(([IO.DriveInfo]::new('C:\')).AvailableFreeSpace/1GB);candidate=$script:campaignCandidate;candidateManifestSha256=$script:campaignCandidateHash;bundle=$bundle;bundleManifestSha256=$bundleManifestHash;protected=$script:campaignBaseline;limits=$commonLimits;phases=@($plan|ForEach-Object{[pscustomobject]@{id=$_.id;kind=$_.kind;scope=$_.scope;limits=$_.limits;manualStep=$_['manualStep']}})}|ConvertTo-Json -Depth 8
 return
}
if(-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'administrator_token_required'}
$env:DOTNET_ROOT=Split-Path -Parent $sdk
Import-Module (Join-Path $PSScriptRoot 'IsolatedScmSafety.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'DevResilienceCampaign.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'RemainingResilience.psm1') -Force
Add-Type -Path (Join-Path $PSScriptRoot 'BoundedScmPressure.cs')
Add-Type -Path (Join-Path $PSScriptRoot 'IsolatedScmRecovery.cs')
$script:remainingInitialChildren=@(Get-RemainingChildIds)
New-Item -ItemType Directory -Path $logRoot | Out-Null
foreach($p in $plan){New-Item -ItemType Directory -Path $p.logDirectory|Out-Null}
$summaryPath=Join-Path $logRoot 'summary.json'
[pscustomobject]@{campaignRunId=$id;summaryPath=$summaryPath;evidenceDirectory=$logRoot}|ConvertTo-Json
$save={param($rows)
 $report=@{campaignRunId=$id;vm='AGENTPC';account=$identity.Name;protectedBaseline=$script:campaignBaseline;limits=$commonLimits;scmCandidateManifest=$script:campaignCandidateHash;testBundleManifest=$bundleManifestHash;autoScmRunId=$autoId;runtimeRunId=$runtimeId;phases=@($rows);safetyAbort=$script:remainingSafetyLatched;overall=$(if(@($rows|Where-Object status -eq FAIL).Count){'FAIL'}elseif(@($rows|Where-Object status -eq 'NOT RUN').Count){'INCOMPLETE'}else{'PASS'});updatedUtc=[DateTimeOffset]::UtcNow.ToString('O')}
 [IO.File]::WriteAllText($summaryPath,($report|ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))
 foreach($row in $rows){[IO.File]::WriteAllText((Join-Path $row.logDirectory 'phase.json'),($row|ConvertTo-Json -Depth 14),[Text.UTF8Encoding]::new($false))}
}
try{$rows=Invoke-RemainingResiliencePlan $plan $id {Assert-RemainingPhase} {param($p) Confirm-RemainingQuiescent $p} $save}
finally{Confirm-RemainingQuiescent $plan[0] | Out-Null}
[pscustomobject]@{campaignRunId=$id;summaryPath=$summaryPath;summarySha256=(Get-FileHash -LiteralPath $summaryPath).Hash;phases=@($rows|Select-Object id,status,reason,scope,logDirectory)}|ConvertTo-Json -Depth 6
if(@($rows|Where-Object status -eq FAIL).Count){exit 1}

