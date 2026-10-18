$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'scripts/chaos/DevResilienceCampaign.psm1') -Force
Import-Module (Join-Path $repo 'scripts/chaos/IsolatedScmSafety.psm1') -Force
Add-Type -Path (Join-Path $repo 'scripts/chaos/BoundedScmPressure.cs')
Describe 'Sequential resilience campaign fail-closed execution' {
    It 'keeps caller scope and guard functions available to synchronous phase callbacks' {
        $phaseScopedValue='caller_scope_fixture'
        function Read-CallerScopedFixture { return $phaseScopedValue }
        $plan=@(@{id='scope';kind='Auto';limits=@{};run={@{ok=$true;value=(Read-CallerScopedFixture)}}})
        $rows=@(Invoke-DevResiliencePlan $plan 'fixture' {} {})
        $rows[0].status | Should Be 'PASS'
        $rows[0].details.value | Should Be 'caller_scope_fixture'
    }
    It 'preserves PASS and FAIL and never invokes a later phase after an exception' {
        $script:laterCalls=0; $script:saves=0
        $plan=@(
            @{id='first';kind='Auto';limits=@{seconds=1};run={@{ok=$true}}},
            @{id='second';kind='Auto';limits=@{seconds=1};run={throw 'fixture_failure'}},
            @{id='third';kind='Auto';limits=@{seconds=1};run={$script:laterCalls++;@{ok=$true}}}
        )
        $rows=@(Invoke-DevResiliencePlan $plan 'fixture' {} {$script:saves++})
        ($rows.status -join ',') | Should Be 'PASS,FAIL,NOT RUN'
        $script:laterCalls | Should Be 0
        ($script:saves -ge 3) | Should Be $true
    }
    It 'marks an explicitly manual phase NOT RUN and can continue independent automatic phases' {
        $plan=@(@{id='manual';kind='Manual';limits=@{};reason='No safe injector';manualStep='Review fixture';run={throw 'must_not_run'}},
                @{id='independent';kind='Auto';limits=@{};run={@{ok=$true}}})
        $rows=@(Invoke-DevResiliencePlan $plan 'fixture' {} {})
        ($rows.status -join ',') | Should Be 'NOT RUN,PASS'
        $rows[0].manualStep | Should Be 'Review fixture'
    }
    It 'treats missing success evidence and guard failure as FAIL and skips successors' {
        $plan=@(@{id='empty';kind='Auto';limits=@{};run={$null}},@{id='later';kind='Auto';limits=@{};run={@{ok=$true}}})
        $rows=@(Invoke-DevResiliencePlan $plan 'fixture' {} {})
        ($rows.status -join ',') | Should Be 'FAIL,NOT RUN'
        $rows=@(Invoke-DevResiliencePlan $plan 'fixture' {throw 'guard_denied'} {})
        ($rows.status -join ',') | Should Be 'FAIL,NOT RUN'
    }
}

Describe 'Bounded native harness process; no SCM or fault injection' {
    It 'captures bounded output and exit status from an exact harmless child' {
        $directory=Join-Path $TestDrive 'native-output'; New-Item -ItemType Directory $directory | Out-Null
        $command='Write-Output ''fixture_ok''; exit 7'
        $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        $result=Invoke-DevBoundedProcess "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" @('-NoProfile','-EncodedCommand',$encoded) 10 $directory {}
        $result.exitCode | Should Be 7
        (Get-Content -LiteralPath $result.stdoutPath -Raw).Trim() | Should Be 'fixture_ok'
        Test-Path -LiteralPath (Join-Path $directory 'child.json') | Should Be $true
    }
    It 'terminates the harmless sleeping child at its deadline and keeps its logs' {
        $directory=Join-Path $TestDrive 'native-timeout'; New-Item -ItemType Directory $directory | Out-Null
        {Invoke-DevBoundedProcess "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" @('-NoProfile','-Command','Start-Sleep -Seconds 60') 1 $directory {}} | Should Throw
        $child=Get-Content -LiteralPath (Join-Path $directory 'child.json') -Raw | ConvertFrom-Json
        @(Get-Process -Id $child.processId -ErrorAction SilentlyContinue).Count | Should Be 0
        Test-Path -LiteralPath (Join-Path $directory 'stdout.txt') | Should Be $true
    }
    It 'does not wait the cleanup grace when a synthetic hard-floor guard trips' {
        $directory=Join-Path $TestDrive 'native-hard-abort'; New-Item -ItemType Directory $directory | Out-Null
        $script:guardCalls=0
        $clock=[Diagnostics.Stopwatch]::StartNew()
        {Invoke-DevBoundedProcess "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" @('-NoProfile','-Command','Start-Sleep -Seconds 60') 20 $directory {
            $script:guardCalls++; if($script:guardCalls -ge 2){throw 'hard_free_memory_floor_512_mib'}
        }} | Should Throw
        ($clock.Elapsed.TotalSeconds -lt 10) | Should Be $true
        $child=Get-Content -LiteralPath (Join-Path $directory 'child.json') -Raw | ConvertFrom-Json
        @(Get-Process -Id $child.processId -ErrorAction SilentlyContinue).Count | Should Be 0
    }
}

Describe 'View fault acceptance requires actual subsequent recovery' {
    It 'keeps startup failure logs from a harmless fake server without running a View scenario' {
        $candidate=Join-Path $TestDrive 'fake-server'; New-Item -ItemType Directory $candidate | Out-Null
        $evidence=Join-Path $TestDrive 'fake-evidence'; New-Item -ItemType Directory $evidence | Out-Null
        $logs=Join-Path $TestDrive 'fake-logs'; New-Item -ItemType Directory $logs | Out-Null
        $code=@'
using System;
using System.Threading;
public static class FIXTURECLASS {
    public static void Main(string[] args) {
        Console.WriteLine("{\"ready\":false}");
        Console.Error.WriteLine("fixture_startup_failure");
        Thread.Sleep(2000);
    }
}
'@
        $code=$code.Replace('FIXTURECLASS',('FakeServer'+[guid]::NewGuid().ToString('N')))
        Add-Type -TypeDefinition $code -OutputAssembly (Join-Path $candidate 'HostAgent.PipeProbe.exe') -OutputType ConsoleApplication
        {Invoke-DevViewCycles $candidate $evidence $logs 1 {}} | Should Throw
        $stderr=@(Get-ChildItem -LiteralPath $logs -Filter 'server-stderr.txt' -Recurse)
        $stderr.Count | Should Be 1
        (Get-Content -LiteralPath $stderr[0].FullName -Raw).Trim() | Should Be 'fixture_startup_failure'
    }
    It 'accepts exactly the first timeout followed by five successes, never repeated failures' {
        $replies=@([pscustomobject]@{ok=$false}) + @(1..5 | ForEach-Object{[pscustomobject]@{ok=$true}})
        $summary=[pscustomobject]@{stopped=$true;quickCheckHealthy=$true}
        Test-DevViewRound $replies $summary 1 | Should Be $true
        $replies[3].ok=$false
        Test-DevViewRound $replies $summary 1 | Should Be $false
        $replies[3].ok=$true; $summary.quickCheckHealthy=$false
        Test-DevViewRound $replies $summary 1 | Should Be $false
    }
}

Describe 'Main campaign artifact guard extracted without executing the script' {
    It 'accepts a complete manifest and rejects extra files and parent traversal' {
        $tokens=$null; $errors=$null
        $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/chaos/run-dev-resilience-campaign.ps1'),[ref]$tokens,[ref]$errors)
        $errors.Count | Should Be 0
        foreach($name in @('Assert-CampaignPlainPath','Assert-CampaignArtifacts')) {
            $definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
            . ([scriptblock]::Create($definition.Extent.Text))
        }
        $script:campaignCandidate=Join-Path $TestDrive 'candidate'; New-Item -ItemType Directory $script:campaignCandidate | Out-Null
        $entries=@(1..73 | ForEach-Object{
            $path=Join-Path $script:campaignCandidate ('fixture-'+$_+'.bin')
            [IO.File]::WriteAllText($path,'test-only')
            [pscustomobject]@{path=[IO.Path]::GetFileName($path);sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
        })
        $manifest=Join-Path $script:campaignCandidate 'candidate-manifest.json'
        [IO.File]::WriteAllText($manifest,(@{format=1;files=$entries}|ConvertTo-Json -Depth 4 -Compress))
        $script:campaignCandidateHash=(Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
        $script:campaignCatalogPath=Join-Path $TestDrive 'catalog.json'
        [IO.File]::WriteAllText($script:campaignCatalogPath,'{"helpers":[]}')
        $script:campaignCatalogHash=(Get-FileHash -LiteralPath $script:campaignCatalogPath -Algorithm SHA256).Hash
        {Assert-CampaignArtifacts} | Should Not Throw
        $extra=Join-Path $script:campaignCandidate 'unlisted.bin'; [IO.File]::WriteAllText($extra,'extra')
        {Assert-CampaignArtifacts} | Should Throw
        Remove-Item -LiteralPath $extra
        $entries[0].path='../outside.bin'
        [IO.File]::WriteAllText($manifest,(@{format=1;files=$entries}|ConvertTo-Json -Depth 4 -Compress))
        $script:campaignCandidateHash=(Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
        {Assert-CampaignArtifacts} | Should Throw
    }
}

Describe 'Existing controlled crash evidence is not repeated or overstated' {
    It 'requires both database proofs and rejects controller fallback or missing persistence' {
        $checks=@('host-agent.db','notifications.db' | ForEach-Object{[pscustomobject]@{database=$_;quickCheck='ok';committedProofPreserved=$true;uncommittedProofAbsent=$true;acknowledgedSnapshotsPreserved=$true}})
        $result=[pscustomobject]@{runId='fixture';serviceName='HostAgentDevScm_fixture';scenario='isolated-scm-controlled-crash-recovery';outcome='PASS';candidateManifestSha256='manifest';protectedServiceUnchanged=$true;controllerFallbackStopUsed=$false;crash=[pscustomobject]@{recovery='controller_start';crashedProcessId=9;recoveredProcessId=10;databaseChecks=[pscustomobject]@{value=$checks;Count=2}}}
        Test-DevCrashProofResult $result 'fixture' 'manifest' | Should Be $true
        $checks[1].uncommittedProofAbsent=$false
        Test-DevCrashProofResult $result 'fixture' 'manifest' | Should Be $false
        $checks[1].uncommittedProofAbsent=$true; $result.controllerFallbackStopUsed=$true
        Test-DevCrashProofResult $result 'fixture' 'manifest' | Should Be $false
    }
}
