$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'scripts/chaos/DevResilienceCampaign.psm1') -Force
if(Test-Path (Join-Path $repo 'scripts/chaos/RemainingResilience.psm1')){Import-Module (Join-Path $repo 'scripts/chaos/RemainingResilience.psm1') -Force}
Describe 'Independent remaining gates continue only with positive safety evidence' {
    It 'records a scenario failure and still executes the next independent scenario' {
        $script:next=0
        $plan=@(@{id='bad';run={throw 'fixture_fault'}},@{id='next';run={$script:next++;@{ok=$true}}})
        # Existing stop-first engine is intentionally the red baseline.
        $command=if(Get-Command Invoke-RemainingResiliencePlan -ErrorAction SilentlyContinue){'Invoke-RemainingResiliencePlan'}else{'Invoke-DevResiliencePlan'}
        foreach($p in $plan){$p.kind='Auto';$p.limits=@{}}
        if($command -eq 'Invoke-RemainingResiliencePlan'){$rows=@(& $command $plan 'fixture' {} {} {})}
        else{$rows=@(& $command $plan 'fixture' {} {})}
        ($rows.status -join ',') | Should Be 'FAIL,PASS'
        $script:next | Should Be 1
    }
    It 'skips successors when quiescence is not confirmed after a failed scenario' {
        $plan=@(@{id='bad';run={throw 'fixture_fault'}},@{id='next';run={throw 'must_not_run'}})
        $rows=@(Invoke-RemainingResiliencePlan $plan 'fixture' {} {throw 'child_still_running'} {})
        ($rows.status -join ',') | Should Be 'FAIL,NOT RUN'
        $rows[0].reason | Should Be 'safety_abort_or_cleanup_unconfirmed'
    }
    It 'never invokes a phase when the protected baseline or resource guard fails' {
        $script:calls=0
        $plan=@(@{id='unsafe';run={$script:calls++;@{ok=$true}}},@{id='next';run={$script:calls++;@{ok=$true}}})
        $rows=@(Invoke-RemainingResiliencePlan $plan 'fixture' {throw 'soft_free_memory_floor_768_mib'} {} {})
        ($rows.status -join ',') | Should Be 'NOT RUN,NOT RUN'
        $script:calls | Should Be 0
    }
    It 'preserves a failed result and manual gate while continuing a fresh independent fixture' {
        $plan=@(@{id='bad';run={@{ok=$false;proof='preserved'}}},@{id='manual';kind='Manual';reason='manual_only'},@{id='next';run={@{ok=$true}}})
        $rows=@(Invoke-RemainingResiliencePlan $plan 'fixture' {} {} {})
        ($rows.status -join ',') | Should Be 'FAIL,NOT RUN,PASS'
        $rows[0].details.proof | Should Be 'preserved'
    }
    It 'does not claim PASS when safe quiescence required an additional cleanup stop' {
        $plan=@(@{id='cleanup';run={@{ok=$true}}},@{id='next';run={@{ok=$true}}})
        $rows=@(Invoke-RemainingResiliencePlan $plan 'fixture' {} {param($phase) @{scenarioFailed=($phase.id -eq 'cleanup')}} {})
        ($rows.status -join ',') | Should Be 'FAIL,PASS'
        $rows[0].reason | Should Be 'scenario_required_additional_cleanup'
    }
    It 'latches a transient resource guard failure even after memory would recover' {
        $tokens=$null;$errors=$null
        $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/chaos/run-remaining-dev-resilience.ps1'),[ref]$tokens,[ref]$errors)
        $errors.Count | Should Be 0
        $definition=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Assert-RemainingFast'},$true)
        . ([scriptblock]::Create($definition.Extent.Text))
        $script:remainingSafetyLatched=$false;$script:remainingFootprint=$null;$script:guardCalls=0
        function Assert-CampaignFastSafety {$script:guardCalls++;if($script:guardCalls -eq 1){throw 'soft_free_memory_floor_768_mib'}}
        {Assert-RemainingFast} | Should Throw
        {Assert-RemainingFast} | Should Throw
        $script:guardCalls | Should Be 1
    }
}
Describe 'Remaining result acceptance is fail closed' {
    It 'requires nonzero executed tests and rejects failed or skipped tests' {
        $file=Join-Path $TestDrive 'tests.trx'
        [IO.File]::WriteAllText($file,'<TestRun><ResultSummary><Counters total="3" executed="3" passed="3" failed="0"/></ResultSummary></TestRun>')
        (Read-RemainingTrx $file 3).ok | Should Be $true
        [IO.File]::WriteAllText($file,'<TestRun><ResultSummary><Counters total="0" executed="0" passed="0" failed="0"/></ResultSummary></TestRun>')
        (Read-RemainingTrx $file 3).ok | Should Be $false
        [IO.File]::WriteAllText($file,'<TestRun><ResultSummary><Counters total="3" executed="3" passed="2" failed="1"/></ResultSummary></TestRun>')
        (Read-RemainingTrx $file 3).ok | Should Be $false
    }
    It 'cannot label controller recovery as SCM recovery or hide a second start' {
        $checks=@('host-agent.db','notifications.db'|ForEach-Object{@{database=$_;quickCheck='ok';committedProofPreserved=$true;uncommittedProofAbsent=$true;acknowledgedSnapshotsPreserved=$true}})
        $r=[pscustomobject]@{runId='fixture';serviceName='HostAgentDevScm_fixture';scenario='isolated-scm-automatic-crash-recovery';candidateManifestSha256='manifest';outcome='PASS';protectedServiceUnchanged=$true;recoveryDisabled=$true;controllerFallbackStopUsed=$false;firstProcessId=10;scmOperations=@(@{operation='start';exitCode=0},@{operation='stop';exitCode=0});crash=@{recovery='scm_failure_action_only';crashedProcessId=10;recoveredProcessId=11;databaseChecks=$checks}}
        Test-RemainingAutoScmResult $r fixture manifest | Should Be $true
        $r.crash.recovery='controller_start'
        Test-RemainingAutoScmResult $r fixture manifest | Should Be $false
        $r.crash.recovery='scm_failure_action_only';$r.scmOperations+=@{operation='start';exitCode=0}
        Test-RemainingAutoScmResult $r fixture manifest | Should Be $false
    }
}
