param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
if($env:COMPUTERNAME -ne 'AGENTPC' -or (Get-CimInstance Win32_ComputerSystem).Model -ne 'VirtualBox' -or (Get-Service VBoxService).Status -ne 'Running'){throw 'DEV VM identity gate failed'}
if((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1024 -lt 1024 -or (Get-PSDrive C).Free -lt 20GB){throw 'Resource gate failed'}
. (Join-Path $PSScriptRoot 'Assert-IsolatedCandidatePath.ps1')
$taskEvidence=Join-Path $env:LOCALAPPDATA 'HostAgent-DEV-evidence'
$taskCandidate=Assert-IsolatedCandidatePath $CandidateDirectory $taskEvidence
$taskDotnet=Join-Path $env:LOCALAPPDATA 'HostAgent-DEV-tools\dotnet-10.0.401\dotnet.exe'
$taskPython=Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
if(-not (Test-Path -LiteralPath $taskPython -PathType Leaf)){throw 'Bundled verifier unavailable'}
$taskFixture=[guid]::NewGuid().ToString('N')
$taskOut=Join-Path $taskEvidence ('interruption-'+$taskFixture+'.jsonl')
$taskErr=Join-Path $taskEvidence ('interruption-'+$taskFixture+'-stderr.txt')
$taskProcess=Start-Process -FilePath $taskDotnet -ArgumentList @('"'+(Join-Path $taskCandidate 'HostAgent.PipeProbe.dll')+'"','isolated-persistence',$taskFixture) -WindowStyle Hidden -RedirectStandardOutput $taskOut -RedirectStandardError $taskErr -PassThru
$taskOriginalHandle=$taskProcess.SafeHandle
try{
    $taskTimer=[Diagnostics.Stopwatch]::StartNew()
    while(-not ((Get-Content -LiteralPath $taskOut -ErrorAction SilentlyContinue) -match '"ready":true')){
        if($taskProcess.HasExited -or $taskTimer.Elapsed.TotalSeconds -gt 15){throw 'Fixture did not reach uncommitted transaction barrier'}
        if((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1024 -lt 768){throw 'Memory safety abort'}
        Start-Sleep -Milliseconds 100
    }
    # Only the exact child created above; no service control or process-tree kill.
    $taskChild=Get-CimInstance Win32_Process -Filter ("ProcessId="+$taskProcess.Id)
    if($taskChild.CommandLine -notlike ('*isolated-persistence '+$taskFixture+'*')){throw 'Child identity gate failed'}
    if($taskProcess.HasExited){throw 'Fixture exited before injection'}
    $taskProcess.Kill()
    if(-not $taskProcess.WaitForExit(10000)){throw 'Fixture process did not terminate'}
    $taskJson=& $taskPython (Join-Path $PSScriptRoot 'inspect-runtime-fixture.py') $taskFixture persistence
    if($LASTEXITCODE -ne 0){throw 'Read-only fixture verification failed'}
    $taskResult=$taskJson | ConvertFrom-Json
    if($taskResult.quickCheck -ne 'ok' -or $taskResult.cpu.Count -ne 1 -or $taskResult.cpu[0][1] -ne 42 -or $taskResult.telemetry.Count -ne 0){
        throw 'Interrupted fixture recovery invariant failed'
    }
    $taskJson
    [pscustomobject]@{scenario='disposable-process-interruption';outcome='PASS';fixture=$taskFixture;processKilled=$true;committedMetricPreserved=$true;uncommittedRowsAbsent=$true;readOnlyQuickCheck='ok'} | ConvertTo-Json -Compress
}
finally{
    if(-not $taskProcess.HasExited){$taskProcess.Kill();$taskProcess.WaitForExit(10000) | Out-Null}
    $taskProcess.Dispose()
}
