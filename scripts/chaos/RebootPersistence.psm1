$ErrorActionPreference = 'Stop'

function Assert-RebootPlainPath([string]$Path) {
    for ($current = [IO.Path]::GetFullPath($Path); $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'reboot_reparse_path' }
    }
}

function Assert-RebootManifest([string]$Directory, [string]$ExpectedHash, [switch]$Canaries) {
    Assert-RebootPlainPath $Directory
    $manifestPath = Join-Path $Directory 'candidate-manifest.json'
    Assert-RebootPlainPath $manifestPath
    if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -cne $ExpectedHash) { throw 'reboot_candidate_manifest_mismatch' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.format -ne 1 -or @($manifest.files).Count -ne 75) { throw 'reboot_candidate_inventory_invalid' }
    $entries = @{}
    foreach ($entry in $manifest.files) {
        $relative = [string]$entry.path
        if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or $relative.Contains('..') -or $relative.Contains(':') -or $entries.ContainsKey($relative.Replace('/','\'))) { throw 'reboot_candidate_entry_invalid' }
        $file = Join-Path $Directory $relative
        Assert-RebootPlainPath $file
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -cne $entry.sha256) { throw 'reboot_candidate_file_mismatch' }
        if ((Get-Item -LiteralPath $file).Length -ne $entry.bytes) { throw 'reboot_candidate_length_mismatch' }
        $entries[$relative.Replace('/','\')] = $true
    }
    $entries['candidate-manifest.json'] = $true
    if ($Canaries) {
        $bytes = [Text.Encoding]::UTF8.GetBytes('HostAgent isolated SCM ACL canary v1')
        foreach ($name in @('.scm-acl-write-canary','.scm-acl-delete-canary')) {
            $file = Join-Path $Directory $name
            if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($file)) -cne [Convert]::ToBase64String($bytes)) { throw 'reboot_canary_changed' }
            $entries[$name] = $true
        }
    }
    $queue = [Collections.Generic.Queue[string]]::new(); $queue.Enqueue($Directory); $count = 0
    while ($queue.Count) {
        foreach ($item in Get-ChildItem -LiteralPath $queue.Dequeue() -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'reboot_candidate_reparse' }
            if ($item.PSIsContainer) { $queue.Enqueue($item.FullName) } else {
                $relative = $item.FullName.Substring($Directory.TrimEnd('\').Length + 1)
                if (-not $entries.ContainsKey($relative)) { throw 'reboot_candidate_extra_file' }
                $count++
            }
        }
    }
    if ($count -ne $entries.Count) { throw 'reboot_candidate_missing_file' }
}

function Assert-RebootServiceIdentity($Service, [string]$RunId, [string]$ImagePath, [string[]]$Modes = @('Auto')) {
    $id = [guid]::Empty
    if (-not [guid]::TryParseExact($RunId,'N',[ref]$id) -or $id -eq [guid]::Empty -or $RunId -cne $id.ToString('N')) { throw 'reboot_guid_required' }
    $name = 'HostAgentDevScm_' + $RunId
    if (-not $Service -or $Service.Name -cne $name -or $Service.StartName -ine ('NT SERVICE\' + $name) -or $Service.PathName -cne $ImagePath -or $Service.StartMode -notin $Modes) { throw 'reboot_service_identity_mismatch' }
}

function Assert-RebootProtectedContinuity($Before, $After) {
    foreach ($name in @('Name','State','StartMode','StartName','PathName','ImageSha256')) {
        if ($Before.$name -cne $After.$name) { throw 'reboot_protected_configuration_changed' }
    }
    foreach ($name in @($Before.AssemblyHashes.PSObject.Properties.Name)) {
        if ($Before.AssemblyHashes.$name -cne $After.AssemblyHashes.$name) { throw 'reboot_protected_assembly_changed' }
    }
}

function Assert-RebootTransition($Prepared, [datetime]$BootAfter, $Closed, $Started, [int]$CurrentPid, [datetime]$CreationTime) {
    $bootBefore = [datetime]::Parse($Prepared.bootBeforeUtc).ToUniversalTime()
    $readyAt = [datetime]::Parse($Prepared.ready.readyAtUtc).ToUniversalTime()
    $bootAfterUtc = $BootAfter.ToUniversalTime()
    if ($bootAfterUtc -le $bootBefore -or $bootAfterUtc -le $readyAt) { throw 'reboot_new_boot_not_proved' }
    if ($Closed.runId -cne $Prepared.runId -or $Closed.reason -cne 'graceful_stop') { throw 'reboot_graceful_rollback_not_proved' }
    $closedAt = [datetime]::Parse($Closed.closedAtUtc).ToUniversalTime()
    if ($closedAt -lt $readyAt -or $closedAt -ge $bootAfterUtc -or ($closedAt - $readyAt).TotalSeconds -gt 900) { throw 'reboot_shutdown_time_invalid' }
    if ($Started.runId -cne $Prepared.runId -or [int]$Started.processId -ne $CurrentPid -or $CurrentPid -le 0 -or [datetime]::Parse($Started.startedAtUtc).ToUniversalTime() -lt $bootAfterUtc -or $CreationTime.ToUniversalTime() -lt $bootAfterUtc) { throw 'reboot_automatic_runtime_not_proved' }
}

Export-ModuleMember -Function Assert-RebootPlainPath,Assert-RebootManifest,Assert-RebootServiceIdentity,Assert-RebootProtectedContinuity,Assert-RebootTransition
