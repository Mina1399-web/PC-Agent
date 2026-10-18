param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
$taskMachine=Get-CimInstance Win32_ComputerSystem
if($env:COMPUTERNAME -ne 'AGENTPC' -or $taskMachine.Model -ne 'VirtualBox' -or (Get-Service VBoxService).Status -ne 'Running'){
    throw 'DEV VM identity gate failed'
}
if((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1024 -lt 768){throw 'Insufficient memory headroom'}
if((Get-PSDrive C).Free -lt 20GB){throw 'Disk safety floor'}
$taskEvidenceRoot=Join-Path $env:LOCALAPPDATA 'HostAgent-DEV-evidence'
. (Join-Path $PSScriptRoot 'Assert-IsolatedCandidatePath.ps1')
$taskCandidate=Assert-IsolatedCandidatePath $CandidateDirectory $taskEvidenceRoot
$taskDll=Join-Path $taskCandidate 'HostAgent.PipeProbe.dll'
$taskDotnet=Join-Path $env:LOCALAPPDATA 'HostAgent-DEV-tools\dotnet-10.0.401\dotnet.exe'
$taskProtected='C:\Program Files\HostAgent\Service\HostAgent.Service.exe'
$taskHashBefore=(Get-FileHash -LiteralPath $taskProtected -Algorithm SHA256).Hash
$taskFixture=[guid]::NewGuid().ToString('N')
$taskFixtureDir=Join-Path $taskEvidenceRoot ('view-candidate-'+$taskFixture)
New-Item -ItemType Directory -Path $taskFixtureDir | Out-Null
$taskStop=Join-Path $taskFixtureDir 'stop'
$taskRounds=@(
    @{name='late-accept';mode='normal';count=5;expectedFailures=0},
    @{name='process-restart';mode='normal';count=2;expectedFailures=0},
    @{name='timeout-recovery-soak';mode='fault';count=6;expectedFailures=1}
)
$taskResults=New-Object System.Collections.Generic.List[object]
foreach($taskRound in $taskRounds){
    if(Test-Path -LiteralPath $taskStop){Remove-Item -LiteralPath $taskStop}
    $taskOut=Join-Path $taskFixtureDir ($taskRound.name+'-server.jsonl')
    $taskErr=Join-Path $taskFixtureDir ($taskRound.name+'-stderr.txt')
    $taskProcess=Start-Process -FilePath $taskDotnet -ArgumentList @('"'+$taskDll+'"','isolated-server',$taskFixture,$taskRound.mode) -WindowStyle Hidden -RedirectStandardOutput $taskOut -RedirectStandardError $taskErr -PassThru
    $taskOriginalHandle=$taskProcess.SafeHandle
    try{
        $taskReady=[Diagnostics.Stopwatch]::StartNew()
        while(-not ((Get-Content -LiteralPath $taskOut -ErrorAction SilentlyContinue) -contains '{"ready":true}')){
            if($taskProcess.HasExited -or $taskReady.Elapsed.TotalSeconds -gt 15){throw 'Isolated server did not become ready'}
            Start-Sleep -Milliseconds 100
        }
        $taskLines=& $taskDotnet $taskDll isolated-view $taskFixture $taskRound.count
        $taskLines | Set-Content -LiteralPath (Join-Path $taskFixtureDir ($taskRound.name+'-client.jsonl')) -Encoding UTF8
        $taskReplies=@($taskLines | ForEach-Object {$_ | ConvertFrom-Json})
        $taskFailures=@($taskReplies | Where-Object {-not $_.ok}).Count
        if($taskReplies.Count -ne $taskRound.count -or $taskFailures -ne $taskRound.expectedFailures -or -not $taskReplies[-1].ok){
            throw 'Unexpected isolated View outcome'
        }
        New-Item -ItemType File -Path $taskStop | Out-Null
        if(-not $taskProcess.WaitForExit(10000)){throw 'Isolated server did not stop promptly'}
        if($taskProcess.ExitCode -ne 0){throw 'Isolated server health check failed'}
        $taskResult=[pscustomobject]@{scenario=$taskRound.name;attempts=$taskReplies.Count;failures=$taskFailures;lastRequestRecovered=$taskReplies[-1].ok;outcome='PASS'}
        $taskResults.Add($taskResult)
        $taskResult | ConvertTo-Json -Compress
    }
    finally{
        if(-not $taskProcess.HasExited){
            if(-not (Test-Path -LiteralPath $taskStop)){New-Item -ItemType File -Path $taskStop | Out-Null}
            if(-not $taskProcess.WaitForExit(10000)){$taskProcess.Kill();$taskProcess.WaitForExit(10000) | Out-Null}
        }
        $taskProcess.Dispose()
    }
}
$taskHashAfter=(Get-FileHash -LiteralPath $taskProtected -Algorithm SHA256).Hash
if($taskHashBefore -ne $taskHashAfter){throw 'Protected image changed'}
$taskResults.ToArray() | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskFixtureDir 'results.json') -Encoding UTF8
[pscustomobject]@{evidenceDirectory=$taskFixtureDir;protectedImageUnchanged=$true;candidateSha256=(Get-FileHash -LiteralPath $taskDll -Algorithm SHA256).Hash} | ConvertTo-Json -Compress
