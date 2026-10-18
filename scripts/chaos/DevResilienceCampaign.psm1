function Invoke-DevResiliencePlan {
    param([object[]]$Plan,[string]$RunId,[scriptblock]$Guard,[scriptblock]$Save)
    $rows=@($Plan | ForEach-Object {[pscustomobject]@{id=$_.id;status='NOT RUN';runId=$RunId;reason='not_reached';manualStep=$_['manualStep'];limits=$_.limits;logDirectory=$_['logDirectory'];startedUtc=$null;finishedUtc=$null;details=$null}})
    & $Save $rows | Out-Null
    $stop=$false
    for($index=0;$index -lt $Plan.Count;$index++) {
        $phase=$Plan[$index]; $row=$rows[$index]
        if($stop){$row.reason='skipped_after_failure_or_uncertain_state';continue}
        if($phase.kind -eq 'Manual') {
            $row.reason=$phase.reason
            & $Save $rows | Out-Null
            continue
        }
        $row.startedUtc=[DateTimeOffset]::UtcNow.ToString('O')
        $row.reason='started_success_not_yet_verified'
        & $Save $rows | Out-Null
        try {
            & $Guard | Out-Null
            $details=& $phase.run
            if(-not $details -or $details.ok -ne $true){throw 'success_evidence_missing'}
            & $Guard | Out-Null
            $row.details=$details; $row.status='PASS'; $row.reason='verified_within_declared_scope'
        }catch {
            $row.status='FAIL'; $stop=$true
            $row.reason=if($_.Exception.Message -match '^[a-z][a-z0-9_]{0,95}$'){$_.Exception.Message}else{'phase_exception_or_uncertain_state'}
        }
        $row.finishedUtc=[DateTimeOffset]::UtcNow.ToString('O')
        & $Save $rows | Out-Null
    }
    & $Save $rows | Out-Null
    return $rows
}

function Invoke-DevBoundedProcess {
    param([string]$File,[string[]]$Arguments,[int]$TimeoutSeconds,[string]$LogDirectory,[scriptblock]$Guard)
    if($TimeoutSeconds -lt 1 -or $TimeoutSeconds -gt 900){throw 'invalid_process_deadline'}
    $job=[ScmPressureJob]::new(); $child=$null; $outTask=$null; $errTask=$null
    $timer=[Diagnostics.Stopwatch]::StartNew(); $peak=0L; $failure=$null
    try {
        & $Guard | Out-Null
        $info=[Diagnostics.ProcessStartInfo]::new()
        $info.FileName=$File; $info.Arguments=ConvertTo-IsolatedScmNativeCommandLine $Arguments
        $info.UseShellExecute=$false; $info.CreateNoWindow=$true
        $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
        $child=[Diagnostics.Process]::Start($info)
        [IO.File]::WriteAllText((Join-Path $LogDirectory 'child.json'),([ordered]@{processId=$child.Id;image=$File;activeDeadlineSeconds=$TimeoutSeconds;cleanupGraceSeconds=12;terminationWaitSeconds=5} | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
        $outTask=$child.StandardOutput.ReadToEndAsync(); $errTask=$child.StandardError.ReadToEndAsync()
        $job.Assign($child)
        while(-not $child.HasExited){
            & $Guard | Out-Null
            if($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds){throw 'phase_process_deadline'}
            $child.Refresh(); if(-not $child.HasExited){$peak=[math]::Max($peak,$child.WorkingSet64)}
            Start-Sleep -Milliseconds 250
        }
        $child.WaitForExit()
        return [pscustomobject]@{exitCode=$child.ExitCode;processId=$child.Id;peakWorkingSetBytes=$peak;elapsedSeconds=[math]::Round($timer.Elapsed.TotalSeconds,1);stdoutPath=(Join-Path $LogDirectory 'stdout.txt');stderrPath=(Join-Path $LogDirectory 'stderr.txt')}
    }catch {
        $failure=$_
        throw
    }finally {
        # Allow the existing SCM runner/watchdog a brief cleanup opportunity on resource abort.
        if($failure -and $failure.Exception.Message -ne 'hard_free_memory_floor_512_mib' -and $child -and -not $child.HasExited){[void]$child.WaitForExit(12000)}
        $job.Dispose()
        if($child){
            if(-not $child.WaitForExit(5000)){$child.Kill();[void]$child.WaitForExit(5000)}
            if($outTask -and $errTask -and $child.HasExited){
                $out=$outTask.GetAwaiter().GetResult(); $err=$errTask.GetAwaiter().GetResult()
                [IO.File]::WriteAllText((Join-Path $LogDirectory 'stdout.txt'),$out.Substring(0,[math]::Min($out.Length,524288)),[Text.UTF8Encoding]::new($false))
                [IO.File]::WriteAllText((Join-Path $LogDirectory 'stderr.txt'),$err.Substring(0,[math]::Min($err.Length,524288)),[Text.UTF8Encoding]::new($false))
            }
            $child.Dispose()
        }
    }
}

function Test-DevViewRound {
    param([object[]]$Replies,$ServerSummary,[int]$ClientExit)
    return ($Replies.Count -eq 6 -and $Replies[0].ok -eq $false -and
        @($Replies | Select-Object -Skip 1 | Where-Object {$_.ok -ne $true}).Count -eq 0 -and
        $ServerSummary.stopped -eq $true -and $ServerSummary.quickCheckHealthy -eq $true -and $ClientExit -eq 1)
}

function Invoke-DevViewCycles {
    param([string]$Candidate,[string]$EvidenceRoot,[string]$LogDirectory,[int]$Cycles,[scriptblock]$Guard)
    if($Cycles -lt 1 -or $Cycles -gt 3){throw 'invalid_transport_cycle_count'}
    $results=@()
    for($round=1;$round -le $Cycles;$round++) {
        & $Guard | Out-Null
        $id=[guid]::NewGuid().ToString('N')
        $fixture=Join-Path $EvidenceRoot ('view-candidate-'+$id)
        if(Test-Path -LiteralPath $fixture){throw 'transport_fixture_exists'}
        New-Item -ItemType Directory -Path $fixture | Out-Null
        $logs=Join-Path $LogDirectory ('round-'+$round+'-'+$id)
        New-Item -ItemType Directory -Path $logs | Out-Null
        $clientLogs=Join-Path $logs 'client'; New-Item -ItemType Directory $clientLogs | Out-Null
        $exe=Join-Path $Candidate 'HostAgent.PipeProbe.exe'
        $job=[ScmPressureJob]::new(); $server=$null; $tail=$null; $errors=$null; $readyTask=$null; $readyText=''
        $clock=[Diagnostics.Stopwatch]::StartNew(); $stats=[pscustomobject]@{peak=0L}
        try {
            $info=[Diagnostics.ProcessStartInfo]::new()
            $info.FileName=$exe; $info.Arguments=ConvertTo-IsolatedScmNativeCommandLine @('isolated-server',$id,'fault')
            $info.UseShellExecute=$false; $info.CreateNoWindow=$true
            $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
            $server=[Diagnostics.Process]::Start($info)
            [IO.File]::WriteAllText((Join-Path $logs 'server-child.json'),(@{processId=$server.Id;runId=$id;image=$exe;serverLifetimeSeconds=60}|ConvertTo-Json))
            $job.Assign($server)
            $errors=$server.StandardError.ReadToEndAsync()
            $readyTask=$server.StandardOutput.ReadLineAsync()
            while(-not $readyTask.IsCompleted) {
                & $Guard | Out-Null
                if($server.HasExited -or $clock.Elapsed.TotalSeconds -ge 15){throw 'transport_server_not_ready'}
                Start-Sleep -Milliseconds 100
            }
            $readyText=$readyTask.GetAwaiter().GetResult()
            $tail=$server.StandardOutput.ReadToEndAsync()
            if($readyText -ne '{"ready":true}'){throw 'transport_ready_marker_invalid'}
            $clientGuard={
                & $Guard | Out-Null
                if($server.HasExited -or $clock.Elapsed.TotalSeconds -ge 70){throw 'transport_round_deadline_or_server_exit'}
                $server.Refresh(); $stats.peak=[math]::Max($stats.peak,$server.WorkingSet64)
                if($server.WorkingSet64 -gt 256MB){throw 'transport_working_set_limit'}
            }.GetNewClosure()
            $client=Invoke-DevBoundedProcess $exe @('isolated-view',$id,'6') 45 $clientLogs $clientGuard
            [IO.File]::WriteAllText((Join-Path $fixture 'stop'),'bounded_test_complete')
            while(-not $server.HasExited) {
                & $Guard | Out-Null
                if($clock.Elapsed.TotalSeconds -ge 70){throw 'transport_round_deadline'}
                Start-Sleep -Milliseconds 100
            }
            $server.WaitForExit()
            $serverText=$tail.GetAwaiter().GetResult()
            $objects=@($serverText -split '\r?\n' | Where-Object {$_} | ForEach-Object {$_ | ConvertFrom-Json})
            $summary=$objects | Where-Object {$_.PSObject.Properties['stopped'] -and $_.stopped -eq $true} | Select-Object -Last 1
            $replies=@(Get-Content -LiteralPath $client.stdoutPath | Where-Object {$_} | ForEach-Object {$_ | ConvertFrom-Json})
            if($server.ExitCode -ne 0 -or -not (Test-DevViewRound $replies $summary $client.exitCode)){throw 'transport_recovery_invariant_failed'}
            $files=@(Get-ChildItem -LiteralPath $fixture -File -Recurse)
            $bytes=($files | Measure-Object -Property Length -Sum).Sum
            if($files.Count -gt 8 -or $bytes -gt 16MB){throw 'transport_fixture_storage_limit'}
            $results+=[pscustomobject]@{runId=$id;fixtureDirectory=$fixture;logDirectory=$logs;attempts=6;expectedFailures=1;remainingRecovered=5;databaseQuickCheck='ok';elapsedSeconds=[math]::Round($clock.Elapsed.TotalSeconds,1);sampledServerPeakWorkingSetBytes=$stats.peak;fixtureBytes=$bytes}
        }finally {
            $job.Dispose()
            if($server){
                if(-not $server.WaitForExit(5000)){$server.Kill();[void]$server.WaitForExit(5000)}
                if($server.HasExited){
                    $text=''; $err=''
                    try {
                        if($readyTask -and -not $tail){$readyText=$readyTask.GetAwaiter().GetResult(); $tail=$server.StandardOutput.ReadToEndAsync()}
                        $text=$readyText
                        if($tail){$text+="`r`n"+$tail.GetAwaiter().GetResult()}
                        if($errors){$err=$errors.GetAwaiter().GetResult()}
                    }catch{$err='server_output_read_failed'}
                    [IO.File]::WriteAllText((Join-Path $logs 'server-stdout.txt'),$text.Substring(0,[math]::Min($text.Length,524288)))
                    [IO.File]::WriteAllText((Join-Path $logs 'server-stderr.txt'),$err.Substring(0,[math]::Min($err.Length,524288)))
                }
                $server.Dispose()
            }
        }
    }
    return [pscustomobject]@{ok=$true;scope='isolated_view_transport_only';cycles=$results;fullApplicationEnduranceVerified=$false;liveWpfVerified=$false}
}

function Test-DevCrashProofResult {
    param($Result,[string]$RunId,[string]$Manifest)
    try {
        $checks=if($Result.crash.databaseChecks.PSObject.Properties['value']){@($Result.crash.databaseChecks.value)}else{@($Result.crash.databaseChecks)}
        return ($Result.outcome -eq 'PASS' -and $Result.runId -ceq $RunId -and $Result.serviceName -ceq ('HostAgentDevScm_'+$RunId) -and
            $Result.scenario -eq 'isolated-scm-controlled-crash-recovery' -and $Result.candidateManifestSha256 -eq $Manifest -and
            $Result.protectedServiceUnchanged -eq $true -and $Result.controllerFallbackStopUsed -eq $false -and
            $Result.crash.recovery -eq 'controller_start' -and $Result.crash.crashedProcessId -gt 0 -and
            $Result.crash.recoveredProcessId -gt 0 -and $Result.crash.recoveredProcessId -ne $Result.crash.crashedProcessId -and
            $checks.Count -eq 2 -and @($checks.database | Sort-Object -Unique).Count -eq 2 -and
            @($checks | Where-Object {$_.database -notin @('host-agent.db','notifications.db') -or $_.quickCheck -ne 'ok' -or
                $_.committedProofPreserved -ne $true -or $_.uncommittedProofAbsent -ne $true -or $_.acknowledgedSnapshotsPreserved -ne $true}).Count -eq 0)
    }catch{return $false}
}

Export-ModuleMember -Function Invoke-DevResiliencePlan,Invoke-DevBoundedProcess,Test-DevViewRound,Invoke-DevViewCycles,Test-DevCrashProofResult
