<#
.SYNOPSIS
Read-only PDH comparison under the elevated interactive operator account.
.DESCRIPTION
DEV VM only. Does not run as HostAgent, impersonate its token, change privileges,
groups, ACLs, services or configuration. Successful results are NOT service-token
proof. Emits native status codes, never measured counter values or raw errors.
Also reads the latest persisted disk reason and quick_check of three installed
databases using mode=ro (never immutable), bounded by size, time and memory.
Missing/unreadable WAL sidecars cause NOTRUN. No requested SQL data/schema,
checkpoint, recovery, configuration, service or ACL changes. Live read-only
SQLite may perform internal SHM coordination writes; this is not a guarantee
of zero filesystem writes. No checkpoint or recovery commands are issued.
Requires the pinned local Codex Python runtime; does not compile helper code.
Run manually in elevated 64-bit PowerShell. No automatic elevation or log writes.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskChild = $null
$taskChildStarted = $false
$taskRuntimeLock = $null

function Get-VerifiedService {
    $taskService = Get-CimInstance Win32_Service -Filter "Name='HostAgent'"
    $taskStatus = Get-Service -Name HostAgent
    if ($null -eq $taskService -or $taskService.State -ne 'Running' -or
        $taskStatus.Status -ne 'Running' -or $taskService.ProcessId -le 0 -or
        $taskService.StartName -ne 'NT SERVICE\HostAgent' -or
        $taskService.PathName -ne '"C:\Program Files\HostAgent\Service\HostAgent.Service.exe"' -or
        $taskService.StartMode -ne 'Auto') {
        throw 'service_identity_guard_failed'
    }
    return $taskService
}

try {
    $taskIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $taskPrincipal = [Security.Principal.WindowsPrincipal]::new($taskIdentity)
    if (-not $taskPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'elevated_operator_required'
    }
    if ([IntPtr]::Size -ne 8) { throw 'x64_required' }
    if ($taskIdentity.Name -ne 'AgentPC\AgentPCdevtest' -or
        $env:COMPUTERNAME -ne 'AGENTPC' -or
        (Get-CimInstance Win32_ComputerSystem).Model -ne 'VirtualBox' -or
        (Get-CimInstance Win32_ComputerSystemProduct).UUID -ne '46543236-8C5E-49DA-953C-DB17F9B1E402' -or
        (Get-Service VBoxService).Status -ne 'Running') {
        throw 'approved_vm_and_account_required'
    }
    $taskBefore = Get-VerifiedService
    if ((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024 -lt 1024) {
        throw 'initial_memory_guard'
    }
    if ((Get-PSDrive C).Free -lt 20GB) { throw 'initial_disk_guard' }
    $taskPython = 'C:\Users\AgentPCdevtest\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
    $taskExpectedHash = 'B07B03B9BA866A341A3C13EE69710FBC8D43D4CE2523DEE13F6DF5C25378D15B'
    $taskAncestor = Get-Item -LiteralPath $taskPython
    while ($null -ne $taskAncestor) {
        if (($taskAncestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'runtime_reparse_point_rejected'
        }
        $taskAncestor = if ($taskAncestor -is [IO.FileInfo]) { $taskAncestor.Directory } else { $taskAncestor.Parent }
    }
    $taskRuntimeLock = [IO.File]::Open($taskPython, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    if ((Get-FileHash -LiteralPath $taskPython -Algorithm SHA256).Hash -ne $taskExpectedHash) {
        throw 'runtime_hash_mismatch'
    }
    $taskCode = @'
import ctypes as C
from ctypes import wintypes as W
import json
import time
import sys
import sqlite3
import pathlib
import stat

REASONS = frozenset(('disk_io_query_unavailable', 'disk_io_counters_unavailable',
    'disk_io_sample_unavailable', 'disk_io_values_unavailable',
    'some_disk_io_counters_unavailable', 'disk_io_api_unavailable'))
CAP = 1024 * 1024 * 1024

def guard_files(path, pins=None):
    # Fail closed on links/junctions and oversized inputs before SQLite opens.
    for item in (path,) + tuple(path.parents):
        info = item.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, 'st_file_attributes', 0) & 0x400:
            raise ValueError('path_guard')
    with path.open('rb') as stream:
        header = stream.read(100)
    if len(header) != 100 or header[:16] != b'SQLite format 3\x00':
        raise ValueError('header_guard')
    wal_mode = header[18] == 2 or header[19] == 2
    total = path.stat().st_size
    for suffix in ('-wal', '-shm', '-journal'):
        sidecar = pathlib.Path(str(path) + suffix)
        try:
            info = sidecar.lstat()
        except FileNotFoundError:
            if wal_mode and suffix in ('-wal', '-shm'):
                raise ValueError('wal_sidecars_required')
            continue
        if stat.S_ISLNK(info.st_mode) or getattr(info, 'st_file_attributes', 0) & 0x400:
            raise ValueError('path_guard')
        if suffix == '-journal' and info.st_size:
            raise ValueError('rollback_journal_present')
        total += info.st_size
        if total > CAP:
            raise ValueError('size_guard')
        stream = sidecar.open('rb')
        try:
            stream.read(1)
            if pins is not None and wal_mode and suffix in ('-wal', '-shm'):
                # Windows CRT read handles allow concurrent reads/writes but
                # deny deletion, retaining existing sidecars through ro close.
                pins.append(stream)
                stream = None
        finally:
            if stream is not None:
                stream.close()
    if total > CAP:
        raise ValueError('size_guard')
    return wal_mode

def inspect_database(path, read_reason=False):
    # This helper accepts fixture paths for isolated review tests. Production
    # calls below use only the three fixed installed locations, never user input.
    output = {'quickCheck': 'NOTRUN', 'stage': 'file_guard'}
    if read_reason:
        output['installedDiskReason'] = {'status': 'NOTRUN', 'reason': None}
    connection = None
    pins = []
    deadline = time.monotonic() + 9.0
    stage = 'file_guard'
    try:
        wal_mode = guard_files(path, pins)
        stage = 'open_read_only'
        connection = sqlite3.connect(path.as_uri() + '?mode=ro', uri=True, timeout=0.1)
        connection.set_progress_handler(lambda: int(time.monotonic() >= deadline), 1000)
        connection.execute('PRAGMA query_only=ON')
        connection.execute('PRAGMA temp_store=MEMORY')
        connection.execute('PRAGMA busy_timeout=100')
        # Recheck before queries, including sidecars if journal mode changed.
        stage = 'wal_guard'
        if connection.execute('PRAGMA journal_mode').fetchone()[0].lower() == 'wal':
            if not guard_files(path):
                raise ValueError('journal_mode_changed')
        elif wal_mode:
            raise ValueError('journal_mode_changed')
        stage = 'quick_check'
        rows = connection.execute('PRAGMA quick_check(1)').fetchmany(2)
        output['quickCheck'] = 'ok' if rows == [('ok',)] else 'FAILED'
        if read_reason:
            stage = 'disk_reason'
            row = connection.execute("""SELECT
                CASE WHEN length(payload_json) <= 1048576
                THEN json_extract(payload_json,'$.Value.IoCounters.UnavailableReason')
                ELSE '__payload_size_guard__' END
                FROM telemetry_snapshots WHERE collector_id='disk'
                ORDER BY sampled_at_utc DESC LIMIT 1""").fetchone()
            if row is None:
                output['installedDiskReason'] = {'status': 'NOTRUN', 'reason': None, 'stage': 'no_disk_snapshot'}
            elif isinstance(row[0], str) and row[0] in REASONS:
                output['installedDiskReason'] = {'status': 'observed', 'reason': row[0]}
            elif row[0] is None:
                output['installedDiskReason'] = {'status': 'no_reason_recorded', 'reason': None}
            else:
                output['installedDiskReason'] = {'status': 'NOTRUN', 'reason': None, 'stage': 'reason_allowlist_guard'}
        if time.monotonic() >= deadline:
            raise TimeoutError()
        output['stage'] = 'complete'
    except Exception:
        # No exception text, rows or arbitrary persisted strings leave this helper.
        output['stage'] = stage + '_unavailable'
        if time.monotonic() >= deadline:
            output['stage'] = 'deadline'
        if read_reason and stage != 'disk_reason':
            output['installedDiskReason'] = {'status': 'NOTRUN', 'reason': None}
    finally:
        if connection is not None:
            connection.close()
        for stream in pins:
            stream.close()
    return output

paths = [r'\PhysicalDisk(_Total)\Disk Read Bytes/sec',
         r'\PhysicalDisk(_Total)\Disk Write Bytes/sec',
         r'\PhysicalDisk(_Total)\Avg. Disk sec/Transfer']
result = {'paths': [], 'collect': [], 'closeQuery': None}
query = C.c_void_p()
def hx(value):
    return '0x%08X' % (value & 0xffffffff)
try:
    pdh = C.WinDLL(r'C:\Windows\System32\pdh.dll')
    pdh.PdhOpenQueryW.argtypes = [W.LPCWSTR, C.c_size_t, C.POINTER(C.c_void_p)]
    pdh.PdhAddEnglishCounterW.argtypes = [C.c_void_p, W.LPCWSTR, C.c_size_t, C.POINTER(C.c_void_p)]
    pdh.PdhCollectQueryData.argtypes = [C.c_void_p]
    pdh.PdhCloseQuery.argtypes = [C.c_void_p]
    class Value(C.Union):
        _fields_ = [('doubleValue', C.c_double), ('longValue', W.LONG),
                    ('largeValue', C.c_longlong), ('stringValue', W.LPWSTR)]
    class Formatted(C.Structure):
        _fields_ = [('status', W.DWORD), ('value', Value)]
    if C.sizeof(Formatted) != 16 or Formatted.value.offset != 8:
        raise RuntimeError()
    pdh.PdhGetFormattedCounterValue.argtypes = [C.c_void_p, W.DWORD, C.POINTER(W.DWORD), C.POINTER(Formatted)]
    for name in ('PdhOpenQueryW', 'PdhAddEnglishCounterW', 'PdhCollectQueryData',
                 'PdhCloseQuery', 'PdhGetFormattedCounterValue'):
        getattr(pdh, name).restype = W.DWORD
    status = pdh.PdhOpenQueryW(None, 0, C.byref(query))
    result['openQuery'] = hx(status)
    if status == 0:
        handles = []
        for path in paths:
            handle = C.c_void_p()
            status = pdh.PdhAddEnglishCounterW(query, path, 0, C.byref(handle))
            row = {'path': path, 'addEnglishCounter': hx(status),
                   'getFormattedCounterValue': None, 'counterStatus': None}
            result['paths'].append(row)
            if status == 0:
                handles.append((handle, row))
        if handles:
            result['collect'].append({'stage': 'first', 'status': hx(pdh.PdhCollectQueryData(query))})
            time.sleep(1)
            result['collect'].append({'stage': 'second', 'status': hx(pdh.PdhCollectQueryData(query))})
            for handle, row in handles:
                value = Formatted()
                status = pdh.PdhGetFormattedCounterValue(handle, 0x200, None, C.byref(value))
                row['getFormattedCounterValue'] = hx(status)
                if status == 0:
                    row['counterStatus'] = hx(value.status)
except Exception:
    result['bridgeFailure'] = 'native_probe_failed'
finally:
    if query.value:
        result['closeQuery'] = hx(pdh.PdhCloseQuery(query))
result['databases'] = {}
for name, location in (
        ('host-agent', r'C:\ProgramData\HostAgent\host-agent.db'),
        ('notifications', r'C:\ProgramData\HostAgent\notifications.db'),
        ('helper-journal', r'C:\ProgramData\HostAgent.Helper\helper-journal.db')):
    result['databases'][name] = inspect_database(pathlib.Path(location), name == 'host-agent')
checks = [item['quickCheck'] for item in result['databases'].values()]
complete = [item['stage'] == 'complete' for item in result['databases'].values()]
reason_status = result['databases']['host-agent']['installedDiskReason']['status']
result['sqliteOverview'] = {
    'scope': 'database_quick_check_only_not_product_health',
    'checkedOk': checks.count('ok'),
    'failed': checks.count('FAILED'),
    'notRun': checks.count('NOTRUN'),
    'diskReasonStatus': reason_status,
    'status': ('FAILED' if 'FAILED' in checks else
               'INCOMPLETE' if 'NOTRUN' in checks or not all(complete) or reason_status == 'NOTRUN' else
               'QUICK_CHECKS_OK')}
print(json.dumps(result, separators=(',', ':')))
sys.exit(1 if 'bridgeFailure' in result else 0)
'@
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
    if (-not $taskChild.Start()) { throw 'probe_start_failed' }
    $taskChildStarted = $true
    $taskOutput = $taskChild.StandardOutput.ReadToEndAsync()
    $taskErrors = $taskChild.StandardError.ReadToEndAsync()
    $taskWrite = $taskChild.StandardInput.WriteAsync($taskCode)
    if (-not $taskWrite.Wait(5000)) { throw 'probe_input_timeout' }
    $taskChild.StandardInput.Close()
    $taskWatch = [Diagnostics.Stopwatch]::StartNew()
    while (-not $taskChild.WaitForExit(250)) {
        $taskFreeMiB = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024
        if ($taskFreeMiB -le 512) { throw 'hard_memory_guard' }
        if ($taskFreeMiB -lt 768) { throw 'soft_memory_guard' }
        if ($taskWatch.Elapsed.TotalSeconds -ge 40) { throw 'probe_timeout' }
    }
    if (-not $taskOutput.Wait(2000) -or -not $taskErrors.Wait(2000) -or
        $taskChild.ExitCode -ne 0 -or $taskOutput.Result.Length -gt 65536 -or
        $taskErrors.Result.Length -ne 0) { throw 'probe_bridge_failed' }
    $taskNative = $taskOutput.Result | ConvertFrom-Json
    $taskAfter = Get-VerifiedService
    if ($taskBefore.ProcessId -ne $taskAfter.ProcessId) { throw 'service_changed_during_probe' }
    [pscustomobject]@{
        scenario = 'installed_disk_pdh_admin_comparison_read_only'
        context = 'elevated_interactive_operator'
        approvedVm = $true
        approvedAccount = $true
        serviceTokenProof = $false
        requestedSqlDataSchemaCheckpointRecoveryChanges = $false
        requestedConfigurationServiceAclChanges = $false
        sqliteInternalShmCoordinationWritesPossible = $true
        memoryGuard = 'initial_1024_mib_inline_250ms_soft_768_hard_512_cancel_own_child_only'
        initialDiskGuard = '20_gib'
        service = [pscustomobject]@{
            name = $taskAfter.Name
            state = $taskAfter.State
            startMode = $taskAfter.StartMode
            startName = $taskAfter.StartName
            imagePath = $taskAfter.PathName
            processId = $taskAfter.ProcessId
            identityUnchangedDuringProbe = $true
        }
        runtimeSha256 = $taskExpectedHash
        native = $taskNative
    } | ConvertTo-Json -Depth 8
}
catch {
    # Never expose raw exceptions, native payloads or environment details.
    $taskAllowed = @('elevated_operator_required', 'x64_required', 'approved_vm_and_account_required',
        'service_identity_guard_failed', 'runtime_reparse_point_rejected', 'runtime_hash_mismatch',
        'probe_start_failed', 'probe_input_timeout', 'probe_timeout', 'probe_bridge_failed',
        'service_changed_during_probe')
    $taskAllowed += @('initial_memory_guard', 'initial_disk_guard', 'hard_memory_guard', 'soft_memory_guard')
    $taskFailure = 'probe_guard_or_dependency_failed'
    if ($taskAllowed -contains $_.Exception.Message) { $taskFailure = $_.Exception.Message }
    [pscustomobject]@{ scenario = 'installed_disk_pdh_admin_comparison_read_only';
        failure = $taskFailure; serviceTokenProof = $false } | ConvertTo-Json
    exit 1
}
finally {
    if ($null -ne $taskChild) {
        if ($taskChildStarted -and -not $taskChild.HasExited) {
            $taskChild.Kill()
            [void]$taskChild.WaitForExit(5000)
        }
        $taskChild.Dispose()
    }
    if ($null -ne $taskRuntimeLock) { $taskRuntimeLock.Dispose() }
}
