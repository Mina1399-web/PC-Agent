$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Add-Type -Path (Join-Path $repo 'scripts/chaos/BoundedScmPressure.cs')
Import-Module (Join-Path $repo 'scripts/chaos/IsolatedScmSafety.psm1') -Force

Describe 'Watchdog-only stop attribution' {
    It 'rejects a protected service process replacement even with unchanged state and hashes' {
        $tokens=$null; $errors=$null
        $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/chaos/run-isolated-scm-candidate.ps1'),[ref]$tokens,[ref]$errors)
        $definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-TaskProtectedService'},$true)
        . ([scriptblock]::Create($definition.Extent.Text))
        $script:fixtureProtected = [pscustomobject]@{Name='HostAgent';State='Running';StartMode='Auto';StartName='NT SERVICE\HostAgent';PathName='fixture';ImageSha256='hash';AssemblyHashes=@{Core='same'};ProcessId=20}
        $script:taskExpectedProtectedAssemblyHashes=@{Core='same'}
        function Get-TaskProtectedService { return $script:fixtureProtected }
        $expected = $script:fixtureProtected.PSObject.Copy()
        $script:fixtureProtected.ProcessId=21
        {Assert-TaskProtectedService $expected} | Should Throw
    }
    It 'accepts only a matching confirmed below-768 stop with successful pressure release' {
        $id = '123456781234123412341234567890ab'
        $marker = [pscustomobject]@{runId=$id;serviceName=('HostAgentDevScm_'+$id);floorMiB=768;availableMemoryMiB=751;stopConfirmed=$true;action='stop_isolated_test_service'}
        Test-IsolatedWatchdogStopEvidence $marker $id 73 0 'Stopped' $false | Should Be $true
        Test-IsolatedWatchdogStopEvidence $marker $id 74 0 'Stopped' $false | Should Be $false
        Test-IsolatedWatchdogStopEvidence $marker $id 73 21 'Stopped' $true | Should Be $false
        Test-IsolatedWatchdogStopEvidence $marker $id 73 0 'Running' $false | Should Be $false
        $marker.stopConfirmed = $false
        Test-IsolatedWatchdogStopEvidence $marker $id 73 0 'Stopped' $false | Should Be $false
        $marker.stopConfirmed = $true; $marker.serviceName = 'HostAgent'
        Test-IsolatedWatchdogStopEvidence $marker $id 73 0 'Stopped' $false | Should Be $false
        $marker.serviceName = 'HostAgentDevScm_'+$id; $marker.availableMemoryMiB = 512
        Test-IsolatedWatchdogStopEvidence $marker $id 73 0 'Stopped' $false | Should Be $false
        $marker.availableMemoryMiB = 768
        Test-IsolatedWatchdogStopEvidence $marker $id 73 0 'Stopped' $false | Should Be $false
    }
}

Describe 'Bounded SCM pressure safety decisions' {
    It 'aborts at the hard floor even when a watchdog marker exists' {
        [BoundedScmPressure]::Decide(512, 1, $true, $true, $false) | Should Be 'hard_abort'
    }
    It 'fails closed on unknown memory' {
        [BoundedScmPressure]::Decide([double]::NaN, 1, $true, $false, $false) | Should Be 'hard_abort'
    }
    It 'releases pressure immediately when the watchdog marker appears' {
        [BoundedScmPressure]::Decide(749, 1, $true, $true, $false) | Should Be 'marker'
    }
    It 'aborts after sixty seconds and on parent loss or cancellation' {
        [BoundedScmPressure]::Decide(1500, 60, $true, $false, $false) | Should Be 'deadline'
        [BoundedScmPressure]::Decide(1500, 1, $false, $false, $false) | Should Be 'parent_lost'
        [BoundedScmPressure]::Decide(1500, 1, $true, $false, $true) | Should Be 'cancelled'
    }
    It 'holds rather than allocates after crossing 752 MiB' {
        [BoundedScmPressure]::Decide(752, 1, $true, $false, $false) | Should Be 'hold'
        [BoundedScmPressure]::Decide(767, 1, $true, $false, $false) | Should Be 'allocate'
    }
}

Describe 'Native pressure lifetime without memory pressure or SCM' {
    It 'kills the exact assigned harmless child when the parent job handle closes' {
        $job = [ScmPressureJob]::new()
        $child = $null
        try {
            $info = [Diagnostics.ProcessStartInfo]::new()
            $info.FileName = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
            $info.Arguments = '-NoProfile -Command "Start-Sleep -Seconds 60"'
            $info.UseShellExecute = $false
            $info.CreateNoWindow = $true
            $child = [Diagnostics.Process]::Start($info)
            $job.Assign($child)
            $child.HasExited | Should Be $false
            $job.Dispose()
            $child.WaitForExit(5000) | Should Be $true
        } finally {
            $job.Dispose()
            if ($child) { if (-not $child.HasExited) { $child.Kill() }; $child.Dispose() }
        }
    }
    It 'launches the real pressure child but allocates nothing when a marker already exists' {
        . (Join-Path $repo 'scripts/chaos/Invoke-BoundedScmPressure.ps1')
        function Assert-TaskSafetyFloor { if ([BoundedScmPressure]::FreeMiB() -lt 1024) { throw 'fixture_memory_preflight' } }
        function Assert-TaskProtectedService { }
        function Assert-TaskNoReparseAncestors { }
        $script:fixtureIdentityCalls = 0
        function Assert-TaskIsolatedService {
            $script:fixtureIdentityCalls++
            [pscustomobject]@{State=$(if ($script:fixtureIdentityCalls -eq 1) {'Running'} else {'Stopped'})}
        }
        $script:ProtectedBaseline = $null
        $script:taskId = '123456781234123412341234567890ab'
        $script:taskEvidenceDirectory = Join-Path $TestDrive 'pressure-no-allocation'
        New-Item -ItemType Directory $script:taskEvidenceDirectory | Out-Null
        $script:taskMemoryMarkerPath = Join-Path $script:taskEvidenceDirectory 'marker.json'
        [pscustomobject]@{runId=$script:taskId;serviceName=('HostAgentDevScm_'+$script:taskId);floorMiB=768;availableMemoryMiB=751;stopConfirmed=$true;action='stop_isolated_test_service'} |
            ConvertTo-Json | Set-Content -LiteralPath $script:taskMemoryMarkerPath
        $info = [Diagnostics.ProcessStartInfo]::new()
        $info.FileName = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
        $info.Arguments = '-NoProfile -Command "exit 73"'
        $info.UseShellExecute = $false; $info.CreateNoWindow=$true
        $script:taskMemoryWatchdog = [Diagnostics.Process]::Start($info)
        try {
            $report = Invoke-TaskWatchdogPressure
            $report.confirmed | Should Be $true
            $report.pressureExit | Should Be 0
            $report.reason | Should Be 'marker'
            $report.gateOpened | Should Be $true
            $report.hardAbort | Should Be $false
        } finally {
            if (-not $script:taskMemoryWatchdog.HasExited) {$script:taskMemoryWatchdog.Kill()}
            $script:taskMemoryWatchdog.Dispose()
        }
    }
}
