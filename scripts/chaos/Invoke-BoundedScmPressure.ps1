# Dot-sourced only by the isolated runner. Does not create/control any Windows service.
function Invoke-TaskWatchdogPressure {
    Assert-TaskSafetyFloor
    Assert-TaskProtectedService $script:ProtectedBaseline
    $service = Assert-TaskIsolatedService
    if ($service.State -ne 'Running') { throw 'pressure_requires_running_isolated_service' }
    $source = Join-Path $PSScriptRoot 'BoundedScmPressure.cs'
    Assert-TaskNoReparseAncestors $source
    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    Add-Type -Path $source -ErrorAction Stop
    Assert-TaskSafetyFloor
    $job = [ScmPressureJob]::new()
    $pressure = $null
    $gate = Join-Path $script:taskEvidenceDirectory 'pressure-assigned.gate'
    $cancel = Join-Path $script:taskEvidenceDirectory 'pressure-cancel.signal'
    $report = [ordered]@{minimumObservedFreeMiB=99999.0;hardAbort=$false;pressureExit=$null;watchdogExit=$null;sourceSha256=$sourceHash;gateOpened=$false;timeout=$false}
    try {
        $parent = [Diagnostics.Process]::GetCurrentProcess()
        $quote = { param($s) "'" + $s.Replace("'", "''") + "'" }
        $childCode = 'Add-Type -Path ' + (& $quote $source) + '; [BoundedScmPressure]::Run(' +
            $parent.Id + ', ' + $parent.StartTime.ToUniversalTime().Ticks + 'L, ' +
            (& $quote $gate) + ', ' + (& $quote $script:taskMemoryMarkerPath) + ', ' + (& $quote $cancel) + ')'
        $info = [Diagnostics.ProcessStartInfo]::new()
        $info.FileName = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $info.Arguments = '-NoProfile -NonInteractive -EncodedCommand ' + [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childCode))
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $pressure = [Diagnostics.Process]::Start($info)
        $stdout = $pressure.StandardOutput.ReadToEndAsync()
        $stderr = $pressure.StandardError.ReadToEndAsync()
        $job.Assign($pressure)
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $sourceHash) { throw 'pressure_source_changed' }
        # Gate remains closed until the exact child has been assigned to kill-on-close job.
        [IO.File]::WriteAllText($gate, 'assigned')
        $report.gateOpened = $true
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while (-not $pressure.HasExited) {
            $free = [BoundedScmPressure]::FreeMiB()
            if ([double]::IsNaN($free) -or $free -le 512) { $report.hardAbort=$true; break }
            $report.minimumObservedFreeMiB = [math]::Min($report.minimumObservedFreeMiB, $free)
            if ($clock.Elapsed.TotalSeconds -ge 65) { $report.timeout=$true; break }
            # Also free pressure if the service watchdog unexpectedly dies without a marker.
            if ($script:taskMemoryWatchdog.HasExited -and -not (Test-Path -LiteralPath $script:taskMemoryMarkerPath)) { break }
            Start-Sleep -Milliseconds 50
            $pressure.Refresh()
        }
        if (-not $pressure.HasExited) { $job.Dispose() }
        if (-not $pressure.WaitForExit(5000)) { throw 'pressure_release_unconfirmed' }
        $report.pressureExit = $pressure.ExitCode
        # Only bounded stable helper output; compiler messages/payloads are never saved.
        [void]$stdout.GetAwaiter().GetResult()
        [void]$stderr.GetAwaiter().GetResult()
        # Windows PowerShell's hosting of Console.Out is not a reliable protocol.
        $report.reason = switch ($report.pressureExit) {
            0 {'marker'}; 21 {'hard_abort'}; 22 {'deadline_parent_loss_or_cancel'}
            23 {'allocation_cap'}; 24 {'allocation_failure'}; default {'child_failed_or_killed'}
        }
        if ($report.pressureExit -eq 21) { $report.hardAbort=$true }
        $report.minimumObservedFreeMiB = [math]::Round($report.minimumObservedFreeMiB,1)
        # Pressure is gone before waiting for the service stop or using CIM.
        if (-not $script:taskMemoryWatchdog.WaitForExit(12000)) { throw 'watchdog_stop_not_completed' }
        $report.watchdogExit = $script:taskMemoryWatchdog.ExitCode
        $marker = if (Test-Path -LiteralPath $script:taskMemoryMarkerPath) { Get-Content -LiteralPath $script:taskMemoryMarkerPath -Raw | ConvertFrom-Json } else { $null }
        $observedService = Assert-TaskIsolatedService
        $report.serviceState = $observedService.State
        $report.confirmed = (Test-IsolatedWatchdogStopEvidence $marker $script:taskId $report.watchdogExit $report.pressureExit $observedService.State $report.hardAbort) -and -not $report.timeout
        return [pscustomobject]$report
    } finally {
        # No pressure child may survive an exception, Ctrl+C or normal exit.
        $job.Dispose()
        if ($pressure) {
            if (-not $pressure.WaitForExit(5000)) { $pressure.Kill(); [void]$pressure.WaitForExit(5000) }
            $pressure.Dispose()
        }
    }
}
