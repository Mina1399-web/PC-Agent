function Invoke-TaskControlledCrash {
    $ready=Join-Path $script:taskDataDirectory 'crash-proof-ready.json'
    $expired=Join-Path $script:taskDataDirectory 'crash-proof-expired.flag'
    Assert-TaskSafetyFloor
    Assert-TaskProtectedService $script:ProtectedBaseline
    Assert-TaskIsolatedService | Out-Null
    [IO.File]::WriteAllText((Join-Path $script:taskDataDirectory 'crash-proof-arm.flag'),'arm_test_only')
    $clock=[Diagnostics.Stopwatch]::StartNew()
    $proof=$null
    while($clock.Elapsed.TotalSeconds -lt 25){
        Assert-TaskSafetyFloor
        Assert-TaskProtectedService $script:ProtectedBaseline
        Assert-TaskIsolatedService | Out-Null
        if(Test-Path -LiteralPath $ready){
            if((Get-Item -LiteralPath $ready).Length -gt 16384){throw 'crash_marker_oversized'}
            try{$proof=Get-Content -LiteralPath $ready -Raw | ConvertFrom-Json}catch{$proof=$null}
            if($proof){break}
        }
        Start-Sleep -Milliseconds 100
    }
    if(-not $proof -or $proof.runId -ne $script:taskId -or -not $proof.committed -or $proof.pendingTransactions -ne 2 -or
        @($proof.snapshotIds).Count -lt 1 -or @($proof.snapshotIds).Count -gt 64 -or (Test-Path -LiteralPath $expired)){
        throw 'crash_barrier_not_ready'
    }
    $markerHash=(Get-FileHash -LiteralPath $ready -Algorithm SHA256).Hash
    $service=Assert-TaskIsolatedService
    $held=[Diagnostics.Process]::GetProcessById([int]$service.ProcessId)
    try{
        # Bind the OS process handle before final identity validation; never kill by a later PID lookup.
        [void]$held.Handle
        $process=Get-CimInstance Win32_Process -Filter ('ProcessId='+$held.Id) -ErrorAction Stop
        $service=Assert-TaskIsolatedService
        Assert-TaskSafetyFloor
        Assert-TaskProtectedService $script:ProtectedBaseline
        if(-not (Test-IsolatedCrashTarget $script:taskId $service $process $script:ProtectedBaseline.ProcessId) -or
            $held.HasExited -or $held.MainModule.FileName -ine $process.ExecutablePath -or
            [math]::Abs(($held.StartTime.ToUniversalTime()-$process.CreationDate.ToUniversalTime()).TotalMilliseconds) -ge 1 -or
            (Test-Path -LiteralPath $expired)) {throw 'crash_target_identity_mismatch'}
        $crashedPid=$held.Id
        $held.Kill()
        if(-not $held.WaitForExit(10000)){throw 'isolated_crash_exit_unconfirmed'}
    }finally{$held.Dispose()}
    Wait-TaskServiceState 'Stopped' 30 | Out-Null
    Invoke-TaskSc 'start'
    $recovered=Wait-TaskServiceState 'Running' 60
    if($recovered.ProcessId -eq $crashedPid){throw 'crash_recovery_pid_not_changed'}
    Invoke-TaskProbe 'view'
    Wait-TaskDatabaseReady 60 | Out-Null
    Invoke-TaskSc 'stop'
    Wait-TaskServiceState 'Stopped' 45 | Out-Null
    if((Get-FileHash -LiteralPath $ready -Algorithm SHA256).Hash -ne $markerHash -or (Test-Path -LiteralPath $expired)){
        throw 'crash_marker_changed_or_expired'
    }
    Assert-TaskSafetyFloor
    Assert-TaskProtectedService $script:ProtectedBaseline
    $python='C:\Users\vmdev\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
    $check=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'check-isolated-scm-crash.py') -Raw
    # No -c; no compiler/cache/output artifacts in data or source folders.
    $readLocks=@()
    try {
        foreach($name in @('host-agent.db','notifications.db')) {
            $readLocks += [IO.File]::Open((Join-Path $script:taskDataDirectory $name),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        }
        $output=@($check | & $python -I -B - $script:taskId)
    } finally {foreach($readLock in $readLocks){$readLock.Dispose()}}
    if($LASTEXITCODE -ne 0 -or $output.Count -ne 1){throw 'crash_post_recovery_check_failed'}
    $checks=$output[0] | ConvertFrom-Json
    if(@($checks).Count -ne 2 -or @($checks | Where-Object {$_.quickCheck -ne 'ok' -or -not $_.committedProofPreserved -or -not $_.uncommittedProofAbsent -or -not $_.acknowledgedSnapshotsPreserved}).Count){throw 'crash_persistence_invariant_failed'}
    return [pscustomobject]@{crashedProcessId=$crashedPid;recoveredProcessId=[int]$recovered.ProcessId;markerSha256=$markerHash;recovery='controller_start';databaseChecks=$checks}
}
