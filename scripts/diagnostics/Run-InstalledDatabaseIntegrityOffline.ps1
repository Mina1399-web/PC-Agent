[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$taskRunId = [guid]::NewGuid().ToString('N')
$taskEvidenceRoot = Join-Path $env:LOCALAPPDATA 'HostAgent-DEV-evidence'
$taskRunRoot = Join-Path $taskEvidenceRoot ('installed-db-integrity-' + $taskRunId)
$taskWorkRoot = Join-Path $taskRunRoot 'db-copies'
$taskPython = 'C:\Users\AgentPCdevtest\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
$taskPythonHash = '73DBF1F40507275D6FA636B49EB80B4753E313F649F52591ABBA86B31F1A1BCE'
$taskManifestPath = 'C:\Users\AgentPCdevtest\Documents\ChatGPT\DevTest VM PC\HostAgent-DEV-releases\SignPath-1.2.8-82465b4-candidate\manifest.json'
$taskManifestHash = '5DCF79DC3284B4937EBA64D4F50A7831A1FFE4F0891AE4D4B190A500B136B25A'
$taskMemorySoftMiB = 768
$taskMemoryHardMiB = 512
$taskDiskFloorBytes = 10GB
$taskDbCapBytes = 1GB
$taskStopTimeoutSeconds = 30
$taskStartTimeoutSeconds = 45
$taskServices = @(
    [pscustomobject]@{ Name='HostAgent'; State=$null; StartMode='Auto'; StartName='NT SERVICE\HostAgent'; PathName='"C:\Program Files\HostAgent\Service\HostAgent.Service.exe"'; ExecutablePath='C:\Program Files\HostAgent\Service\HostAgent.Service.exe'; ExecutableSha256='A4C8FEADE850E35DFB0FD4D3E0B5FC5DB428BD6E3E88A05AEDC973C4900FFB8C'; OriginallyRunning=$false; StopRequested=$false },
    [pscustomobject]@{ Name='HostAgent.Helper'; State=$null; StartMode='Auto'; StartName='LocalSystem'; PathName='"C:\Program Files\HostAgent\Helper\HostAgent.Helper.exe"'; ExecutablePath='C:\Program Files\HostAgent\Helper\HostAgent.Helper.exe'; ExecutableSha256='96E804D80DBFF3F09822C32DDADD5204AFE737EFD81898887AB519A555B8A99D'; OriginallyRunning=$false; StopRequested=$false }
)
$taskDatabases = @(
    [pscustomobject]@{ Name='host-agent'; Path='C:\ProgramData\HostAgent\host-agent.db'; Service='HostAgent'; Status='NOTRUN'; FailureCode=$null; QuickCheck=$null; SourceBytes=0; CopyVerified=$false },
    [pscustomobject]@{ Name='notifications'; Path='C:\ProgramData\HostAgent\notifications.db'; Service='HostAgent'; Status='NOTRUN'; FailureCode=$null; QuickCheck=$null; SourceBytes=0; CopyVerified=$false },
    [pscustomobject]@{ Name='helper-journal'; Path='C:\ProgramData\HostAgent.Helper\helper-journal.db'; Service='HostAgent.Helper'; Status='NOTRUN'; FailureCode=$null; QuickCheck=$null; SourceBytes=0; CopyVerified=$false }
)
$taskChild = $null
$taskServicesStopped = $false
$taskFailureCode = $null
$taskCleanupFailures = [System.Collections.Generic.List[string]]::new()
$taskResult = $null
$taskIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()

function Get-TaskFreeMemoryMiB {
    return [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024, 1)
}

function Assert-TaskRuntimeFloors {
    if ((Get-TaskFreeMemoryMiB) -le $taskMemoryHardMiB) { throw 'hard_memory_floor' }
    if ((Get-TaskFreeMemoryMiB) -lt $taskMemorySoftMiB) { throw 'soft_memory_floor' }
    if ((Get-PSDrive -Name C).Free -lt $taskDiskFloorBytes) { throw 'disk_floor' }
}

function Get-TaskServiceSnapshot([string]$Name) {
    $taskSvc = Get-CimInstance Win32_Service -Filter "Name='$Name'"
    if ($null -eq $taskSvc) { throw 'service_missing' }
    return $taskSvc
}

function Assert-TaskServiceIdentity($Expected, $Actual) {
    if ($Actual.Name -ne $Expected.Name -or $Actual.StartMode -ne $Expected.StartMode -or
        $Actual.StartName -ne $Expected.StartName -or $Actual.PathName -ne $Expected.PathName -or
        (Get-FileHash -LiteralPath $Expected.ExecutablePath -Algorithm SHA256).Hash -cne $Expected.ExecutableSha256) {
        throw 'service_identity_guard_failed'
    }
}

function Wait-TaskServiceState([string]$Name, [string]$Expected, [int]$TimeoutSeconds, [bool]$ForRecovery = $false) {
    $taskTimer = [Diagnostics.Stopwatch]::StartNew()
    while ($taskTimer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if (-not $ForRecovery) { Assert-TaskRuntimeFloors }
        $taskSvc = Get-TaskServiceSnapshot $Name
        if ($taskSvc.State -eq $Expected) { return $taskSvc }
        Start-Sleep -Milliseconds 250
    }
    throw 'service_state_timeout'
}

function Get-TaskFixedFile([string]$Path) {
    if (-not [IO.File]::Exists($Path)) { return $null }
    $taskFile = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    $taskAncestor = $taskFile
    while ($null -ne $taskAncestor) {
        if (($taskAncestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'database_reparse_point' }
        if ($taskAncestor -is [IO.FileInfo]) { $taskAncestor = $taskAncestor.Directory }
        else { $taskAncestor = $taskAncestor.Parent }
    }
    return $taskFile
}

function Assert-TaskPlainCopyTree([string]$Path) {
    $taskRootItem = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (($taskRootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'copy_tree_reparse_point' }
    foreach ($taskItem in Get-ChildItem -LiteralPath $Path -Force -Recurse -ErrorAction Stop) {
        if (($taskItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'copy_tree_reparse_point' }
    }
}

function Copy-TaskFileBounded([string]$Source, [string]$Destination) {
    $taskInput = $null
    $taskOutput = $null
    try {
        $taskInput = [IO.File]::Open($Source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $taskOutput = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $taskBuffer = [byte[]]::new(1MB)
        while (($taskRead = $taskInput.Read($taskBuffer, 0, $taskBuffer.Length)) -gt 0) {
            Assert-TaskRuntimeFloors
            $taskOutput.Write($taskBuffer, 0, $taskRead)
        }
        $taskOutput.Flush($true)
    }
    finally {
        if ($null -ne $taskOutput) { $taskOutput.Dispose() }
        if ($null -ne $taskInput) { $taskInput.Dispose() }
    }
}

try {
    $taskIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $taskPrincipal = [Security.Principal.WindowsPrincipal]::new($taskIdentity)
    if (-not $taskPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'elevated_operator_required' }
    if ([IntPtr]::Size -ne 8) { throw 'x64_required' }
    if ($env:COMPUTERNAME -ne 'AGENTPC' -or $taskIdentity.Name -ne 'AgentPC\AgentPCdevtest' -or
        (Get-CimInstance Win32_ComputerSystem).Model -ne 'VirtualBox' -or
        (Get-CimInstance Win32_ComputerSystemProduct).UUID -ne '46543236-8C5E-49DA-953C-DB17F9B1E402' -or
        (Get-Service -Name VBoxService -ErrorAction Stop).Status -ne 'Running') { throw 'approved_vm_identity_required' }
    if ((Get-TaskFreeMemoryMiB) -lt 1024) { throw 'initial_memory_floor' }
    if ((Get-PSDrive -Name C).Free -lt $taskDiskFloorBytes) { throw 'initial_disk_floor_10_gib' }
    if (-not [IO.File]::Exists($taskManifestPath) -or
        (Get-FileHash -LiteralPath $taskManifestPath -Algorithm SHA256).Hash -ne $taskManifestHash) { throw 'release_manifest_hash_mismatch' }
    $taskManifest = Get-Content -LiteralPath $taskManifestPath -Raw -ErrorAction Stop | ConvertFrom-Json
    if ($taskManifest.version -ne '1.2.8' -or $taskManifest.sourceCommit -ne '82465b48d6137b4f81a57f33900f3da69c194658') { throw 'release_manifest_identity_mismatch' }
    foreach ($taskExpectedService in $taskServices) {
        $taskRelative = if ($taskExpectedService.Name -eq 'HostAgent') { 'Service/HostAgent.Service.exe' } else { 'Helper/HostAgent.Helper.exe' }
        $taskManifestEntry = @($taskManifest.files | Where-Object { $_.path -ceq $taskRelative })
        if ($taskManifestEntry.Count -ne 1 -or $taskManifestEntry[0].sha256 -cne $taskExpectedService.ExecutableSha256) { throw 'release_manifest_service_hash_mismatch' }
    }
    if (-not [IO.File]::Exists($taskPython) -or
        (Get-FileHash -LiteralPath $taskPython -Algorithm SHA256).Hash -ne $taskPythonHash) { throw 'pinned_python_unavailable' }

    foreach ($taskExpectedService in $taskServices) {
        $taskSvc = Get-TaskServiceSnapshot $taskExpectedService.Name
        Assert-TaskServiceIdentity $taskExpectedService $taskSvc
        if ($taskSvc.State -notin @('Running','Stopped')) { throw 'service_identity_guard_failed' }
        $taskExpectedService.State = $taskSvc.State
        $taskExpectedService.OriginallyRunning = $taskSvc.State -eq 'Running'
    }

    $taskTotalSourceBytes = [int64]0
    foreach ($taskDb in $taskDatabases) {
        $taskMain = Get-TaskFixedFile $taskDb.Path
        if ($null -eq $taskMain) { continue }
        $taskDb.SourceBytes = [int64]$taskMain.Length
        foreach ($taskSuffix in @('-wal','-shm','-journal')) {
            $taskSidecar = Get-TaskFixedFile ($taskDb.Path + $taskSuffix)
            if ($null -ne $taskSidecar) { $taskDb.SourceBytes += [int64]$taskSidecar.Length }
        }
        $taskTotalSourceBytes += $taskDb.SourceBytes
    }
    foreach ($taskRequiredName in @('host-agent','notifications')) {
        if (-not [IO.File]::Exists(($taskDatabases | Where-Object Name -eq $taskRequiredName).Path)) {
            throw 'required_database_missing'
        }
    }
    if ($taskTotalSourceBytes -gt $taskDbCapBytes) { throw 'database_copy_size_cap' }
    if ((Get-PSDrive -Name C).Free -lt ($taskDiskFloorBytes + $taskTotalSourceBytes + 256MB)) { throw 'disk_copy_headroom' }

    [void][IO.Directory]::CreateDirectory($taskEvidenceRoot)
    [void][IO.Directory]::CreateDirectory($taskRunRoot)
    [void][IO.Directory]::CreateDirectory($taskWorkRoot)
    Assert-TaskRuntimeFloors

    # Quiesce only the two fixed product services, HostAgent first to stop new helper calls.
    foreach ($taskName in @('HostAgent','HostAgent.Helper')) {
        $taskTarget = $taskServices | Where-Object Name -eq $taskName
        if ($taskTarget.OriginallyRunning) {
            $taskCurrent = Get-TaskServiceSnapshot $taskName
            Assert-TaskServiceIdentity $taskTarget $taskCurrent
            if ($taskCurrent.State -eq 'Running') {
                # Mark the attempt before SCM, so even a partially completed stop is recovered.
                $taskTarget.StopRequested = $true
                Stop-Service -Name $taskName -NoWait -ErrorAction Stop
                [void](Wait-TaskServiceState $taskName 'Stopped' $taskStopTimeoutSeconds)
            } elseif ($taskCurrent.State -ne 'Stopped') { throw 'service_state_changed_before_stop' }
        }
    }
    $taskServicesStopped = $true

    $taskPythonDbInputs = [System.Collections.Generic.List[string]]::new()
    foreach ($taskDb in $taskDatabases) {
        $taskMain = Get-TaskFixedFile $taskDb.Path
        if ($null -eq $taskMain) {
            $taskDb.FailureCode = 'database_file_missing'
            continue
        }
        $taskCopyMain = Join-Path $taskWorkRoot ($taskDb.Name + '.db')
        Copy-TaskFileBounded $taskDb.Path $taskCopyMain
        if ((Get-FileHash -LiteralPath $taskDb.Path -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $taskCopyMain -Algorithm SHA256).Hash) { throw 'database_copy_hash_mismatch' }
        foreach ($taskSuffix in @('-wal','-shm','-journal')) {
            $taskSourceSidecar = Get-TaskFixedFile ($taskDb.Path + $taskSuffix)
            if ($null -ne $taskSourceSidecar) {
                $taskSourceSidecarPath = $taskDb.Path + $taskSuffix
                $taskCopySidecarPath = $taskCopyMain + $taskSuffix
                Copy-TaskFileBounded $taskSourceSidecarPath $taskCopySidecarPath
                if ((Get-FileHash -LiteralPath $taskSourceSidecarPath -Algorithm SHA256).Hash -ne
                    (Get-FileHash -LiteralPath $taskCopySidecarPath -Algorithm SHA256).Hash) { throw 'database_copy_hash_mismatch' }
            }
        }
        Assert-TaskRuntimeFloors
        $taskDb.CopyVerified = $true
        $taskPythonDbInputs.Add($taskCopyMain)
    }

    if ($taskPythonDbInputs.Count -eq 0) { throw 'no_database_files_found' }
    Assert-TaskRuntimeFloors
    $taskJsonInputs = ConvertTo-Json -InputObject @($taskPythonDbInputs.ToArray()) -Compress
    $taskCode = @'
import json, pathlib, sqlite3, sys, time
paths = json.loads(r'''__DATABASE_PATHS__''')
results = []
for value in paths:
    path = pathlib.Path(value)
    item = {"database": path.name, "quickCheck": "NOTRUN", "failureCode": None}
    connection = None
    deadline = time.monotonic() + 30.0
    try:
        if not path.is_file():
            item["failureCode"] = "copy_missing"
        elif path.stat().st_size > 1073741824:
            item["failureCode"] = "size_guard"
        else:
            connection = sqlite3.connect(path.as_uri() + "?mode=ro", uri=True, timeout=2.0)
            connection.execute("PRAGMA query_only=ON")
            connection.execute("PRAGMA temp_store=MEMORY")
            connection.set_progress_handler(lambda: int(time.monotonic() >= deadline), 1000)
            rows = connection.execute("PRAGMA quick_check(1)").fetchmany(2)
            item["quickCheck"] = "ok" if rows == [("ok",)] else "FAILED"
    except sqlite3.Error:
        item["failureCode"] = "sqlite_operation_failed"
    except OSError:
        item["failureCode"] = "file_io_failed"
    finally:
        if connection is not None:
            connection.close()
    results.append(item)
print(json.dumps({"databases": results}, separators=(",", ":")))
'@.Replace('__DATABASE_PATHS__', $taskJsonInputs.Replace("'", "\'"))

    $taskStart = [Diagnostics.ProcessStartInfo]::new()
    $taskStart.FileName = $taskPython
    $taskStart.Arguments = '-I -B -'
    $taskStart.UseShellExecute = $false
    $taskStart.CreateNoWindow = $true
    $taskStart.RedirectStandardInput = $true
    $taskStart.RedirectStandardOutput = $true
    $taskStart.RedirectStandardError = $true
    $taskChild = [Diagnostics.Process]::new()
    $taskChild.StartInfo = $taskStart
    if (-not $taskChild.Start()) { throw 'sqlite_probe_start_failed' }
    $taskOut = $taskChild.StandardOutput.ReadToEndAsync()
    $taskErr = $taskChild.StandardError.ReadToEndAsync()
    $taskWrite = $taskChild.StandardInput.WriteAsync($taskCode)
    if (-not $taskWrite.Wait(5000)) { throw 'sqlite_probe_input_timeout' }
    $taskChild.StandardInput.Close()
    $taskTimer = [Diagnostics.Stopwatch]::StartNew()
    while (-not $taskChild.WaitForExit(250)) {
        Assert-TaskRuntimeFloors
        if ($taskTimer.Elapsed.TotalSeconds -gt 100) { throw 'sqlite_probe_timeout' }
    }
    if (-not $taskOut.Wait(2000) -or -not $taskErr.Wait(2000) -or $taskChild.ExitCode -ne 0 -or
        $taskOut.Result.Length -gt 8192 -or $taskErr.Result.Length -ne 0) { throw 'sqlite_probe_failed' }
    $taskSqlite = $taskOut.Result | ConvertFrom-Json
    foreach ($taskChecked in $taskSqlite.databases) {
        $taskDb = $taskDatabases | Where-Object { $_.Name -eq ([IO.Path]::GetFileNameWithoutExtension($taskChecked.database)) }
        if ($null -eq $taskDb) { throw 'sqlite_result_identity_mismatch' }
        $taskDb.QuickCheck = [string]$taskChecked.quickCheck
        $taskDb.FailureCode = $taskChecked.failureCode
        if ($taskDb.QuickCheck -eq 'ok') { $taskDb.Status = 'PASS' }
        elseif ($taskDb.QuickCheck -eq 'FAILED') { $taskDb.Status = 'FAIL' }
        else { $taskDb.Status = 'NOTRUN' }
    }
    if (@($taskDatabases | Where-Object { $_.Name -in @('host-agent','notifications') -and $_.Status -ne 'PASS' }).Count -gt 0 -or
        @($taskDatabases | Where-Object { $_.CopyVerified -and $_.Status -ne 'PASS' }).Count -gt 0) {
        throw 'database_quick_check_not_passed'
    }
    $taskResult = 'PASS'
}
catch {
    $taskAllowedFailures = @('elevated_operator_required','x64_required','approved_vm_identity_required','initial_memory_floor',
        'initial_disk_floor_10_gib','pinned_python_unavailable','service_missing','service_identity_guard_failed',
        'release_manifest_hash_mismatch','release_manifest_identity_mismatch','release_manifest_service_hash_mismatch',
        'hard_memory_floor','soft_memory_floor','disk_floor','service_state_timeout','service_state_changed_before_stop','database_reparse_point','copy_tree_reparse_point',
        'database_copy_size_cap','disk_copy_headroom','no_database_files_found','sqlite_probe_start_failed',
        'required_database_missing','database_copy_hash_mismatch','sqlite_probe_input_timeout','sqlite_probe_timeout',
        'sqlite_probe_failed','sqlite_result_identity_mismatch','database_quick_check_not_passed')
    if ($taskAllowedFailures -contains $_.Exception.Message) { $taskFailureCode = $_.Exception.Message }
    else { $taskFailureCode = 'unclassified_probe_failure' }
    if ($null -eq $taskResult) { $taskResult = 'FAIL' }
}
finally {
    if ($null -ne $taskChild) {
        if (-not $taskChild.HasExited) {
            try { $taskChild.Kill(); [void]$taskChild.WaitForExit(5000) } catch { [void]$taskCleanupFailures.Add('sqlite_child_stop_failed') }
        }
        $taskChild.Dispose()
    }
    foreach ($taskName in @('HostAgent.Helper','HostAgent')) {
        $taskTarget = $taskServices | Where-Object Name -eq $taskName
        if ($taskTarget.OriginallyRunning -and $taskTarget.StopRequested) {
            try {
                $taskCurrent = Get-TaskServiceSnapshot $taskName
                Assert-TaskServiceIdentity $taskTarget $taskCurrent
                if ($taskCurrent.State -eq 'Stop Pending') {
                    $taskCurrent = Wait-TaskServiceState $taskName 'Stopped' $taskStopTimeoutSeconds $true
                    Assert-TaskServiceIdentity $taskTarget $taskCurrent
                }
                if ($taskCurrent.State -eq 'Start Pending') {
                    [void](Wait-TaskServiceState $taskName 'Running' $taskStartTimeoutSeconds $true)
                } elseif ($taskCurrent.State -ne 'Running') {
                    if ($taskCurrent.State -ne 'Stopped') { throw 'service_state_changed_before_restore' }
                    Start-Service -Name $taskName -ErrorAction Stop
                    [void](Wait-TaskServiceState $taskName 'Running' $taskStartTimeoutSeconds $true)
                }
            } catch { [void]$taskCleanupFailures.Add(($taskName + '_restore_failed')) }
        }
    }
    foreach ($taskExpectedService in $taskServices) {
        try {
            $taskFinal = Get-TaskServiceSnapshot $taskExpectedService.Name
            if ($taskExpectedService.OriginallyRunning -and $taskFinal.State -ne 'Running') {
                [void]$taskCleanupFailures.Add(($taskExpectedService.Name + '_not_running_after_restore'))
            }
            if ($taskFinal.StartMode -ne $taskExpectedService.StartMode -or $taskFinal.StartName -ne $taskExpectedService.StartName -or
                $taskFinal.PathName -ne $taskExpectedService.PathName -or
                (Get-FileHash -LiteralPath $taskExpectedService.ExecutablePath -Algorithm SHA256).Hash -cne $taskExpectedService.ExecutableSha256) {
                [void]$taskCleanupFailures.Add(($taskExpectedService.Name + '_identity_changed'))
            }
        } catch { [void]$taskCleanupFailures.Add(($taskExpectedService.Name + '_postcheck_failed')) }
    }
    if ($taskCleanupFailures.Count -gt 0) { $taskResult = 'FAIL'; if ($null -eq $taskFailureCode) { $taskFailureCode = 'service_restore_or_identity_check_failed' } }
    $taskPreserveCopies = $taskResult -ne 'PASS'
    if (-not $taskPreserveCopies -and [IO.Directory]::Exists($taskWorkRoot)) {
        try { Assert-TaskPlainCopyTree $taskWorkRoot; Remove-Item -LiteralPath $taskWorkRoot -Recurse -Force -ErrorAction Stop } catch { $taskCleanupFailures.Add('temporary_copy_cleanup_failed'); $taskResult='FAIL'; $taskFailureCode='temporary_copy_cleanup_failed'; $taskPreserveCopies=$true }
    }
    $taskSummary = [pscustomobject]@{
        scenario = 'installed_database_integrity_offline_copy'
        runId = $taskRunId
        result = $taskResult
        failureCode = $taskFailureCode
        vm = $env:COMPUTERNAME
        account = if ($null -ne $taskIdentity) { $taskIdentity.Name } else { $null }
        servicesBefore = @($taskServices | ForEach-Object { [pscustomobject]@{ name=$_.Name; state=$_.State; originallyRunning=$_.OriginallyRunning; stopRequested=$_.StopRequested } })
        servicesAfter = @($taskServices | ForEach-Object { try { $s=Get-TaskServiceSnapshot $_.Name; [pscustomobject]@{name=$s.Name;state=$s.State;startMode=$s.StartMode;startName=$s.StartName;pathName=$s.PathName;processId=$s.ProcessId} } catch { [pscustomobject]@{name=$_.Name;state='unavailable'} } })
        databases = @($taskDatabases | ForEach-Object { [pscustomobject]@{ name=$_.Name; status=$_.Status; quickCheck=$_.QuickCheck; failureCode=$_.FailureCode; copyVerified=$_.CopyVerified } })
        serviceControl = 'restore_only_originally_running_fixed_services_with_a_stop_requested_by_this_run'
        databaseSourceMode = 'sources_copied_only_after_services_stopped; SQLite_quick_check_used_mode_ro_on_copies'
        requestedSourceDatabaseWrites = $false
            sourceDatabaseMayChangeDuringNormalGracefulShutdown = $true
        temporaryCopiesPreserved = [bool]$taskPreserveCopies
        cleanupFailures = @($taskCleanupFailures.ToArray())
        memoryFloorsMiB = [pscustomobject]@{ start=1024; soft=768; hard=512 }
        diskFloorGiB = 10
        evidenceDirectory = $taskRunRoot
        copyDirectory = if ($taskPreserveCopies -and [IO.Directory]::Exists($taskWorkRoot)) { $taskWorkRoot } else { $null }
    }
    if ([IO.Directory]::Exists($taskRunRoot)) {
        $taskSummaryPath = Join-Path $taskRunRoot 'result.json'
        $taskSummary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskSummaryPath -Encoding utf8
    }
    $taskSummary | ConvertTo-Json -Depth 8
}

if ($taskResult -ne 'PASS') { exit 1 }
