$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'scripts/chaos/IsolatedScmCrashSafety.psm1') -Force
Describe 'Exact isolated crash process selection' {
    It 'allows the matching GUID service and image and rejects protected or reused targets' {
        $id='123456781234123412341234567890ab'
        $image='C:\ProgramData\HostAgent-DEV-SCM-'+$id+'\app\HostAgent.PipeProbe.exe'
        $command='"'+$image+'" isolated-scm-service '+$id+' S-1-5-21-1-2-3-1000 crash-proof'
        $service=[pscustomobject]@{Name=('HostAgentDevScm_'+$id);State='Running';ProcessId=1234;StartName=('NT SERVICE\HostAgentDevScm_'+$id);PathName=$command}
        $process=[pscustomobject]@{ProcessId=1234;ExecutablePath=$image;CommandLine=$command}
        Test-IsolatedCrashTarget $id $service $process 5678 | Should Be $true
        Test-IsolatedCrashTarget $id $service $process 1234 | Should Be $false
        $process.ProcessId=1235
        Test-IsolatedCrashTarget $id $service $process 5678 | Should Be $false
        $process.ProcessId=1234; $service.Name='HostAgent'
        Test-IsolatedCrashTarget $id $service $process 5678 | Should Be $false
        $service.Name='HostAgentDevScm_'+$id; $process.ExecutablePath='C:\Program Files\HostAgent\Service\HostAgent.Service.exe'
        Test-IsolatedCrashTarget $id $service $process 5678 | Should Be $false
    }
}
