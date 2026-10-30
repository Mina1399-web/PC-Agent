param([switch]$Execute,[Parameter(Mandatory=$true)][ValidatePattern('^[a-f0-9]{32}$')][string]$RunId)
$WatchdogStopProbe=$false
$CrashRecoveryProbe=$true
Import-Module (Join-Path $PSScriptRoot 'IsolatedScmCrashSafety.psm1') -Force
. (Join-Path $PSScriptRoot 'Invoke-IsolatedScmAutoCrash.ps1')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'IsolatedScmSafety.psm1') -Force
if ($WatchdogStopProbe) { . (Join-Path $PSScriptRoot 'Invoke-BoundedScmPressure.ps1') }

# One-shot, explicitly opted-in SCM test for the current Release candidate.
# Without -Execute this performs read-only preflight and prints the plan.
$taskExpectedManifestSha256 = 'EBBAD0807899BB8243AE71B2877C4C02B4C3F6024024EE748A179342428CAAAB'
$taskCandidatePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\HostAgent-DEV-SCM-candidates\scm-release-candidate-20261017-r5'))
$taskEvidenceRoot = Join-Path $env:LOCALAPPDATA 'HostAgent-DEV-evidence'
$taskProtectedPath = 'C:\Program Files\HostAgent\Service\HostAgent.Service.exe'
$taskProtectedSha256 = '04DF669D662BB08BDBD76653E46158E3D701D7E0D48653439F8F836BB417CB87'
$taskExpectedProductHashes = @{
    'HostAgent.Core.dll' = 'A59AE0BABD875F83025B876CEBA0883A92BB9686FF8C2F65AC6034C1AD268379'
    'HostAgent.Infrastructure.dll' = '48A628574834962BE7BB37CDBE08B0E86C8A09D7A7187D35B65AE9DCC2B2672C'
    'HostAgent.Service.dll' = 'E3E253AADB91932D1061569955E236115CB45A0F4FDC0E772A083A8D00CF6A10'
    'HostAgent.PipeProbe.dll' = 'E0BA2494907A7CAEBA385B8D615077654B638318F9E28EC32CF1AE63206C5B77'
    'HostAgent.PipeProbe.exe' = '6E270A54D2FC3B6E5B69DEEECA9090203BBF8D3BB032FD345CDAA68B2106E39D'
}
$taskExpectedProtectedAssemblyHashes = @{
    'HostAgent.Core.dll' = 'E7F156748E61AB8AEBE49B80E2B9A379EAC4588559EA55BF0B806D5229100080'
    'HostAgent.Infrastructure.dll' = 'B86C9673CBDE1EDC755B1AD8FFA498373517EF1E3A2CD673261970931594A8AE'
    'HostAgent.Service.dll' = '19772D28E83DB3704419F2C6EB1450F3FF51BD1FCD8ED2B9D63A8A32695CA840'
}
$taskCurrentStage = 'preflight'
$taskId = $RunId
if ($taskId -eq '00000000000000000000000000000000') { throw 'empty_run_id' }
$taskAutoRecoveryQuiesced = $false
$taskServiceName = 'HostAgentDevScm_' + $taskId
$taskPipeName = 'HostAgent.Dev.Scm.View.' + $taskId
$taskPackageRoot = Join-Path $env:ProgramData ('HostAgent-DEV-SCM-' + $taskId)
$taskAppDirectory = Join-Path $taskPackageRoot 'app'
$taskDataDirectory = Join-Path $taskPackageRoot 'data'
$taskEvidenceDirectory = Join-Path $taskEvidenceRoot ('scm-candidate-' + $taskId)
$script:taskMemoryMarkerPath = Join-Path $taskEvidenceDirectory 'memory-floor-breach.json'
$script:taskWatchdogStopPath = Join-Path $taskEvidenceDirectory 'memory-watchdog.stop'
$script:taskMemoryWatchdog = $null
$taskServiceCreated = $false
$taskServiceCreationAttempted = $false
$taskUnexpectedRegistration = $false
$taskPackageCreated = $false
$taskWatchdogProbeValidated = $false
$taskRunResult = [ordered]@{
    scenario = 'isolated-current-release-scm-lifecycle'
    runId = $taskId
    serviceName = $taskServiceName
    pipeName = $taskPipeName
    outcome = 'not_run'
    failureCode = $null
    failureReason = $null
    scmCreateExitCode = $null
    scmOperations = @()
    protectedServiceUnchanged = $null
    viewStages = @()
    databaseChecks = @()
    memoryGuard = 'not_started'
    cleanup = 'not_started'
}

function Get-TaskFreeMiB {
    $taskOs = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
    return [math]::Round($taskOs.FreePhysicalMemory / 1024, 1)
}

function Get-TaskCFreeBytes {
    return [long](Get-PSDrive -Name C -ErrorAction Stop).Free
}

function Assert-TaskVm {
    $taskComputer = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
    $taskBios = Get-ItemProperty 'HKLM:\HARDWARE\DESCRIPTION\System\BIOS' -ErrorAction Stop
    $taskVBox = Get-Service VBoxService -ErrorAction Stop
    if ($env:COMPUTERNAME -ne 'AGENTPC' -or $taskComputer.Model -ne 'VirtualBox' -or
        $taskBios.SystemProductName -ne 'VirtualBox' -or $taskVBox.Status -ne 'Running') {
        throw 'vm_identity_mismatch'
    }
    if ((Get-TaskFreeMiB) -lt 1024) { throw 'preflight_free_memory_below_1024_mib' }
    if ((Get-TaskCFreeBytes) -lt 20GB) { throw 'preflight_c_free_below_20_gib' }
}

function Assert-TaskPlainTree([string]$Path) {
    $taskFullPath = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $taskFullPath -PathType Container)) { throw 'required_directory_missing' }
    for ($taskParent = Get-Item -LiteralPath $taskFullPath -Force; $null -ne $taskParent; $taskParent = $taskParent.Parent) {
        if (($taskParent.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'reparse_path_rejected' }
    }
    $taskQueue = [System.Collections.Generic.Queue[string]]::new()
    $taskQueue.Enqueue($taskFullPath)
    while ($taskQueue.Count -gt 0) {
        foreach ($taskItem in Get-ChildItem -LiteralPath $taskQueue.Dequeue() -Force -ErrorAction Stop) {
            if (($taskItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'reparse_path_rejected' }
            if ($taskItem.PSIsContainer) { $taskQueue.Enqueue($taskItem.FullName) }
        }
    }
}

function Assert-TaskNoReparseAncestors([string]$Path) {
    for ($taskParent = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath($Path)); $null -ne $taskParent; $taskParent = $taskParent.Parent) {
        $taskParent.Refresh()
        if ($taskParent.Exists -and ($taskParent.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'reparse_path_rejected'
        }
    }
}

function Assert-TaskManifest([string]$Directory) {
    Assert-TaskPlainTree $Directory
    $taskManifestPath = Join-Path $Directory 'candidate-manifest.json'
    $taskManifestHash = (Get-FileHash -LiteralPath $taskManifestPath -Algorithm SHA256 -ErrorAction Stop).Hash
    if ($taskManifestHash -ne $taskExpectedManifestSha256) { throw 'candidate_manifest_hash_mismatch' }
    $taskManifest = Get-Content -LiteralPath $taskManifestPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    if ($taskManifest.format -ne 1 -or $taskManifest.files.Count -lt 10) { throw 'candidate_manifest_invalid' }
    $taskEntries = @{}
    foreach ($taskEntry in $taskManifest.files) {
        $taskRelative = [string]$taskEntry.path
        if ([string]::IsNullOrWhiteSpace($taskRelative) -or [IO.Path]::IsPathRooted($taskRelative) -or
            $taskRelative.Contains('..') -or $taskEntries.ContainsKey($taskRelative)) { throw 'candidate_manifest_path_invalid' }
        $taskFull = [IO.Path]::GetFullPath((Join-Path $Directory $taskRelative))
        $taskPrefix = [IO.Path]::GetFullPath($Directory).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $taskFull.StartsWith($taskPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'candidate_manifest_path_escape' }
        if (-not (Test-Path -LiteralPath $taskFull -PathType Leaf)) { throw 'candidate_file_missing' }
        $taskActualHash = (Get-FileHash -LiteralPath $taskFull -Algorithm SHA256 -ErrorAction Stop).Hash
        if ($taskActualHash -ne $taskEntry.sha256) { throw 'candidate_file_hash_mismatch' }
        $taskEntries[$taskRelative.Replace('/', '\')] = $true
    }
    $taskActualFiles = @(Get-ChildItem -LiteralPath $Directory -File -Recurse -Force |
        Where-Object { $_.Name -ne 'candidate-manifest.json' })
    if ($taskActualFiles.Count -ne $taskEntries.Count) { throw 'candidate_file_inventory_mismatch' }
    foreach ($taskFile in $taskActualFiles) {
        $taskRelative = $taskFile.FullName.Substring(([IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\').Length)
        if (-not $taskEntries.ContainsKey($taskRelative)) { throw 'candidate_unmanifested_file' }
    }
    foreach ($taskName in $taskExpectedProductHashes.Keys) {
        $taskProductPath = Join-Path $Directory $taskName
        if ((Get-FileHash -LiteralPath $taskProductPath -Algorithm SHA256).Hash -ne $taskExpectedProductHashes[$taskName]) {
            throw 'release_assembly_hash_mismatch'
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $Directory 'HostAgent.PipeProbe.exe') -PathType Leaf)) {
        throw 'scm_test_host_missing'
    }
    return $taskManifestHash
}

function Get-TaskProtectedService {
    $taskService = Get-CimInstance Win32_Service -Filter "Name='HostAgent'" -ErrorAction Stop
    if (-not $taskService -or $taskService.State -ne 'Running' -or $taskService.StartMode -ne 'Auto' -or
        $taskService.StartName -ne 'NT SERVICE\HostAgent' -or
        $taskService.PathName -ne '"C:\Program Files\HostAgent\Service\HostAgent.Service.exe"') {
        throw 'protected_service_preflight_mismatch'
    }
    $taskHash = (Get-FileHash -LiteralPath $taskProtectedPath -Algorithm SHA256 -ErrorAction Stop).Hash
    if ($taskHash -ne $taskProtectedSha256) { throw 'protected_image_preflight_mismatch' }
    $taskAssemblyHashes = @{}
    foreach ($taskName in $taskExpectedProtectedAssemblyHashes.Keys) {
        $taskAssemblyPath = Join-Path 'C:\Program Files\HostAgent\Service' $taskName
        $taskAssemblyHash = (Get-FileHash -LiteralPath $taskAssemblyPath -Algorithm SHA256 -ErrorAction Stop).Hash
        if ($taskAssemblyHash -ne $taskExpectedProtectedAssemblyHashes[$taskName]) { throw 'protected_assembly_preflight_mismatch' }
        $taskAssemblyHashes[$taskName] = $taskAssemblyHash
    }
    return [pscustomobject]@{
        Name = $taskService.Name; State = $taskService.State; StartMode = $taskService.StartMode
        StartName = $taskService.StartName; PathName = $taskService.PathName; ImageSha256 = $taskHash
        ProcessId = [int]$taskService.ProcessId
        AssemblyHashes = $taskAssemblyHashes
    }
}

function Assert-TaskProtectedService($Expected) {
    $taskNow = Get-TaskProtectedService
    foreach ($taskProperty in @('Name', 'State', 'StartMode', 'StartName', 'PathName', 'ImageSha256', 'ProcessId')) {
        if ($taskNow.$taskProperty -ne $Expected.$taskProperty) { throw 'protected_service_changed_during_test' }
    }
    foreach ($taskName in $taskExpectedProtectedAssemblyHashes.Keys) {
        if ($taskNow.AssemblyHashes[$taskName] -ne $Expected.AssemblyHashes[$taskName]) { throw 'protected_assembly_changed_during_test' }
    }
}

function Assert-TaskSafetyFloor {
    if (Test-Path -LiteralPath $script:taskMemoryMarkerPath -PathType Leaf) {
        try { $taskRunResult.memoryGuard = Get-Content -LiteralPath $script:taskMemoryMarkerPath -Raw | ConvertFrom-Json }
        catch { $taskRunResult.memoryGuard = 'breach_marker_unreadable' }
        throw 'memory_floor_breached_watchdog_stopped_test_service'
    }
    if ($script:taskMemoryWatchdog -and $script:taskMemoryWatchdog.HasExited) {
        throw ('memory_watchdog_exited_' + $script:taskMemoryWatchdog.ExitCode)
    }
    if ((Get-TaskFreeMiB) -lt 768) { throw 'soft_free_memory_floor_768_mib' }
    if ((Get-TaskCFreeBytes) -lt 20GB) { throw 'c_free_floor_20_gib' }
}

function Start-TaskMemoryWatchdog {
    Assert-TaskIsolatedService | Out-Null
    $taskInfo = [Diagnostics.ProcessStartInfo]::new()
    $taskInfo.FileName = $script:taskServiceExe
    $taskInfo.WorkingDirectory = $script:taskAppDirectory
    $taskInfo.UseShellExecute = $false
    $taskInfo.CreateNoWindow = $true
    $taskInfo.Arguments = ConvertTo-IsolatedScmNativeCommandLine @(
        'isolated-scm-watchdog', $script:taskId,
        $script:taskMemoryMarkerPath, $script:taskWatchdogStopPath, '768')
    $script:taskMemoryWatchdog = [Diagnostics.Process]::Start($taskInfo)
    if (-not $script:taskMemoryWatchdog) { throw 'memory_watchdog_start_failed' }
    Start-Sleep -Milliseconds 300
    if ($script:taskMemoryWatchdog.HasExited) { throw ('memory_watchdog_start_exit_' + $script:taskMemoryWatchdog.ExitCode) }
    $taskRunResult.memoryGuard = 'active_250ms_watchdog_and_inline_checks_at_768_mib'
}

function Stop-TaskMemoryWatchdog {
    $taskWaitMilliseconds = 3000
    if ($script:taskMemoryWatchdog -and -not $script:taskMemoryWatchdog.HasExited) {
        if ((Get-TaskFreeMiB) -ge 768) {
            [IO.File]::WriteAllText($script:taskWatchdogStopPath, 'test_complete')
        } else {
            $taskWaitMilliseconds = 12000
        }
        if (-not $script:taskMemoryWatchdog.WaitForExit($taskWaitMilliseconds)) { throw 'memory_watchdog_shutdown_timeout' }
    }
    if (Test-Path -LiteralPath $script:taskWatchdogStopPath -PathType Leaf) {
        Remove-Item -LiteralPath $script:taskWatchdogStopPath -Force -ErrorAction Stop
    }
}

function Wait-TaskServiceState([string]$State, [int]$TimeoutSeconds) {
    $taskWatch = [Diagnostics.Stopwatch]::StartNew()
    do {
        Assert-TaskSafetyFloor
        Assert-TaskProtectedService $script:ProtectedBaseline
        $taskService = Get-CimInstance Win32_Service -Filter "Name='$script:taskServiceName'" -ErrorAction SilentlyContinue
        if ($taskService) { Assert-TaskIsolatedService | Out-Null }
        if ($null -ne $taskService -and $taskService.State -eq $State) { return $taskService }
        Start-Sleep -Seconds 1
    } while ($taskWatch.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    throw ('isolated_service_state_timeout_' + $State.ToLowerInvariant())
}

function Ensure-TaskStoppedAfterMemoryBreach {
    $taskWatch = [Diagnostics.Stopwatch]::StartNew()
    while ($taskWatch.Elapsed.TotalSeconds -lt 15) {
        $taskService = Get-CimInstance Win32_Service -Filter "Name='$script:taskServiceName'" -ErrorAction SilentlyContinue
        if (-not $taskService) { return $false }
        Assert-TaskIsolatedService | Out-Null
        if ($taskService.State -eq 'Stopped') { return $true }
        if ($taskService.State -eq 'StopPending') {
            Start-Sleep -Milliseconds 250
            continue
        }
        Assert-TaskProtectedService $script:ProtectedBaseline
        & "$env:WINDIR\System32\sc.exe" stop $script:taskServiceName | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Start-Sleep -Milliseconds 250
            continue
        }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Assert-TaskIsolatedService {
    $taskService = Get-CimInstance Win32_Service -Filter "Name='$script:taskServiceName'" -ErrorAction Stop
    $taskExpected = '"' + $script:taskServiceExe + '" isolated-scm-service ' + $script:taskId + ' ' + $script:taskOperatorSid.Value + ' crash-proof'
    if (-not $taskService -or $taskService.Name -ne $script:taskServiceName -or
        $taskService.StartName -ne ('NT SERVICE\' + $script:taskServiceName) -or
        $taskService.StartMode -notin @('Manual','Disabled') -or $taskService.PathName -ne $taskExpected) {
        throw 'isolated_service_identity_mismatch'
    }
    return $taskService
}

function Invoke-TaskSc([string]$Operation) {
    Assert-TaskSafetyFloor
    Assert-TaskProtectedService $script:ProtectedBaseline
    Assert-TaskIsolatedService | Out-Null
    if ($Operation -notin @('start', 'stop', 'delete')) { throw 'invalid_service_operation' }
    if ($Operation -eq 'start' -and -not (Test-IsolatedScmPackageRootAcl (Get-Acl -LiteralPath $script:taskPackageRoot -ErrorAction Stop))) {
        throw 'isolated_package_root_acl_mismatch'
    }
    & "$env:WINDIR\System32\sc.exe" $Operation $script:taskServiceName | Out-Null
    $taskRunResult.scmOperations += [pscustomobject]@{ operation = $Operation; exitCode = $LASTEXITCODE }
    if ($LASTEXITCODE -ne 0) { throw ('isolated_sc_' + $Operation + '_failed') }
    Assert-TaskProtectedService $script:ProtectedBaseline
}

function Invoke-TaskProbe([string]$Mode) {
    Assert-TaskSafetyFloor
    Assert-TaskProtectedService $script:ProtectedBaseline
    Assert-TaskIsolatedService | Out-Null
    $taskProbeExe = Join-Path $script:taskAppDirectory 'HostAgent.PipeProbe.exe'
    if ($Mode -eq 'view') {
        $taskOutput = @(& $taskProbeExe 'isolated-scm-view' $script:taskId $script:taskServiceSid.Value 2>$null)
        Assert-TaskSafetyFloor
        if ($LASTEXITCODE -ne 0 -or $taskOutput.Count -lt 1) { throw 'isolated_view_probe_failed' }
        $taskItem = $taskOutput | Select-Object -Last 1 | ConvertFrom-Json -ErrorAction Stop
        if (-not $taskItem.ok) { throw 'isolated_view_unavailable' }
        $script:taskRunResult.viewStages += [pscustomobject]@{
            attempt = [int]$taskItem.attempt; ok = [bool]$taskItem.ok
            elapsedMilliseconds = [long]$taskItem.elapsedMilliseconds
        }
        return
    }
    $taskOutput = @(& $taskProbeExe 'isolated-scm-dbcheck' $script:taskId 2>$null)
    Assert-TaskSafetyFloor
    if ($LASTEXITCODE -ne 0 -or $taskOutput.Count -lt 1) { throw 'isolated_database_check_failed' }
    $taskItem = $taskOutput | Select-Object -Last 1 | ConvertFrom-Json -ErrorAction Stop
    if (-not $taskItem.healthy -or $taskItem.quickCheck -ne 'ok') { throw 'isolated_database_unhealthy' }
    $taskCheck = [pscustomobject]@{
        healthy = [bool]$taskItem.healthy; quickCheck = [string]$taskItem.quickCheck
        migrationCount = [int]$taskItem.migrationCount; snapshotCount = [int]$taskItem.snapshotCount
        baselineCount = [int]$taskItem.baselineCount; databaseBytes = [long]$taskItem.databaseBytes
    }
    $script:taskRunResult.databaseChecks += $taskCheck
    return $taskCheck
}

function Wait-TaskDatabaseReady([int]$TimeoutSeconds) {
    $taskWatch = [Diagnostics.Stopwatch]::StartNew()
    do {
        Assert-TaskSafetyFloor
        Assert-TaskProtectedService $script:ProtectedBaseline
        Assert-TaskIsolatedService | Out-Null
        try { return (Invoke-TaskProbe 'dbcheck') }
        catch { Start-Sleep -Seconds 1 }
    } while ($taskWatch.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    throw 'isolated_database_initialization_timeout'
}

function Set-TaskDirectoryAcl([string]$Directory, $ServiceSid, [ValidateSet('PackageRoot','App','Data')][string]$Scope) {
    $taskAdminSid = [System.Security.Principal.SecurityIdentifier]::new([System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
    $taskSystemSid = [System.Security.Principal.SecurityIdentifier]::new([System.Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
    $taskAcl = [System.Security.AccessControl.DirectorySecurity]::new()
    $taskAcl.SetAccessRuleProtection($true, $false)
    $taskAcl.SetOwner($taskAdminSid)
    $taskInheritance = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
    $taskAcl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($taskAdminSid,
        [System.Security.AccessControl.FileSystemRights]::FullControl, $taskInheritance,
        [System.Security.AccessControl.PropagationFlags]::None, [System.Security.AccessControl.AccessControlType]::Allow))
    $taskAcl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($taskSystemSid,
        [System.Security.AccessControl.FileSystemRights]::FullControl, $taskInheritance,
        [System.Security.AccessControl.PropagationFlags]::None, [System.Security.AccessControl.AccessControlType]::Allow))
    $taskPolicy = Get-IsolatedScmAclPolicy $Scope
    if ($null -ne $ServiceSid -and $taskPolicy.ServiceRights -ne 'None') {
        $taskServiceRights = [System.Enum]::Parse([System.Security.AccessControl.FileSystemRights], $taskPolicy.ServiceRights)
        $taskAcl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($ServiceSid,
            $taskServiceRights, $taskInheritance, [System.Security.AccessControl.PropagationFlags]::None,
            [System.Security.AccessControl.AccessControlType]::Allow))
    }
    Set-Acl -LiteralPath $Directory -AclObject $taskAcl -ErrorAction Stop
}

function Set-TaskFileAcl([string]$File, $ServiceSid) {
    $taskAdminSid = [System.Security.Principal.SecurityIdentifier]::new([System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
    $taskSystemSid = [System.Security.Principal.SecurityIdentifier]::new([System.Security.Principal.WellKnownSidType]::LocalSystemSid, $null)
    $taskAcl = [System.Security.AccessControl.FileSecurity]::new()
    $taskAcl.SetAccessRuleProtection($true, $false)
    $taskAcl.SetOwner($taskAdminSid)
    foreach ($taskSid in @($taskAdminSid, $taskSystemSid)) {
        $taskAcl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($taskSid,
            [System.Security.AccessControl.FileSystemRights]::FullControl,
            [System.Security.AccessControl.AccessControlType]::Allow))
    }
    $taskAcl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($ServiceSid,
        [System.Security.AccessControl.FileSystemRights]::ReadAndExecute,
        [System.Security.AccessControl.AccessControlType]::Allow))
    Set-Acl -LiteralPath $File -AclObject $taskAcl -ErrorAction Stop
}

function Assert-TaskAclCanaries {
    $taskExpected = [Text.Encoding]::UTF8.GetBytes('HostAgent isolated SCM ACL canary v1')
    foreach ($taskName in @('.scm-acl-write-canary', '.scm-acl-delete-canary')) {
        $taskPath = Join-Path $script:taskAppDirectory $taskName
        if (-not (Test-Path -LiteralPath $taskPath -PathType Leaf)) { throw 'app_acl_canary_missing' }
        $taskBytes = [IO.File]::ReadAllBytes($taskPath)
        if ([Convert]::ToBase64String($taskBytes) -ne [Convert]::ToBase64String($taskExpected)) {
            throw 'app_acl_canary_changed'
        }
    }
    $taskWriteCanaryHash = (Get-FileHash -LiteralPath (Join-Path $script:taskAppDirectory '.scm-acl-write-canary') -Algorithm SHA256).Hash
    if ($taskWriteCanaryHash -ne $script:taskCanarySha256) { throw 'app_acl_canary_hash_changed' }
}

function Remove-TaskOwnedTree {
    if (-not $script:taskPackageCreated -or -not (Test-Path -LiteralPath $script:taskPackageRoot -PathType Container)) { return $true }
    $taskExpectedRoot = Join-Path $env:ProgramData ('HostAgent-DEV-SCM-' + $script:taskId)
    if ([IO.Path]::GetFullPath($script:taskPackageRoot) -ne [IO.Path]::GetFullPath($taskExpectedRoot)) { return $false }
    try {
        if (Get-CimInstance Win32_Service -Filter "Name='$script:taskServiceName'" -ErrorAction SilentlyContinue) { return $false }
        Assert-TaskPlainTree $script:taskPackageRoot
        Remove-Item -LiteralPath $script:taskPackageRoot -Recurse -Force -ErrorAction Stop
        return $true
    }
    catch { return $false }
}

function Write-TaskEvidence {
    if (-not (Test-Path -LiteralPath $taskEvidenceDirectory -PathType Container)) { return }
    $taskEvidencePath = Join-Path $taskEvidenceDirectory 'result.json'
    $taskRunResult | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $taskEvidencePath -Encoding UTF8
}

$taskCurrentStage = 'preflight'
Assert-TaskVm
$taskManifestHash = Assert-TaskManifest $taskCandidatePath
$taskCrashHelperHashes=[ordered]@{}
foreach($taskName in @('IsolatedScmCrashSafety.psm1','Invoke-IsolatedScmAutoCrash.ps1','check-isolated-scm-crash.py')) {
    $taskHelper=Join-Path $PSScriptRoot $taskName
    Assert-TaskNoReparseAncestors $taskHelper
    if(-not (Test-Path -LiteralPath $taskHelper -PathType Leaf)){throw 'crash_helper_missing'}
    $taskCrashHelperHashes[$taskName]=(Get-FileHash -LiteralPath $taskHelper -Algorithm SHA256).Hash
}
if(-not (Test-Path -LiteralPath 'C:\Users\vmdev\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' -PathType Leaf)){throw 'verified_python_missing'}
$taskPressureSources = $null
if ($WatchdogStopProbe) {
    $taskPressureSources = [ordered]@{}
    foreach ($taskPressureName in @('BoundedScmPressure.cs','Invoke-BoundedScmPressure.ps1','IsolatedScmSafety.psm1')) {
        $taskPressureSource = Join-Path $PSScriptRoot $taskPressureName
        Assert-TaskNoReparseAncestors $taskPressureSource
        if (-not (Test-Path -LiteralPath $taskPressureSource -PathType Leaf)) { throw 'pressure_helper_missing' }
        $taskPressureSources[$taskPressureName] = (Get-FileHash -LiteralPath $taskPressureSource -Algorithm SHA256).Hash
    }
}
$script:ProtectedBaseline = Get-TaskProtectedService
$taskIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$taskOperatorSid = $taskIdentity.User
if (-not $taskOperatorSid) { throw 'operator_sid_unavailable' }
if (Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'" -ErrorAction SilentlyContinue) { throw 'isolated_service_name_already_exists' }
if (Test-Path -LiteralPath $taskPackageRoot) { throw 'isolated_package_or_data_path_already_exists' }
if (Test-Path -LiteralPath $taskEvidenceDirectory) { throw 'isolated_evidence_path_already_exists' }
Assert-TaskNoReparseAncestors $taskPackageRoot
Assert-TaskNoReparseAncestors $taskEvidenceRoot
Assert-TaskVm

$taskPreflight = [pscustomobject]@{
    mode = $(if ($WatchdogStopProbe) { 'bounded_watchdog_stop_probe' } else { 'controlled_crash_recovery' })
    crashHelperHashes = $taskCrashHelperHashes
    pressureHelperHashes = $taskPressureSources
    vm = $env:COMPUTERNAME
    account = $taskIdentity.Name
    model = (Get-CimInstance Win32_ComputerSystem).Model
    vboxGuestAdditions = (Get-Service VBoxService).Status.ToString()
    freeMemoryMiB = Get-TaskFreeMiB
    cFreeGiB = [math]::Round((Get-TaskCFreeBytes) / 1GB, 2)
    candidatePath = $taskCandidatePath
    candidateManifestSha256 = $taskManifestHash
    protectedServiceName = $script:ProtectedBaseline.Name
    protectedServiceState = $script:ProtectedBaseline.State
    protectedServiceProcessId = $script:ProtectedBaseline.ProcessId
    protectedImageSha256 = $script:ProtectedBaseline.ImageSha256
    protectedAssemblySha256 = $script:ProtectedBaseline.AssemblyHashes
    candidateAssembliesSha256 = [ordered]@{
        'HostAgent.Core.dll' = (Get-FileHash -LiteralPath (Join-Path $taskCandidatePath 'HostAgent.Core.dll') -Algorithm SHA256).Hash
        'HostAgent.Infrastructure.dll' = (Get-FileHash -LiteralPath (Join-Path $taskCandidatePath 'HostAgent.Infrastructure.dll') -Algorithm SHA256).Hash
        'HostAgent.Service.dll' = (Get-FileHash -LiteralPath (Join-Path $taskCandidatePath 'HostAgent.Service.dll') -Algorithm SHA256).Hash
        'HostAgent.PipeProbe.dll' = (Get-FileHash -LiteralPath (Join-Path $taskCandidatePath 'HostAgent.PipeProbe.dll') -Algorithm SHA256).Hash
        'HostAgent.PipeProbe.exe' = (Get-FileHash -LiteralPath (Join-Path $taskCandidatePath 'HostAgent.PipeProbe.exe') -Algorithm SHA256).Hash
    }
    plannedServiceName = $taskServiceName
    plannedViewPipe = $taskPipeName
    plannedServiceImagePath = Join-Path $taskPackageRoot 'app\HostAgent.PipeProbe.exe'
    plannedDataDirectory = $taskDataDirectory
    rollbackSnapshotPreviouslyHostVerified = 'pre chaos snapshot save / dc8af832-31d6-49ac-a8c3-f00d9381597f'
}
$taskPreflight | ConvertTo-Json -Depth 4
if (-not $Execute) {
    Write-Output 'READ-ONLY PREFLIGHT ONLY. No service, file, ACL, or database operation was performed.'
    Write-Output 'After review, run once from an elevated PowerShell inside HostAgent-DEV with -Execute.'
    return
}
if (-not [Security.Principal.WindowsPrincipal]::new($taskIdentity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'administrator_token_required'
}

try {
    $taskCurrentStage = 'evidence_setup'
    New-Item -ItemType Directory -Path $taskEvidenceDirectory | Out-Null
    $taskRunResult.account = $taskIdentity.Name
    $taskRunResult.candidateManifestSha256 = $taskManifestHash
    $taskRunResult.crashHelperHashes = $taskCrashHelperHashes
    if ($WatchdogStopProbe) { $taskRunResult.pressureHelperHashes = $taskPressureSources }
    $taskRunResult.serviceSid = $null

    $taskCurrentStage = 'candidate_stage'
    Assert-TaskManifest $taskCandidatePath | Out-Null
    New-Item -ItemType Directory -Path $taskPackageRoot | Out-Null
    $script:taskPackageCreated = $true
    Set-TaskDirectoryAcl $taskPackageRoot $null 'PackageRoot'
    Assert-TaskPlainTree $taskPackageRoot

    $taskCurrentStage = 'scm_create'
    Assert-TaskProtectedService $script:ProtectedBaseline
    Assert-TaskVm
    $script:taskServiceExe = Join-Path $taskAppDirectory 'HostAgent.PipeProbe.exe'
    $taskBinPath = '"' + $script:taskServiceExe + '" isolated-scm-service ' + $taskId + ' ' + $taskOperatorSid.Value + ' crash-proof'
    $script:taskServiceCreationAttempted = $true
    $taskCreateArgs = @(Get-IsolatedScmCreateArguments $taskServiceName $taskBinPath ('HostAgent DEV SCM ' + $taskId))
    $taskCreateInfo = [Diagnostics.ProcessStartInfo]::new()
    $taskCreateInfo.FileName = Join-Path $env:WINDIR 'System32\sc.exe'
    $taskCreateInfo.Arguments = ConvertTo-IsolatedScmNativeCommandLine $taskCreateArgs
    $taskCreateInfo.UseShellExecute = $false
    $taskCreateInfo.CreateNoWindow = $true
    $taskCreateInfo.RedirectStandardOutput = $true
    $taskCreateInfo.RedirectStandardError = $true
    $taskCreateProcess = [Diagnostics.Process]::Start($taskCreateInfo)
    $taskCreateOutput = $taskCreateProcess.StandardOutput.ReadToEndAsync()
    $taskCreateError = $taskCreateProcess.StandardError.ReadToEndAsync()
    $taskCreateWatch = [Diagnostics.Stopwatch]::StartNew()
    try {
        while (-not $taskCreateProcess.HasExited) {
            Assert-TaskSafetyFloor
            if ($taskCreateWatch.Elapsed.TotalSeconds -ge 15) { throw 'isolated_sc_create_timeout' }
            Start-Sleep -Milliseconds 100
            $taskCreateProcess.Refresh()
        }
        $taskCreateProcess.WaitForExit()
        $taskRunResult.scmCreateExitCode = $taskCreateProcess.ExitCode
        [void]$taskCreateOutput.GetAwaiter().GetResult()
        [void]$taskCreateError.GetAwaiter().GetResult()
        if ($taskCreateProcess.ExitCode -ne 0) { throw 'isolated_sc_create_failed' }
    }
    finally {
        if (-not $taskCreateProcess.HasExited) {
            $taskCreateProcess.Kill()
            [void]$taskCreateProcess.WaitForExit(5000)
        }
        $taskCreateProcess.Dispose()
    }
    $script:taskServiceCreated = $true
    $taskCurrentStage = 'candidate_acl'
    $taskServiceSid = ([Security.Principal.NTAccount]::new('NT SERVICE', $taskServiceName)).Translate([Security.Principal.SecurityIdentifier])
    $script:taskServiceSid = $taskServiceSid
    Set-TaskDirectoryAcl $taskPackageRoot $null 'PackageRoot'
    New-Item -ItemType Directory -Path $taskAppDirectory | Out-Null
    New-Item -ItemType Directory -Path $taskDataDirectory | Out-Null
    Set-TaskDirectoryAcl $taskAppDirectory $taskServiceSid 'App'
    Set-TaskDirectoryAcl $taskDataDirectory $taskServiceSid 'Data'
    Get-ChildItem -LiteralPath $taskCandidatePath -Force | Copy-Item -Destination $taskAppDirectory -Recurse -Force
    Get-ChildItem -LiteralPath $taskAppDirectory -File -Recurse -Force | ForEach-Object { Set-TaskFileAcl $_.FullName $taskServiceSid }
    Assert-TaskManifest $taskAppDirectory | Out-Null
    $taskCanaryBytes = [Text.Encoding]::UTF8.GetBytes('HostAgent isolated SCM ACL canary v1')
    foreach ($taskCanaryName in @('.scm-acl-write-canary', '.scm-acl-delete-canary')) {
        $taskCanaryPath = Join-Path $taskAppDirectory $taskCanaryName
        [IO.File]::WriteAllBytes($taskCanaryPath, $taskCanaryBytes)
        Set-TaskFileAcl $taskCanaryPath $taskServiceSid
    }
    $script:taskCanarySha256 = (Get-FileHash -LiteralPath (Join-Path $taskAppDirectory '.scm-acl-write-canary') -Algorithm SHA256).Hash
    $taskRunResult.serviceSid = $taskServiceSid.Value
    $taskRunResult.serviceImageSha256 = (Get-FileHash -LiteralPath $script:taskServiceExe -Algorithm SHA256).Hash

    $taskCreatedService = Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'" -ErrorAction Stop
    $taskExpectedPrefix = '"' + $script:taskServiceExe + '" isolated-scm-service ' + $taskId + ' ' + $taskOperatorSid.Value + ' crash-proof'
    if (-not $taskCreatedService -or $taskCreatedService.StartName -ne ('NT SERVICE\' + $taskServiceName) -or
        $taskCreatedService.StartMode -ne 'Manual' -or $taskCreatedService.PathName -ne $taskExpectedPrefix) {
        throw 'isolated_scm_configuration_mismatch'
    }
    $taskSidOutput = & "$env:WINDIR\System32\sc.exe" showsid $taskServiceName 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $taskSidOutput -notmatch [regex]::Escape($taskServiceSid.Value)) { throw 'isolated_service_sid_mismatch' }

    Add-Type -Path (Join-Path $PSScriptRoot 'IsolatedScmRecovery.cs')
    Assert-TaskProtectedService $script:ProtectedBaseline
    Assert-TaskIsolatedService | Out-Null
    [IsolatedScmRecovery]::Set($taskId,$true)
    $taskRunResult.recoveryPolicy = 'one_restart_after_5000ms_then_none'
    $taskCurrentStage = 'memory_watchdog_start'
    Start-TaskMemoryWatchdog
    $taskCurrentStage = 'first_start'
    Invoke-TaskSc 'start'
    $taskFirst = Wait-TaskServiceState 'Running' 60
    $taskFirstPid = [int]$taskFirst.ProcessId
    Invoke-TaskProbe 'view'
    Assert-TaskAclCanaries
    $taskFirstData = Wait-TaskDatabaseReady 90
    $taskRunResult.firstProcessId = $taskFirstPid
    if ($WatchdogStopProbe) {
        $taskCurrentStage = 'bounded_watchdog_pressure'
        $taskRunResult.scenario = 'isolated-scm-forced-watchdog-stop'
        $taskRunResult.pressure = Invoke-TaskWatchdogPressure
        $taskWatchdogProbeValidated = [bool]$taskRunResult.pressure.confirmed
        if (-not $taskWatchdogProbeValidated) { throw 'watchdog_stop_probe_unconfirmed' }
        $taskRunResult.outcome = 'PASS'
    } else {
    $taskCurrentStage = 'controlled_crash_recovery'
    $taskRunResult.scenario = 'isolated-scm-automatic-crash-recovery'
    $taskRunResult.crash = Invoke-TaskControlledCrash
    $taskRunResult.outcome = 'PASS'
    }
}
catch {
    $taskRunResult.outcome = 'FAIL'
    $taskRunResult.failureCode = 'failure_' + $taskCurrentStage
    $taskRunResult.failureReason = if ($_.Exception.Message -match '^[a-z][a-z0-9_]{0,95}$') {
        $_.Exception.Message
    } else { 'unexpected_exception' }
}
finally {
    $taskMemoryBreached = Test-Path -LiteralPath $script:taskMemoryMarkerPath -PathType Leaf
    if ($taskMemoryBreached) {
        if ($script:taskMemoryWatchdog -and -not $script:taskMemoryWatchdog.HasExited) {
            [void]$script:taskMemoryWatchdog.WaitForExit(12000)
        }
        try { $taskRunResult.memoryGuard = Get-Content -LiteralPath $script:taskMemoryMarkerPath -Raw | ConvertFrom-Json }
        catch { $taskRunResult.memoryGuard = 'breach_marker_unreadable' }
        $taskRunResult.outcome = 'FAIL'
        if (-not $taskRunResult.failureCode) { $taskRunResult.failureCode = 'memory_floor_breached' }
    }
    if (-not $script:taskServiceCreated -and $script:taskServiceCreationAttempted) {
        $taskPossibleService = Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'" -ErrorAction SilentlyContinue
        $taskPossibleImage = '"' + $script:taskServiceExe + '" isolated-scm-service ' + $taskId + ' ' + $taskOperatorSid.Value + ' crash-proof'
        if ($taskPossibleService -and $taskPossibleService.PathName -eq $taskPossibleImage) { $script:taskServiceCreated = $true }
        elseif ($taskPossibleService) { $script:taskUnexpectedRegistration = $true }
    }
    if ($taskServiceCreated) {
        try {
            Assert-TaskProtectedService $script:ProtectedBaseline
            Assert-TaskIsolatedService | Out-Null
            [IsolatedScmRecovery]::Set($taskId,$false)
            $taskAutoRecoveryQuiesced = $true
        } catch {
            $taskRunResult.outcome = 'FAIL'
            $taskRunResult.failureCode = 'automatic_recovery_disable_unconfirmed'
        }
    }
    if ($taskServiceCreated -and $CrashRecoveryProbe) {
        # Preserve this probe's registration, app and data even on failure.
        # Any controller fallback stop is explicitly NOT watchdog success.
        try {
            $taskRetainedService = Assert-TaskIsolatedService
            $taskRunResult.controllerFallbackStopUsed = ($taskRetainedService.State -ne 'Stopped')
            if ($taskRunResult.controllerFallbackStopUsed) {
                $taskWatchdogProbeValidated = $false
                $taskRunResult.outcome = 'FAIL'
                $taskRunResult.failureCode = 'watchdog_stop_probe_controller_fallback'
                if (-not (Ensure-TaskStoppedAfterMemoryBreach)) { throw 'isolated_stop_unconfirmed' }
            }
            $taskRunResult.cleanup = 'crash_probe_registration_package_and_data_preserved'
            $taskRunResult.retainedPackagePath = $taskPackageRoot
            $taskRunResult.retainedDataPath = $taskDataDirectory
        } catch {
            $taskWatchdogProbeValidated = $false
            $taskRunResult.outcome = 'FAIL'
            $taskRunResult.failureCode = 'crash_probe_final_stop_unconfirmed'
            $taskRunResult.cleanup = 'crash_probe_evidence_preserved_manual_recovery_required'
        }
    }
    elseif ($taskServiceCreated -and $taskMemoryBreached) {
        $taskStopConfirmed = $false
        try { $taskStopConfirmed = Ensure-TaskStoppedAfterMemoryBreach }
        catch { $taskStopConfirmed = $false }
        if ($taskStopConfirmed) {
            $taskRunResult.cleanup = 'memory_floor_breached_test_service_stopped_registration_and_data_preserved'
        } else {
            $taskRunResult.cleanup = 'memory_floor_breached_test_service_stop_unconfirmed_registration_and_data_preserved'
            $taskRunResult.failureCode = 'memory_floor_breached_test_service_stop_unconfirmed'
        }
        $taskRunResult.outcome = 'FAIL'
        if (-not $taskRunResult.failureCode) { $taskRunResult.failureCode = 'memory_floor_breached' }
    }
    elseif ($taskServiceCreated) {
        try {
            $taskCurrent = Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'" -ErrorAction SilentlyContinue
            if ($taskCurrent -and $taskCurrent.State -ne 'Stopped') {
                Assert-TaskSafetyFloor
                Assert-TaskProtectedService $script:ProtectedBaseline
                Assert-TaskIsolatedService | Out-Null
                & "$env:WINDIR\System32\sc.exe" stop $taskServiceName | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'cleanup_stop_failed' }
                Wait-TaskServiceState 'Stopped' 45 | Out-Null
            }
            $taskCurrent = Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'" -ErrorAction SilentlyContinue
            if ($taskCurrent -and $taskCurrent.State -eq 'Stopped') {
                Assert-TaskSafetyFloor
                Assert-TaskProtectedService $script:ProtectedBaseline
                Assert-TaskIsolatedService | Out-Null
                & "$env:WINDIR\System32\sc.exe" delete $taskServiceName | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'cleanup_delete_failed' }
                $taskDeleteWatch = [Diagnostics.Stopwatch]::StartNew()
                do {
                    Start-Sleep -Milliseconds 500
                    $taskCurrent = Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'" -ErrorAction SilentlyContinue
                } while ($null -ne $taskCurrent -and $taskDeleteWatch.Elapsed.TotalSeconds -lt 10)
                if ($null -ne $taskCurrent) { throw 'cleanup_service_still_registered' }
                $script:taskServiceCreated = $false
            }
            else { throw 'cleanup_service_state_unknown' }
        }
        catch {
            $taskRunResult.cleanup = 'service_or_data_preserved_for_manual_recovery'
            if (-not $taskRunResult.failureCode) { $taskRunResult.failureCode = 'cleanup_incomplete' }
            $taskRunResult.outcome = 'FAIL'
        }
    }
    if ($script:taskUnexpectedRegistration) {
        $taskRunResult.cleanup = 'unexpected_service_registration_preserved_for_review'
        $taskRunResult.outcome = 'FAIL'
        if (-not $taskRunResult.failureCode) { $taskRunResult.failureCode = 'unexpected_service_registration' }
    }
    elseif (-not $script:taskServiceCreated -and -not $script:taskPackageCreated) {
        $taskRunResult.cleanup = 'no_temporary_service_or_package_created'
    }
    elseif (-not $script:taskServiceCreated) {
        $taskRunResult.cleanup = 'new_guid_package_preserved_no_service_created'
    }
    if (-not $taskMemoryBreached -and (Test-Path -LiteralPath $script:taskMemoryMarkerPath -PathType Leaf)) {
        $taskMemoryBreached = $true
        try { $taskRunResult.memoryGuard = Get-Content -LiteralPath $script:taskMemoryMarkerPath -Raw | ConvertFrom-Json }
        catch { $taskRunResult.memoryGuard = 'breach_marker_unreadable' }
        $taskRunResult.outcome = 'FAIL'
        if (-not $taskRunResult.failureCode) { $taskRunResult.failureCode = 'memory_floor_breached' }
    }
    try { Assert-TaskProtectedService $script:ProtectedBaseline; $taskRunResult.protectedServiceUnchanged = $true }
    catch { $taskRunResult.protectedServiceUnchanged = $false; $taskRunResult.outcome = 'FAIL'; $taskRunResult.failureCode = 'protected_service_postcheck_failed' }
    try { Stop-TaskMemoryWatchdog }
    catch {
        $taskRunResult.cleanup = 'memory_watchdog_shutdown_incomplete'
        $taskRunResult.outcome = 'FAIL'
        if (-not $taskRunResult.failureCode) { $taskRunResult.failureCode = 'memory_watchdog_shutdown_incomplete' }
    }
    if ($WatchdogStopProbe -and $taskWatchdogProbeValidated -and $taskRunResult.protectedServiceUnchanged -eq $true -and
        $taskRunResult.cleanup -eq 'crash_probe_registration_package_and_data_preserved') {
        # A breach is expected ONLY in this explicit probe, after positive watchdog-only attribution.
        $taskRunResult.outcome = 'PASS'
        $taskRunResult.failureCode = $null
        $taskRunResult.failureReason = $null
    }
    $taskRunResult.recoveryDisabled = $taskAutoRecoveryQuiesced
    Write-TaskEvidence
    $taskRunResult | ConvertTo-Json -Depth 6
}

if ($taskRunResult.outcome -ne 'PASS') { throw ('isolated_scm_test_failed:' + $taskRunResult.failureCode) }
return
