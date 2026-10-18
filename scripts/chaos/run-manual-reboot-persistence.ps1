param(
    [ValidateSet('Preflight','Prepare','Verify')][string]$Stage = 'Preflight',
    [switch]$Execute,
    [ValidatePattern('^[a-f0-9]{32}$')][string]$RunId = 'c60759c492a840f7b993afc807e7339a'
)
$ErrorActionPreference = 'Stop'
$Stage = switch($Stage.ToLowerInvariant()){'preflight'{'Preflight'}'prepare'{'Prepare'}'verify'{'Verify'}}
if($Execute-and$Stage-cne'Preflight'-and-not[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'administrator_token_required'}
# Operator-only S5. This script has no guest or host power operation and deletes nothing.
$taskId = $RunId
if ($taskId -eq '00000000000000000000000000000000') { throw 'reboot_empty_guid' }
$taskCandidatePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261018-reboot-r9'))
$taskExpectedManifestSha256 = 'AA1F5AAD8F4EC4C4EA70CBF88882E55588F7DE19F04E62B10FA95B2D7A569366'
$taskCatalogHash = '78BA190BD36AA355CEAD38C9B9BCC89C5824A8A33CE5104B8B525E1A8E9BB66E'
$taskBaseScriptHash = 'E2D003A07C9B5786A3BC7684E8F19174562A7C590C54181557656D31D34BCA33'
$taskEvidenceDirectory = Join-Path $env:LOCALAPPDATA ('HostAgent-DEV-evidence\reboot-' + $taskId)
$taskPackageRoot = Join-Path $env:ProgramData ('HostAgent-DEV-SCM-' + $taskId)
$taskAppDirectory = Join-Path $taskPackageRoot 'app'
$taskDataDirectory = Join-Path $taskPackageRoot 'data'
$taskServiceName = 'HostAgentDevScm_' + $taskId
$taskServiceExe = Join-Path $taskAppDirectory 'HostAgent.PipeProbe.exe'
$taskPipeName = 'HostAgent.Dev.Scm.View.' + $taskId
$taskMemoryMarkerPath = Join-Path $taskDataDirectory 'reboot-safety-abort.json'
$taskMemoryWatchdog = $null
$taskProtectedPath = 'C:\Program Files\HostAgent\Service\HostAgent.Service.exe'
$taskProtectedSha256 = '04DF669D662BB08BDBD76653E46158E3D701D7E0D48653439F8F836BB417CB87'
$taskExpectedProtectedAssemblyHashes = @{
    'HostAgent.Core.dll'='E7F156748E61AB8AEBE49B80E2B9A379EAC4588559EA55BF0B806D5229100080'
    'HostAgent.Infrastructure.dll'='B86C9673CBDE1EDC755B1AD8FFA498373517EF1E3A2CD673261970931594A8AE'
    'HostAgent.Service.dll'='19772D28E83DB3704419F2C6EB1450F3FF51BD1FCD8ED2B9D63A8A32695CA840'
}
$taskPython = 'C:\Users\AgentPCdevtest\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
$taskRuntime = 'C:\Program Files\dotnet'
$taskPlanPath = Join-Path $taskEvidenceDirectory 'prepared.json'
$taskRunResult = [ordered]@{scenario='manual-normal-vm-reboot-persistence';stage=$Stage;runId=$taskId;outcome='NOT RUN';candidateManifestSha256=$taskExpectedManifestSha256;scmOperations=@();viewStages=@();databaseChecks=@();cleanup='not_needed';failureCode=$null}

# Load only reviewed function definitions, never the old runner's executable body.
$taskBaseScript = Join-Path $PSScriptRoot 'run-isolated-scm-candidate.ps1'
for($parent=[IO.DirectoryInfo]::new($PSScriptRoot);$parent;$parent=$parent.Parent){if($parent.Attributes-band[IO.FileAttributes]::ReparsePoint){throw 'reboot_harness_reparse'}}
if((Get-Item -LiteralPath $taskBaseScript).Attributes-band[IO.FileAttributes]::ReparsePoint){throw 'reboot_harness_reparse'}
if ((Get-FileHash -LiteralPath $taskBaseScript -Algorithm SHA256).Hash -cne $taskBaseScriptHash) { throw 'reboot_base_harness_changed' }
$taskTokens=$null; $taskParseErrors=$null
$taskAst = [Management.Automation.Language.Parser]::ParseFile($taskBaseScript,[ref]$taskTokens,[ref]$taskParseErrors)
if ($taskParseErrors.Count) { throw 'reboot_base_harness_parse_failed' }
foreach ($name in @('Get-TaskFreeMiB','Get-TaskCFreeBytes','Assert-TaskVm','Assert-TaskPlainTree','Assert-TaskNoReparseAncestors','Get-TaskProtectedService','Assert-TaskProtectedService','Set-TaskDirectoryAcl','Set-TaskFileAcl','Assert-TaskAclCanaries')) {
    $definition = @($taskAst.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name},$false))
    if ($definition.Count -ne 1) { throw 'reboot_harness_function_missing' }
    . ([scriptblock]::Create($definition[0].Extent.Text))
}
$taskCatalogPath=Join-Path $PSScriptRoot 'reboot-resilience-manifest.json'
Assert-TaskNoReparseAncestors $taskCatalogPath
if((Get-FileHash -LiteralPath $taskCatalogPath -Algorithm SHA256).Hash-cne$taskCatalogHash){throw 'reboot_catalog_mismatch'}
$taskCatalog=Get-Content -LiteralPath $taskCatalogPath -Raw|ConvertFrom-Json
foreach($entry in $taskCatalog.files){$path=Join-Path $PSScriptRoot $entry.path;Assert-TaskNoReparseAncestors $path;if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash-cne$entry.sha256){throw 'reboot_harness_artifact_mismatch'}}
Import-Module (Join-Path $PSScriptRoot 'IsolatedScmSafety.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'RebootPersistence.psm1') -Force

function Assert-RebootEnvironment {
    # Establish identity/owned cleanup eligibility before a resource rejection.
    # A 900MiB reading must not strand an already prepared Auto service.
    if ($env:COMPUTERNAME -cne 'AGENTPC' -or (Get-CimInstance Win32_ComputerSystem -OperationTimeoutSec 5).Model -cne 'VirtualBox' -or
        (Get-Service VBoxService).Status -ne 'Running' -or [Security.Principal.WindowsIdentity]::GetCurrent().Name -ine 'AgentPC\AgentPCdevtest' -or
        (Get-CimInstance Win32_ComputerSystemProduct -OperationTimeoutSec 5).UUID -ine '46543236-8C5E-49DA-953C-DB17F9B1E402' -or
        [IO.Path]::GetFullPath($env:ProgramData) -ine 'C:\ProgramData' -or [IO.Path]::GetFullPath($env:LOCALAPPDATA) -ine 'C:\Users\AgentPCdevtest\AppData\Local') { throw 'reboot_vm_or_account_mismatch' }
    if($Execute-and$Stage-ceq'Verify'){
        Assert-RebootPlainPath $taskPlanPath
        if(-not(Test-Path -LiteralPath $taskPlanPath -PathType Leaf)-or(Get-Item -LiteralPath $taskPlanPath).Length-gt65536){throw 'reboot_preparation_record_missing_or_large'}
        $owner=Get-Content -LiteralPath $taskPlanPath -Raw|ConvertFrom-Json
        if($owner.runId-cne$taskId-or$owner.candidateManifestSha256-cne$taskExpectedManifestSha256-or$owner.catalogSha256-cne$taskCatalogHash-or$owner.runnerSha256-cne(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash){throw 'reboot_preparation_identity_mismatch'}
        $registration=Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'" -OperationTimeoutSec 5
        $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $image='"'+$taskServiceExe+'" isolated-scm-service '+$taskId+' '+$sid+' reboot-proof'
        Assert-RebootServiceIdentity $registration $taskId $image @('Auto','Disabled','Manual')
        $script:taskOwnCreated=$true
        $script:taskExpectedImage=$image
    }
    if((Get-TaskFreeMiB)-lt1024){throw 'preflight_free_memory_below_1024_mib'}
    if((Get-TaskCFreeBytes)-lt20GB){throw 'preflight_c_free_below_20_gib'}
    foreach ($path in @($taskCandidatePath,$taskPackageRoot,$taskEvidenceDirectory,$taskPython,$taskBaseScript,$PSScriptRoot)) { Assert-RebootPlainPath $path }
    if ((Get-FileHash -LiteralPath (Join-Path $taskRuntime 'dotnet.exe') -Algorithm SHA256).Hash -cne 'ABCE6623A6729059CF39BB0F728C4A7804F625895099C88C17E6E704661E09EB') { throw 'reboot_runtime_mismatch' }
    if (-not (Test-Path -LiteralPath $taskPython -PathType Leaf) -or (Get-FileHash -LiteralPath $taskPython -Algorithm SHA256).Hash -cne 'B07B03B9BA866A341A3C13EE69710FBC8D43D4CE2523DEE13F6DF5C25378D15B') { throw 'reboot_python_identity_mismatch' }
    Assert-RebootManifest $taskCandidatePath $taskExpectedManifestSha256
    $catalogPath=Join-Path $PSScriptRoot 'reboot-resilience-manifest.json'
    if ((Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash -cne $taskCatalogHash) { throw 'reboot_catalog_mismatch' }
    $catalog=Get-Content -LiteralPath $catalogPath -Raw|ConvertFrom-Json
    foreach ($entry in $catalog.files) {
        $path=Join-Path $PSScriptRoot $entry.path; Assert-RebootPlainPath $path
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $entry.sha256) { throw 'reboot_harness_artifact_mismatch' }
    }
}
function Get-RebootHelper {
    $service=Get-CimInstance Win32_Service -Filter "Name='HostAgent.Helper'" -OperationTimeoutSec 5
    $image='C:\Program Files\HostAgent\Helper\HostAgent.Helper.exe'
    $hash=(Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash
    if (-not $service -or $service.State -cne 'Running' -or $service.StartMode -cne 'Auto' -or $service.StartName -cne 'LocalSystem' -or $service.PathName -cne ('"'+$image+'"') -or $hash -cne 'DB326B08693D3C8F89FDE68E66B398A08D172872669CDBF5761214D9E3E2D1CF') { throw 'reboot_protected_helper_mismatch' }
    return [pscustomobject]@{Name=$service.Name;State=$service.State;StartMode=$service.StartMode;StartName=$service.StartName;PathName=$service.PathName;ImageSha256=$hash;ProcessId=[int]$service.ProcessId;AssemblyHashes=[pscustomobject]@{}}
}
function Assert-RebootSafety {
    if ($script:taskMonitor) {
        if($script:taskMonitor.FailureCode){throw $script:taskMonitor.FailureCode}
    }else{
        $free=Get-TaskFreeMiB
        if($free-le512){throw 'hard_free_memory_floor_512_mib'}
        if($free-lt768-or(Get-TaskCFreeBytes)-lt20GB){throw 'reboot_resource_floor'}
    }
    if (Test-Path -LiteralPath $taskMemoryMarkerPath) { throw 'reboot_service_safety_abort' }
}
function Assert-RebootWithinPhase {
    $agent=Get-TaskProtectedService;$helper=Get-RebootHelper
    if($agent.ProcessId-ne$ProtectedBaseline.ProcessId-or$helper.ProcessId-ne$taskHelperBaseline.ProcessId){throw 'reboot_protected_process_changed_within_phase'}
    Assert-RebootProtectedContinuity ($ProtectedBaseline|ConvertTo-Json -Depth 4|ConvertFrom-Json) $agent
    Assert-RebootProtectedContinuity ($taskHelperBaseline|ConvertTo-Json -Depth 4|ConvertFrom-Json) $helper
}
function Get-RebootService([string[]]$Modes=@('Auto','Manual','Disabled')) {
    $service=Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'" -OperationTimeoutSec 5
    Assert-RebootServiceIdentity $service $taskId $script:taskExpectedImage $Modes
    $required=@((Get-Service -Name $taskServiceName).RequiredServices|ForEach-Object Name)
    if ($required.Count -ne 1 -or $required[0] -cne 'VBoxService') { throw 'reboot_dependency_mismatch' }
    return $service
}
function Invoke-RebootSc([string[]]$Arguments) {
    Assert-RebootSafety
    if ($Arguments.Count -lt 2 -or $Arguments[1] -cne $taskServiceName -or $Arguments[0] -notin @('create','config','start','stop')) { throw 'reboot_scm_scope_rejected' }
    if ($Arguments[0] -ne 'create') { Get-RebootService | Out-Null }
    $info=[Diagnostics.ProcessStartInfo]::new(); $info.FileName=Join-Path $env:WINDIR 'System32\sc.exe'
    $info.Arguments=ConvertTo-IsolatedScmNativeCommandLine $Arguments; $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $process=[Diagnostics.Process]::Start($info); $out=$process.StandardOutput.ReadToEndAsync(); $err=$process.StandardError.ReadToEndAsync()
    try {
        $clock=[Diagnostics.Stopwatch]::StartNew()
        while (-not $process.HasExited) { Assert-RebootSafety; if($clock.Elapsed.TotalSeconds-ge15){throw 'reboot_native_deadline'}; Start-Sleep -Milliseconds 100 }
        $process.WaitForExit(); [void]$out.GetAwaiter().GetResult(); [void]$err.GetAwaiter().GetResult()
        $taskRunResult.scmOperations += [pscustomobject]@{operation=$Arguments[0];exitCode=$process.ExitCode}
        if($process.ExitCode-ne0){throw ('reboot_native_'+$Arguments[0]+'_failed')}
    } finally {if(-not$process.HasExited){$process.Kill();[void]$process.WaitForExit(5000)};$process.Dispose()}
}
function Wait-RebootService([string]$State,[int]$Seconds) {
    $clock=[Diagnostics.Stopwatch]::StartNew()
    do { Assert-RebootSafety; $service=Get-RebootService; if($service.State-ceq$State){return $service};Start-Sleep -Milliseconds 500 } while($clock.Elapsed.TotalSeconds-lt$Seconds)
    throw ('reboot_service_deadline_'+$State.ToLowerInvariant())
}
function Read-RebootMarker([string]$Name) {
    $path=Join-Path $taskDataDirectory $Name;Assert-RebootPlainPath $path
    if (-not(Test-Path -LiteralPath $path -PathType Leaf)-or(Get-Item -LiteralPath $path).Length-gt16384){throw 'reboot_marker_missing_or_large'}
    return (Get-Content -LiteralPath $path -Raw|ConvertFrom-Json)
}
function Invoke-RebootProbe([string]$Mode,[int]$DeadlineSeconds=40) {
    Assert-RebootSafety; $service=Get-RebootService @('Auto')
    $probeArgs=@(('isolated-scm-'+$Mode),$taskId);if($Mode-ceq'view'){$probeArgs+= $script:taskServiceSid.Value}
    $text=Invoke-RebootChild $taskServiceExe $probeArgs $DeadlineSeconds
    $rows=@($text -split '\r?\n'|Where-Object {$_})
    if(-not$rows.Count){throw ('reboot_'+$Mode+'_probe_failed')}
    $item=$rows[-1]|ConvertFrom-Json
    if($Mode-ceq'view' -and $item.ok-ne$true){throw 'reboot_view_unavailable'}
    if($Mode-ceq'dbcheck' -and ($item.healthy-ne$true-or$item.quickCheck-cne'ok')){throw 'reboot_database_unhealthy'}
    Assert-RebootSafety;return $item
}
function Wait-RebootDatabase([int]$MinimumSnapshots,[int]$Seconds=90) {
    $clock=[Diagnostics.Stopwatch]::StartNew()
    do {
        Assert-RebootSafety
        if((Get-RebootService @('Auto')).State-cne'Running'){throw 'reboot_runtime_not_running'}
        $remaining=[int][math]::Ceiling($Seconds-$clock.Elapsed.TotalSeconds)
        if($remaining-lt1){throw 'reboot_database_readiness_deadline'}
        try {
            $database=Invoke-RebootProbe 'dbcheck' ([math]::Min(40,$remaining))
            if($database.snapshotCount-ge$MinimumSnapshots){return $database}
        }catch{
            # Exit1 is a bounded not-yet-initialized/unhealthy read; other failures stay fail-fast.
            if($_.Exception.Message-cne'reboot_child_failed'-or$taskRunResult.childExitCode-ne1){throw}
        }
        if($clock.Elapsed.TotalSeconds-ge$Seconds){throw 'reboot_database_readiness_deadline'}
        Start-Sleep -Milliseconds 250
    }while($true)
}
function Invoke-RebootChild([string]$File,[string[]]$Arguments,[int]$Seconds,[string]$Code=$null) {
    $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=$File;$info.Arguments=ConvertTo-IsolatedScmNativeCommandLine $Arguments
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.RedirectStandardInput= -not[string]::IsNullOrEmpty($Code)
    $child=[Diagnostics.Process]::Start($info);$out=$child.StandardOutput.ReadToEndAsync();$err=$child.StandardError.ReadToEndAsync();$writing=$null;$inputClosed=$false
    if($info.RedirectStandardInput){$writing=$child.StandardInput.WriteAsync($Code)}
    try{
        $clock=[Diagnostics.Stopwatch]::StartNew()
        while(-not$child.HasExited){
            Assert-RebootSafety
            if($writing-and$writing.IsCompleted-and-not$inputClosed){[void]$writing.GetAwaiter().GetResult();$child.StandardInput.Close();$inputClosed=$true}
            if($clock.Elapsed.TotalSeconds-ge$Seconds){throw 'reboot_child_deadline'}
            Start-Sleep -Milliseconds 250
        }
        $child.WaitForExit();$text=$out.GetAwaiter().GetResult();[void]$err.GetAwaiter().GetResult()
        $taskRunResult.childExitCode=$child.ExitCode
        if($child.ExitCode-ne0){throw 'reboot_child_failed'}
        if($text.Length-gt65536){throw 'reboot_child_output_limit'}
        return $text.Trim()
    }finally{if(-not$child.HasExited){$child.Kill();[void]$child.WaitForExit(5000)};$child.Dispose()}
}
function Stop-RebootFixture {
    # Cleanup must also work before dependency setup and below the resource floor.
    # It still requires the exact planned GUID/account/image before every control.
    $service=Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'" -OperationTimeoutSec 5
    Assert-RebootServiceIdentity $service $taskId $taskExpectedImage @('Auto','Manual','Disabled')
    $ownProcessId=[int]$service.ProcessId
    if(-not('IsolatedScmRecovery'-as[type])){Add-Type -Path (Join-Path $PSScriptRoot 'IsolatedScmRecovery.cs')}
    [IsolatedScmRecovery]::Set($taskId,$false) # Exact GUID only: recovery none + startup disabled.
    if($service.State-cne'Stopped'){
        $service=Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'" -OperationTimeoutSec 5
        Assert-RebootServiceIdentity $service $taskId $taskExpectedImage @('Disabled')
        $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=Join-Path $env:WINDIR 'System32\sc.exe'
        $info.Arguments=ConvertTo-IsolatedScmNativeCommandLine @('stop',$taskServiceName);$info.UseShellExecute=$false;$info.CreateNoWindow=$true
        $stop=[Diagnostics.Process]::Start($info)
        try {if(-not$stop.WaitForExit(15000)){throw 'reboot_cleanup_native_deadline'}}finally{if(-not$stop.HasExited){$stop.Kill();[void]$stop.WaitForExit(5000)};$stop.Dispose()}
    }
    $clock=[Diagnostics.Stopwatch]::StartNew()
    do {
        $service=Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'" -OperationTimeoutSec 5
        Assert-RebootServiceIdentity $service $taskId $taskExpectedImage @('Disabled')
        if($service.State-ceq'Stopped'-and$service.ProcessId-eq0){
            $process=if($ownProcessId-gt0){Get-CimInstance Win32_Process -Filter ('ProcessId='+$ownProcessId) -OperationTimeoutSec 5}else{$null}
            if(-not$process-or$process.ExecutablePath-ine$taskServiceExe){return}
        }
        if($clock.Elapsed.TotalSeconds-ge45){throw 'reboot_cleanup_stop_unconfirmed'}
        Start-Sleep -Milliseconds 250
    }while($true)
}

$taskMonitor=$null;$taskOwnCreated=$false;$taskCreateAttempted=$false
try {
Assert-RebootEnvironment
$taskIdentity=[Security.Principal.WindowsIdentity]::GetCurrent();$taskOperatorSid=$taskIdentity.User
$taskExpectedImage='"'+$taskServiceExe+'" isolated-scm-service '+$taskId+' '+$taskOperatorSid.Value+' reboot-proof'
$script:ProtectedBaseline=Get-TaskProtectedService; $taskHelperBaseline=Get-RebootHelper
$taskBoot=(Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 5).LastBootUpTime.ToUniversalTime()
$taskPreflight=[ordered]@{mode='read_only';stage=$Stage;runId=$taskId;vm=$env:COMPUTERNAME;uuid='46543236-8C5E-49DA-953C-DB17F9B1E402';account=$taskIdentity.Name;freeMemoryMiB=Get-TaskFreeMiB;cFreeGiB=[math]::Round((Get-TaskCFreeBytes)/1GB,2);candidatePath=$taskCandidatePath;candidateManifestSha256=$taskExpectedManifestSha256;protectedService=$ProtectedBaseline;protectedHelper=$taskHelperBaseline;plannedService=$taskServiceName;plannedPipe=$taskPipeName;dataDirectory=$taskDataDirectory;operatorVerifiedSnapshot='pre reboot save / 84c78c1b-61b9-4e41-a8ef-30db3a655ddf';guestPowerOperations=0;softFloorMiB=768;hardFloorMiB=512;serviceLeaseSeconds=900;pollMilliseconds=250}
if($Stage-ceq'Verify'){
    Assert-RebootPlainPath $taskPlanPath
    if(-not(Test-Path -LiteralPath $taskPlanPath -PathType Leaf)){throw 'reboot_preparation_record_missing'}
    $taskPrepared=Get-Content -LiteralPath $taskPlanPath -Raw|ConvertFrom-Json
    if($taskPrepared.runId-cne$taskId-or$taskPrepared.candidateManifestSha256-cne$taskExpectedManifestSha256-or$taskPrepared.catalogSha256-cne$taskCatalogHash-or$taskPrepared.runnerSha256-cne(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash){throw 'reboot_preparation_identity_mismatch'}
    Assert-RebootManifest $taskAppDirectory $taskExpectedManifestSha256 -Canaries
    Assert-RebootProtectedContinuity $taskPrepared.protectedService $ProtectedBaseline
    Assert-RebootProtectedContinuity $taskPrepared.protectedHelper $taskHelperBaseline
    $taskPreflight.currentIsolatedService=Get-RebootService
}else{
    if((Test-Path -LiteralPath $taskPackageRoot)-or(Test-Path -LiteralPath $taskEvidenceDirectory)-or(Get-Service -Name $taskServiceName -ErrorAction SilentlyContinue)){throw 'reboot_guid_fixture_already_exists'}
}
$taskPreflight|ConvertTo-Json -Depth 6
if(-not$Execute-or$Stage-ceq'Preflight'){Write-Output 'READ-ONLY PREFLIGHT. No service, file, ACL or database was changed.';return}
if(-not[Security.Principal.WindowsPrincipal]::new($taskIdentity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'administrator_token_required'}
$env:DOTNET_ROOT=$taskRuntime;$env:DOTNET_ROOT_X64=$taskRuntime
Add-Type -Path (Join-Path $PSScriptRoot 'CompleteCampaignMonitor.cs')
Add-Type -Path (Join-Path $PSScriptRoot 'IsolatedScmRecovery.cs')
$script:taskMonitor=[CompleteCampaignMonitor]::new()
    if($Stage-ceq'Prepare'){
        New-Item -ItemType Directory -Path $taskEvidenceDirectory|Out-Null
        New-Item -ItemType Directory -Path $taskPackageRoot|Out-Null
        Set-TaskDirectoryAcl $taskPackageRoot $null 'PackageRoot'
        # Demand start during setup; no executable exists at this path until its ACL is established.
        $create=@(Get-IsolatedScmCreateArguments $taskServiceName $taskExpectedImage ('HostAgent DEV reboot '+$taskId))
        $taskCreateAttempted=$true;Invoke-RebootSc $create; $taskOwnCreated=$true
        $taskServiceSid=([Security.Principal.NTAccount]::new('NT SERVICE',$taskServiceName)).Translate([Security.Principal.SecurityIdentifier])
        foreach($pair in @(@($taskAppDirectory,'App'),@($taskDataDirectory,'Data'))){New-Item -ItemType Directory -Path $pair[0]|Out-Null;Set-TaskDirectoryAcl $pair[0] $taskServiceSid $pair[1]}
        Get-ChildItem -LiteralPath $taskCandidatePath -Force|Copy-Item -Destination $taskAppDirectory -Recurse
        Get-ChildItem -LiteralPath $taskAppDirectory -File -Recurse -Force|ForEach-Object {Assert-RebootSafety;Set-TaskFileAcl $_.FullName $taskServiceSid}
        Assert-RebootManifest $taskAppDirectory $taskExpectedManifestSha256
        foreach($name in @('.scm-acl-write-canary','.scm-acl-delete-canary')){ $file=Join-Path $taskAppDirectory $name;[IO.File]::WriteAllText($file,'HostAgent isolated SCM ACL canary v1',[Text.UTF8Encoding]::new($false));Set-TaskFileAcl $file $taskServiceSid }
        # None recovery, then Auto + VBoxService dependency on this GUID registration only.
        $registration=Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'" -OperationTimeoutSec 5
        Assert-RebootServiceIdentity $registration $taskId $taskExpectedImage @('Manual')
        [IsolatedScmRecovery]::Set($taskId,$false)
        # Configuration is checked before depend= is set, hence a direct bounded invocation with a temporary dependency guard.
        function Get-RebootServiceBeforeDependency { $s=Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'";Assert-RebootServiceIdentity $s $taskId $taskExpectedImage @('Disabled');return $s }
        Get-RebootServiceBeforeDependency|Out-Null
        $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=Join-Path $env:WINDIR 'System32\sc.exe';$info.Arguments=ConvertTo-IsolatedScmNativeCommandLine @('config',$taskServiceName,'start=','auto','depend=','VBoxService');$info.UseShellExecute=$false;$info.CreateNoWindow=$true
        $child=[Diagnostics.Process]::Start($info);try{if(-not$child.WaitForExit(15000)){throw 'reboot_config_deadline'};if($child.ExitCode-ne0){throw 'reboot_auto_config_failed'}}finally{if(-not$child.HasExited){$child.Kill();[void]$child.WaitForExit(5000)};$child.Dispose()}
        Get-RebootService @('Auto')|Out-Null
        if(-not[IsolatedScmRecovery]::IsDisabled($taskId)){throw 'reboot_recovery_actions_not_none'}
        if(-not(Test-IsolatedScmPackageRootAcl (Get-Acl -LiteralPath $taskPackageRoot))){throw 'reboot_package_acl_invalid'}
        Assert-RebootManifest $taskAppDirectory $taskExpectedManifestSha256 -Canaries
        Invoke-RebootSc @('start',$taskServiceName);$first=Wait-RebootService 'Running' 60
        $view=Invoke-RebootProbe 'view';$db=Wait-RebootDatabase 1
        if($db.snapshotCount-lt1){throw 'reboot_no_acknowledged_snapshot'}
        [IO.File]::WriteAllText((Join-Path $taskDataDirectory 'reboot-proof-arm.flag'),'arm_test_only')
        $clock=[Diagnostics.Stopwatch]::StartNew();$ready=$null
        do{Assert-RebootSafety;if(Test-Path -LiteralPath (Join-Path $taskDataDirectory 'reboot-proof-ready.json')){$ready=Read-RebootMarker 'reboot-proof-ready.json';break};Start-Sleep -Milliseconds 250}while($clock.Elapsed.TotalSeconds-lt40)
        if(-not$ready-or$ready.runId-cne$taskId-or$ready.committed-ne$true-or$ready.pendingTransactions-ne2-or@($ready.snapshotIds).Count-lt1-or@($ready.snapshotIds).Count-gt64){throw 'reboot_pending_barrier_not_proved'}
        Assert-RebootWithinPhase
        $prepared=[ordered]@{runId=$taskId;candidateManifestSha256=$taskExpectedManifestSha256;catalogSha256=$taskCatalogHash;runnerSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;bootBeforeUtc=$taskBoot.ToString('O');ready=$ready;readySha256=(Get-FileHash -LiteralPath (Join-Path $taskDataDirectory 'reboot-proof-ready.json') -Algorithm SHA256).Hash;protectedService=$ProtectedBaseline;protectedHelper=$taskHelperBaseline;firstProcessId=[int]$first.ProcessId;snapshotCountBefore=[int]$db.snapshotCount;serviceSid=$taskServiceSid.Value;view=$view;preparedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}
        [IO.File]::WriteAllText($taskPlanPath,($prepared|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
        $taskRunResult.outcome='PREPARED_MANUAL_RESTART_REQUIRED';$taskRunResult.firstProcessId=[int]$first.ProcessId;$taskRunResult.cleanup='auto_GUID_service_and_evidence_retained_for_manual_restart'
    }else{
        $taskOwnCreated=$true;$taskServiceSid=[Security.Principal.SecurityIdentifier]::new($taskPrepared.serviceSid)
        $service=Get-RebootService @('Auto')
        if($service.State-cne'Running'){throw 'reboot_automatic_start_not_running'}
        $process=Get-CimInstance Win32_Process -Filter ('ProcessId='+$service.ProcessId) -OperationTimeoutSec 5
        if(-not$process-or$process.ExecutablePath-ine$taskServiceExe){throw 'reboot_process_image_mismatch'}
        $closed=Read-RebootMarker 'reboot-proof-closed.json';$after=Read-RebootMarker 'reboot-after-start.json'
        Assert-RebootTransition $taskPrepared $taskBoot $closed $after ([int]$service.ProcessId) $process.CreationDate
        foreach($protected in @($ProtectedBaseline,$taskHelperBaseline)){
            $protectedProcess=Get-CimInstance Win32_Process -Filter ('ProcessId='+$protected.ProcessId) -OperationTimeoutSec 5
            if(-not$protectedProcess-or$protectedProcess.CreationDate.ToUniversalTime()-lt$taskBoot-or$protectedProcess.ExecutablePath-ine$protected.PathName.Trim('"')){throw 'reboot_protected_automatic_start_not_proved'}
        }
        if((Get-FileHash -LiteralPath (Join-Path $taskDataDirectory 'reboot-proof-ready.json') -Algorithm SHA256).Hash-cne$taskPrepared.readySha256){throw 'reboot_ready_proof_changed'}
        if(-not[IsolatedScmRecovery]::IsDisabled($taskId)){throw 'reboot_unexpected_recovery_actions'}
        $view=Invoke-RebootProbe 'view';$db=Wait-RebootDatabase ([int]$taskPrepared.snapshotCountBefore+1)
        Stop-RebootFixture
        $locks=@();$beforeHashes=@{}
        try{
            foreach($name in @('host-agent.db','notifications.db')){$path=Join-Path $taskDataDirectory $name;Assert-RebootPlainPath $path;$locks += [IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read);$beforeHashes[$name]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
            $code=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'check-reboot-persistence.py') -Raw
            $output=Invoke-RebootChild $taskPython @('-I','-B','-',$taskId,$taskBoot.ToString('O')) 30 $code
            $checks=$output|ConvertFrom-Json
            if(@($checks).Count-ne2-or@($checks|Where-Object {$_.database-ceq'host-agent.db'}).Count-ne1-or@($checks|Where-Object {$_.database-ceq'notifications.db'}).Count-ne1-or
                @($checks|Where-Object {$_.quickCheck-cne'ok'-or$_.committedProofPreserved-ne$true-or$_.uncommittedProofAbsent-ne$true-or$_.postBootProgress-ne$true-or($_.database-ceq'host-agent.db'-and$_.acknowledgedSnapshotsPreserved-ne$true)}).Count){throw 'reboot_persistence_invariant_failed'}
            foreach($name in $beforeHashes.Keys){if((Get-FileHash -LiteralPath (Join-Path $taskDataDirectory $name) -Algorithm SHA256).Hash-cne$beforeHashes[$name]){throw 'reboot_readonly_check_changed_database'}}
        }finally{foreach($lock in $locks){$lock.Dispose()}}
        Assert-RebootManifest $taskAppDirectory $taskExpectedManifestSha256 -Canaries
        Assert-RebootProtectedContinuity $taskPrepared.protectedService (Get-TaskProtectedService);Assert-RebootProtectedContinuity $taskPrepared.protectedHelper (Get-RebootHelper)
        Assert-RebootWithinPhase
        $taskRunResult.outcome='PASS';$taskRunResult.scope='normal_Windows_restart_graceful_rollback_and_automatic_GUID_service_start_not_power_loss';$taskRunResult.bootBeforeUtc=$taskPrepared.bootBeforeUtc;$taskRunResult.bootAfterUtc=$taskBoot.ToString('O');$taskRunResult.firstProcessId=$taskPrepared.firstProcessId;$taskRunResult.secondProcessId=[int]$service.ProcessId;$taskRunResult.databaseChecks=@($checks);$taskRunResult.view=$view;$taskRunResult.protectedConfigurationAndHashesUnchanged=$true;$taskRunResult.cleanup='GUID_service_Stopped_Disabled_package_and_data_preserved'
    }
}catch{
    $taskRunResult.outcome='FAIL';$taskRunResult.failureCode=if($_.Exception.Message-match'^[a-z][a-z0-9_]{1,95}$'){$_.Exception.Message}else{'reboot_unexpected_exception'}
    $taskRunResult.failureLine=$_.InvocationInfo.ScriptLineNumber
    $taskRunResult.failureType=$_.Exception.GetType().FullName
    if($taskCreateAttempted-and-not$taskOwnCreated){try{$s=Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'" -OperationTimeoutSec 5;Assert-RebootServiceIdentity $s $taskId $taskExpectedImage @('Manual','Auto','Disabled');$taskOwnCreated=$true}catch{}}
    if($taskOwnCreated){try{Stop-RebootFixture;$taskRunResult.cleanup='GUID_service_Stopped_Disabled_all_evidence_preserved'}catch{$taskRunResult.cleanup='UNCONFIRMED_all_evidence_preserved';$taskRunResult.cleanupFailureLine=$_.InvocationInfo.ScriptLineNumber}}
}finally{
    if($taskMonitor){
        if($taskMonitor.FailureCode){
            if($taskRunResult.outcome-cne'FAIL'-and$taskOwnCreated){try{Stop-RebootFixture;$taskRunResult.cleanup='GUID_service_Stopped_Disabled_all_evidence_preserved'}catch{$taskRunResult.cleanup='UNCONFIRMED_all_evidence_preserved';$taskRunResult.cleanupFailureLine=$_.InvocationInfo.ScriptLineNumber}}
            $taskRunResult.outcome='FAIL';$taskRunResult.failureCode=$taskMonitor.FailureCode
        }
        $taskMonitor.Dispose()
    }
    $taskRunResult.evidenceDirectory=$taskEvidenceDirectory;$taskRunResult.retainedPackagePath=$taskPackageRoot
    if($Execute-and$Stage-cne'Preflight'){
        if(Test-Path -LiteralPath $taskEvidenceDirectory){$path=Join-Path $taskEvidenceDirectory ('result-'+$Stage.ToLowerInvariant()+'-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')+'.json');[IO.File]::WriteAllText($path,($taskRunResult|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false));$taskRunResult.resultPath=$path}
        $taskRunResult|ConvertTo-Json -Depth 8
    }
}
if($taskRunResult.outcome-ceq'FAIL'){throw ('reboot_gate_failed:'+ $taskRunResult.failureCode)}
if($Stage-ceq'Prepare'){Write-Output 'READY: manually choose Start > Power > Restart INSIDE THE VM within 5 minutes. After sign-in run this same script -Stage Verify -Execute within 10 minutes. No power action was performed by this script.'}
