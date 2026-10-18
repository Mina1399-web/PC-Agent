$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Bounded S13 graceful-stop/recovery and read-only SQLite quick_check.
# This script targets only the installed HostAgent service in HostAgent-DEV.
# It opens SQLite only in read-only mode and does not edit service configuration or security policy.

$expectedComputer = 'AGENTPC'
$expectedServiceHash = '04DF669D662BB08BDBD76653E46158E3D701D7E0D48653439F8F836BB417CB87'
$serviceName = 'HostAgent'
$serviceExe = 'C:\Program Files\HostAgent\Service\HostAgent.Service.exe'
$databasePath = 'C:\ProgramData\HostAgent\host-agent.db'
$evidenceRoot = Join-Path $env:LOCALAPPDATA 'HostAgent-DEV-evidence\chaos-20261013T074135Z'
$phase = 'preflight'
$failureCode = $null
$stopIssued = $false
$stopResult = 'not_run'
$pipeWhileStopped = 'not_run'
$storageResult = 'not_run'
$recoveryResult = 'not_run'
$preView = $null
$postView = $null
$baselineAttempts = New-Object 'System.Collections.Generic.List[object]'
$databaseSize = $null
$databaseWalSize = 0L
$databaseShmSize = 0L
$safeVmContext = $false
$startedAtUtc = [DateTimeOffset]::UtcNow

function Wait-HostAgentState([string]$State, [int]$TimeoutSeconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $svc = Get-Service -Name $serviceName -ErrorAction Stop
        if ($svc.Status.ToString() -eq $State) { return $true }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    return $false
}

function Get-ViewProbe([int]$TimeoutMilliseconds) {
    $raw = [HostAgentChaosViewProbe20261015]::ReadOverview($TimeoutMilliseconds)
    $parts = $raw.Split('|', 4)
    [pscustomobject]@{
        Ok = ($parts[0] -eq 'ok')
        Stage = [string]$parts[1]
        ElapsedMs = [int]$parts[2]
        PayloadBase64 = [string]$parts[3]
    }
}

function Get-ViewSummary($Probe) {
    if (-not $Probe.Ok -or -not $Probe.PayloadBase64) {
        return [pscustomobject]@{ Available = $false; Code = $Probe.Stage; IncidentCount = $null; IncidentHashes = @() }
    }
    try {
        $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Probe.PayloadBase64))
        $reply = $json | ConvertFrom-Json -ErrorAction Stop
        if (-not $reply.ok -or $reply.code -ne 'overview' -or -not $reply.data) {
            return [pscustomobject]@{ Available = $false; Code = 'invalid_overview'; IncidentCount = $null; IncidentHashes = @() }
        }
        $hashes = New-Object 'System.Collections.Generic.List[string]'
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            foreach ($incident in @($reply.data.incidents)) {
                $id = [string]$incident.id
                if ($id) {
                    $digest = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($id))
                    $hashes.Add(([BitConverter]::ToString($digest)).Replace('-', ''))
                }
            }
        } finally { $sha.Dispose() }
        [pscustomobject]@{
            Available = $true
            Code = 'overview'
            IncidentCount = @($reply.data.incidents).Count
            IncidentHashes = @($hashes)
        }
    } catch {
        [pscustomobject]@{ Available = $false; Code = 'invalid_overview'; IncidentCount = $null; IncidentHashes = @() }
    }
}

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $computer = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
    $freeMiB = [math]::Round(([double]$os.FreePhysicalMemory / 1024), 1)
    $drive = New-Object System.IO.DriveInfo -ArgumentList 'C:\'
    $freeBytes = $drive.AvailableFreeSpace
    $freeGiB = [math]::Round(($freeBytes / 1GB), 2)

    if ($env:COMPUTERNAME -ne $expectedComputer -or $computer.Manufacturer -notmatch 'innotek' -or $computer.Model -notmatch 'VirtualBox') {
        throw 'preflight_wrong_vm'
    }
    $safeVmContext = $true
    if (-not $isAdmin) { throw 'preflight_not_elevated' }
    if ($freeMiB -lt 1024) { throw 'preflight_memory_below_1024_mib' }
    if ($freeGiB -lt 20) { throw 'preflight_disk_below_20_gib' }

    $service = Get-CimInstance Win32_Service -Filter "Name='HostAgent'" -ErrorAction Stop
    if (-not $service -or $service.State -ne 'Running' -or $service.StartMode -ne 'Auto' -or $service.StartName -ne 'NT SERVICE\HostAgent') {
        throw 'preflight_unexpected_service_state_or_identity'
    }
    if ($service.PathName -ne ('"' + $serviceExe + '"')) { throw 'preflight_unexpected_service_image_path' }
    $imageHash = (Get-FileHash -LiteralPath $serviceExe -Algorithm SHA256 -ErrorAction Stop).Hash
    if ($imageHash -ne $expectedServiceHash) { throw 'preflight_unexpected_service_image_hash' }
    if (-not (Test-Path -LiteralPath $databasePath -PathType Leaf)) { throw 'preflight_database_missing_or_unreadable' }
    $dbItem = Get-Item -LiteralPath $databasePath -ErrorAction Stop
    $databaseSize = [long]$dbItem.Length
    if ($databaseSize -gt 536870912) { throw 'preflight_database_over_512_mib' }
    if (-not (Test-Path -LiteralPath 'C:\Program Files\HostAgent\Service\e_sqlite3.dll' -PathType Leaf)) {
        throw 'preflight_sqlite_library_missing'
    }

    $pipeSource = @'
using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Diagnostics;
public static class HostAgentChaosViewProbe20261015 {
    private static void ReadExact(NamedPipeClientStream p, byte[] b, int count, CancellationToken token) {
        int offset = 0;
        while (offset < count) {
            int n = p.ReadAsync(b, offset, count - offset, token).GetAwaiter().GetResult();
            if (n <= 0) throw new EndOfStreamException();
            offset += n;
        }
    }
    public static string ReadOverview(int timeoutMs) {
        var watch = Stopwatch.StartNew();
        string stage = "connect";
        try {
            using (var cts = new CancellationTokenSource(timeoutMs))
            using (var p = new NamedPipeClientStream(".", "HostAgent.View.v1",
                PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Identification, HandleInheritability.None)) {
                p.Connect(timeoutMs);
                stage = "owner";
                var expected = (SecurityIdentifier)new NTAccount("NT SERVICE", "HostAgent").Translate(typeof(SecurityIdentifier));
                var actual = p.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
                if (actual == null || !expected.Equals(actual)) return "fail|owner_mismatch|" + watch.ElapsedMilliseconds + "|";
                stage = "request";
                byte[] request = Encoding.UTF8.GetBytes("{\"version\":1,\"command\":\"overview\",\"body\":{}} ".TrimEnd());
                byte[] header = BitConverter.GetBytes(request.Length);
                p.WriteAsync(header, 0, header.Length, cts.Token).GetAwaiter().GetResult();
                p.WriteAsync(request, 0, request.Length, cts.Token).GetAwaiter().GetResult();
                p.FlushAsync(cts.Token).GetAwaiter().GetResult();
                stage = "response_header";
                byte[] responseHeader = new byte[4];
                ReadExact(p, responseHeader, 4, cts.Token);
                int length = BitConverter.ToInt32(responseHeader, 0);
                if (length <= 0 || length > 16384) return "fail|invalid_frame|" + watch.ElapsedMilliseconds + "|";
                stage = "response_body";
                byte[] response = new byte[length];
                ReadExact(p, response, length, cts.Token);
                stage = "complete";
                return "ok|complete|" + watch.ElapsedMilliseconds + "|" + Convert.ToBase64String(response);
            }
        } catch (OperationCanceledException) {
            return "fail|timeout|" + watch.ElapsedMilliseconds + "|";
        } catch (EndOfStreamException) {
            return "fail|eof|" + watch.ElapsedMilliseconds + "|";
        } catch (UnauthorizedAccessException) {
            return "fail|access_denied|" + watch.ElapsedMilliseconds + "|";
        } catch {
            return "fail|" + stage + "_failed|" + watch.ElapsedMilliseconds + "|";
        }
    }
}
'@
    if (-not ('HostAgentChaosViewProbe20261015' -as [type])) {
        Add-Type -TypeDefinition $pipeSource -Language CSharp -ErrorAction Stop
    }

    $sqliteSource = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
public static class HostAgentChaosSqliteCheck20261015 {
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ExecCallback(IntPtr context, int columnCount, IntPtr values, IntPtr names);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ProgressCallback(IntPtr context);
    private sealed class DeadlineState {
        public readonly Stopwatch Watch = Stopwatch.StartNew();
        public readonly int TimeoutMilliseconds;
        public DeadlineState(int timeoutMilliseconds) { TimeoutMilliseconds = timeoutMilliseconds; }
    }
    [DllImport(@"C:\Program Files\HostAgent\Service\e_sqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPStr)] string filename, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(@"C:\Program Files\HostAgent\Service\e_sqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPStr)] string sql, ExecCallback callback, IntPtr context, out IntPtr errorMessage);
    [DllImport(@"C:\Program Files\HostAgent\Service\e_sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close_v2(IntPtr db);
    [DllImport(@"C:\Program Files\HostAgent\Service\e_sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void sqlite3_progress_handler(IntPtr db, int operationInterval, ProgressCallback callback, IntPtr context);
    [DllImport(@"C:\Program Files\HostAgent\Service\e_sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void sqlite3_free(IntPtr value);
    private static int Collect(IntPtr context, int columnCount, IntPtr values, IntPtr names) {
        var rows = GCHandle.FromIntPtr(context).Target as List<string>;
        if (rows != null && columnCount > 0) {
            IntPtr first = Marshal.ReadIntPtr(values);
            rows.Add(first == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(first));
        }
        return 0;
    }
    private static int CheckDeadline(IntPtr context) {
        var state = GCHandle.FromIntPtr(context).Target as DeadlineState;
        return state != null && state.Watch.ElapsedMilliseconds >= state.TimeoutMilliseconds ? 1 : 0;
    }
    public static string QuickCheck(string path, int timeoutMilliseconds) {
        IntPtr db = IntPtr.Zero;
        int open = sqlite3_open_v2(path, out db, 0x00000001, IntPtr.Zero); // SQLITE_OPEN_READONLY
        if (open != 0) { if (db != IntPtr.Zero) sqlite3_close_v2(db); return "open_failed|" + open; }
        IntPtr error = IntPtr.Zero;
        var rows = new List<string>();
        var handle = GCHandle.Alloc(rows);
        var deadline = new DeadlineState(timeoutMilliseconds);
        var deadlineHandle = GCHandle.Alloc(deadline);
        var progress = new ProgressCallback(CheckDeadline);
        try {
            var callback = new ExecCallback(Collect);
            sqlite3_progress_handler(db, 10000, progress, GCHandle.ToIntPtr(deadlineHandle));
            int result = sqlite3_exec(db, "PRAGMA quick_check;", callback, GCHandle.ToIntPtr(handle), out error);
            if (result == 9 && deadline.Watch.ElapsedMilliseconds >= timeoutMilliseconds) return "timeout|" + timeoutMilliseconds;
            if (result != 0) return "query_failed|" + result;
            if (rows.Count == 1 && String.Equals(rows[0], "ok", StringComparison.OrdinalIgnoreCase)) return "healthy|1";
            return "integrity_issue|" + rows.Count;
        } finally {
            sqlite3_progress_handler(db, 0, null, IntPtr.Zero);
            GC.KeepAlive(progress);
            if (error != IntPtr.Zero) sqlite3_free(error);
            deadlineHandle.Free();
            handle.Free();
            sqlite3_close_v2(db);
        }
    }
}
'@
    if (-not ('HostAgentChaosSqliteCheck20261015' -as [type])) {
        Add-Type -TypeDefinition $sqliteSource -Language CSharp -ErrorAction Stop
    }

    $phase = 'baseline_overview'
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $beforeProbe = Get-ViewProbe 12000
        $preView = Get-ViewSummary $beforeProbe
        $baselineAttempts.Add([pscustomobject]@{
            attempt = $attempt
            stage = $beforeProbe.Stage
            elapsedMs = $beforeProbe.ElapsedMs
            overviewAvailable = $preView.Available
            resultCode = $preView.Code
        })
        if ($preView.Available) { break }
        if ($attempt -lt 5) { Start-Sleep -Seconds 1 }
    }
    if (-not $preView.Available) { throw 'baseline_overview_unavailable' }

    $phase = 'controlled_stop'
    $stopIssued = $true
    & "$env:WINDIR\System32\sc.exe" stop $serviceName | Out-Null
    $stopExit = $LASTEXITCODE
    if ($stopExit -ne 0) { throw 'sc_stop_failed' }
    if (-not (Wait-HostAgentState 'Stopped' 30)) { throw 'service_stop_timeout' }
    $stopResult = 'stopped'

    $phase = 'pipe_unavailable_check'
    $stoppedProbe = Get-ViewProbe 1500
    $pipeWhileStopped = if ($stoppedProbe.Ok) { 'unexpectedly_available' } else { [string]$stoppedProbe.Stage }

    $phase = 'readonly_database_check'
    $storageRaw = [HostAgentChaosSqliteCheck20261015]::QuickCheck($databasePath, 45000)
    $storageParts = $storageRaw.Split('|', 2)
    $storageResult = [string]$storageParts[0]
    if ($storageParts.Count -gt 1) { $storageResult += ':' + [string]$storageParts[1] }
    foreach ($suffix in @('-wal', '-shm')) {
        $sidecar = $databasePath + $suffix
        if (Test-Path -LiteralPath $sidecar -PathType Leaf) {
            $sidecarSize = [long](Get-Item -LiteralPath $sidecar -ErrorAction Stop).Length
            if ($suffix -eq '-wal') { $databaseWalSize = $sidecarSize } else { $databaseShmSize = $sidecarSize }
        }
    }
} catch {
    if ($_.Exception.Message -match '^([a-z0-9_]+)$') { $failureCode = $Matches[1] }
    else { $failureCode = 'unexpected_test_error' }
} finally {
    if ($stopIssued) {
        try {
            $current = Get-Service -Name $serviceName -ErrorAction Stop
            if ($current.Status.ToString() -ne 'Running') {
                if ($current.Status.ToString() -eq 'StopPending') { [void](Wait-HostAgentState 'Stopped' 20) }
                & "$env:WINDIR\System32\sc.exe" start $serviceName | Out-Null
                $startExit = $LASTEXITCODE
                if ($startExit -ne 0 -and (Get-Service -Name $serviceName).Status.ToString() -ne 'Running') {
                    $recoveryResult = 'start_command_failed'
                } elseif (Wait-HostAgentState 'Running' 60) {
                    $recoveryResult = 'running'
                } else {
                    $recoveryResult = 'start_timeout'
                }
            } elseif ($current.Status.ToString() -eq 'Running') {
                $recoveryResult = 'running'
            }
        } catch { $recoveryResult = 'recovery_error' }
    }
}

if ($recoveryResult -eq 'running') {
    try {
        $afterService = Get-CimInstance Win32_Service -Filter "Name='HostAgent'" -ErrorAction Stop
        if ($afterService.State -ne 'Running' -or $afterService.StartName -ne 'NT SERVICE\HostAgent') {
            $recoveryResult = 'service_identity_or_state_mismatch'
        } else {
            $afterHash = (Get-FileHash -LiteralPath $serviceExe -Algorithm SHA256 -ErrorAction Stop).Hash
            if ($afterHash -ne $expectedServiceHash) { $recoveryResult = 'image_hash_changed' }
            else {
                $afterProbe = Get-ViewProbe 8000
                $postView = Get-ViewSummary $afterProbe
                if ($postView.Available) { $recoveryResult = 'running_and_view_available' }
                else { $recoveryResult = 'running_but_view_unavailable:' + $postView.Code }
            }
        }
    } catch { $recoveryResult = 'post_recovery_check_error' }
}

$overlap = $null
$newIncidents = $null
$missingIncidents = $null
if ($preView -and $postView -and $preView.Available -and $postView.Available) {
    $afterSet = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($hash in $postView.IncidentHashes) { [void]$afterSet.Add($hash) }
    $overlap = 0
    foreach ($hash in $preView.IncidentHashes) { if ($afterSet.Contains($hash)) { $overlap++ } }
    $newIncidents = $postView.IncidentCount - $overlap
    $missingIncidents = $preView.IncidentCount - $overlap
}

$outcome = 'partial'
if ($failureCode) { $outcome = 'fail' }
elseif ($stopResult -eq 'stopped' -and $pipeWhileStopped -ne 'unexpectedly_available' -and
        $storageResult -like 'healthy:*' -and $recoveryResult -eq 'running_and_view_available') { $outcome = 'pass' }
elseif ($recoveryResult -notlike 'running*') { $outcome = 'fail' }

$baselineAttemptArray = [object[]]$baselineAttempts.ToArray()
$incidentCountBefore = $null
$incidentCountAfter = $null
if ($preView) { $incidentCountBefore = $preView.IncidentCount }
if ($postView) { $incidentCountAfter = $postView.IncidentCount }

$result = [ordered]@{
    scenario = 'S13-graceful-service-stop-readonly-storage-check-recovery'
    vm = $env:COMPUTERNAME
    model = [string]$computer.Model
    account = [string]([Security.Principal.WindowsIdentity]::GetCurrent().Name)
    elevated = $isAdmin
    startedAtUtc = $startedAtUtc.ToString('o')
    finishedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    outcome = $outcome
    failureCode = $failureCode
    freeMemoryMiBBefore = $freeMiB
    cFreeGiBBefore = $freeGiB
    imageSha256 = $imageHash
    databaseFile = 'host-agent.db'
    databaseSizeBytes = $databaseSize
    baselinePipeAttempts = $baselineAttemptArray
    databaseWalSizeBytesAfterStop = $databaseWalSize
    databaseShmSizeBytesAfterStop = $databaseShmSize
    stopResult = $stopResult
    pipeWhileStopped = $pipeWhileStopped
    storageQuickCheck = $storageResult
    recoveryResult = $recoveryResult
    incidentCountBefore = $incidentCountBefore
    incidentCountAfter = $incidentCountAfter
    incidentIdHashOverlap = $overlap
    incidentIdHashNew = $newIncidents
    incidentIdHashMissing = $missingIncidents
    rawPipePayloadSaved = $false
    storageOpenMode = 'read_only'
    databaseWritesRequested = $false
    serviceConfigurationChanged = $false
}

if ($safeVmContext) {
    if (-not (Test-Path -LiteralPath $evidenceRoot -PathType Container)) {
        New-Item -Path $evidenceRoot -ItemType Directory -Force | Out-Null
    }
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $artifact = Join-Path $evidenceRoot ('s13-admin-check-' + $stamp + '.json')
    $suffix = 1
    while (Test-Path -LiteralPath $artifact) {
        $artifact = Join-Path $evidenceRoot ('s13-admin-check-' + $stamp + '-' + $suffix + '.json')
        $suffix++
    }
    $json = $result | ConvertTo-Json -Depth 5
    Set-Content -LiteralPath $artifact -Value $json -Encoding UTF8
    $artifactHash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
    $result['artifactSha256'] = $artifactHash
    $result['artifactPath'] = $artifact
} else {
    $result['artifactSaved'] = $false
}
$result | ConvertTo-Json -Depth 5
