# Own the encoder dependency; never rely on another entry point's import scope.
Import-Module (Join-Path $PSScriptRoot 'IsolatedScmSafety.psm1')

function Get-CompleteMemoryDecision([double]$FreeMiB) {
 if([double]::IsNaN($FreeMiB) -or [double]::IsInfinity($FreeMiB) -or $FreeMiB -le 512){return 'hard_abort'}
 if($FreeMiB -lt 768){return 'soft_abort'}
 return 'continue'
}
function Invoke-CompleteBoundedProcess {
    param([string]$File,[string[]]$Arguments,[int]$TimeoutSeconds,[string]$LogDirectory,[scriptblock]$Guard)
    if($TimeoutSeconds -lt 1 -or $TimeoutSeconds -gt 2400){throw 'invalid_process_deadline'}
    $job=[ScmPressureJob]::new(); $child=$null; $outTask=$null; $errTask=$null
    $timer=[Diagnostics.Stopwatch]::StartNew(); $peak=0L; $failure=$null
    try {
        & $Guard | Out-Null
        $info=[Diagnostics.ProcessStartInfo]::new()
        $info.FileName=$File; $info.Arguments=ConvertTo-IsolatedScmNativeCommandLine $Arguments
        $info.UseShellExecute=$false; $info.CreateNoWindow=$true
        $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
                if($File -like '*\WindowsPowerShell\v1.0\powershell.exe') {
            $info.EnvironmentVariables['PSModulePath']='C:\Windows\System32\WindowsPowerShell\v1.0\Modules;C:\Program Files\WindowsPowerShell\Modules;C:\Users\vmdev\Documents\WindowsPowerShell\Modules'
        }
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
Export-ModuleMember -Function Get-CompleteMemoryDecision,Invoke-CompleteBoundedProcess

