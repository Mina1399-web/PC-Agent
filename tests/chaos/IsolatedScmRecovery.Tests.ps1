$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Describe 'SCM recovery scope and bounded policy; no native service calls' {
    It 'rejects protected names and requires one delayed restart followed by no action' {
        $source=Join-Path $repo 'scripts/chaos/IsolatedScmRecovery.cs'
        if(Test-Path $source){Add-Type -Path $source}
        # A policy accepting no restart would falsely prove automatic recovery.
        $accepted=if('IsolatedScmRecovery' -as [type]){[IsolatedScmRecovery]::IsBounded(@(0,0),@(0,0))}else{$true}
        $accepted | Should Be $false
        [IsolatedScmRecovery]::IsBounded(@(1,0),@(5000,0)) | Should Be $true
        [IsolatedScmRecovery]::IsBounded(@(1,1),@(5000,5000)) | Should Be $false
        [IsolatedScmRecovery]::Name('9aa86a18379245f2940c3224ae6c4967') | Should Be 'HostAgentDevScm_9aa86a18379245f2940c3224ae6c4967'
        {[IsolatedScmRecovery]::Name('HostAgent')} | Should Throw
        {[IsolatedScmRecovery]::Name('00000000000000000000000000000000')} | Should Throw
    }
}
