$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'scripts/chaos/IsolatedScmSafety.psm1') -Force

function New-TestLockedPackageAcl {
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    $inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
    foreach ($sid in @('S-1-5-32-544','S-1-5-18')) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new($sid),
            [Security.AccessControl.FileSystemRights]::FullControl, $inheritance,
            [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow))
    }
    return $acl
}

Describe 'Isolated SCM safety guards' {
    It 'stops the watchdog before removing its executable package' {
        $script:fixtureWatchdogStopped = $false
        $removed = Remove-IsolatedScmPackageAfterWatchdog {
            $script:fixtureWatchdogStopped = $true
        } {
            # Model the open executable handle: deletion succeeds only after stop.
            return $script:fixtureWatchdogStopped
        }
        $removed | Should Be $true
    }

    It 'preserves the package if watchdog shutdown fails' {
        $script:fixtureRemoveCalls = 0
        $failure = $null
        try {
            Remove-IsolatedScmPackageAfterWatchdog { throw 'fixture_shutdown_failed' } {
            $script:fixtureRemoveCalls++; return $true
            }
        } catch { $failure = $_.Exception.Message }
        $failure | Should Be 'fixture_shutdown_failed'
        $script:fixtureRemoveCalls | Should Be 0
    }

    It 'accepts only the administrative package root ACL with no service access' {
        Test-IsolatedScmPackageRootAcl (New-TestLockedPackageAcl) | Should Be $true
    }

    It 'rejects even a read-control grant to a service on the package root' {
        $acl = New-TestLockedPackageAcl
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new('S-1-5-80-1-2-3-4-5'),
            [Security.AccessControl.FileSystemRights]::ReadPermissions,
            [Security.AccessControl.AccessControlType]::Allow))
        Test-IsolatedScmPackageRootAcl $acl | Should Be $false
    }

    It 'rejects an unprotected package root ACL' {
        $acl = New-TestLockedPackageAcl
        $acl.SetAccessRuleProtection($false, $true)
        Test-IsolatedScmPackageRootAcl $acl | Should Be $false
    }

    It 'grants no service access on package root' {
        (Get-IsolatedScmAclPolicy PackageRoot).ServiceRights | Should Be 'None'
    }

    It 'grants only read and execute on app and FullControl in isolated data' {
        (Get-IsolatedScmAclPolicy App).ServiceRights | Should Be 'ReadAndExecute'
        (Get-IsolatedScmAclPolicy Data).ServiceRights | Should Be 'FullControl'
    }

    It 'passes each SCM option and its value as separate native arguments' {
        # Defect: grouped "binPath= value" arguments cause sc.exe exit 1639.
        $service = 'HostAgentDevScm_00112233445566778899aabbccddeeff'
        $binary = '"C:\ProgramData\HostAgent-DEV-SCM-00112233445566778899aabbccddeeff\app\HostAgent.PipeProbe.exe" isolated-scm-service 00112233445566778899aabbccddeeff S-1-5-21-1-2-3-1001'
        $actual = @(Get-IsolatedScmCreateArguments $service $binary 'HostAgent DEV SCM fixture')
        $actual.Count | Should Be 12
        $actual[0] | Should Be 'create'
        $actual[1] | Should Be $service
        $actual[2] | Should Be 'binPath='
        $actual[3] | Should Be $binary
        $actual[4] | Should Be 'start='
        $actual[5] | Should Be 'demand'
        $actual[6] | Should Be 'obj='
        $actual[7] | Should Be ('NT SERVICE\' + $service)
        $actual[8] | Should Be 'type='
        $actual[9] | Should Be 'own'
        $actual[10] | Should Be 'DisplayName='
        $actual[11] | Should Be 'HostAgent DEV SCM fixture'
    }

    It 'preserves the exact SCM argv through a harmless native process including embedded quotes' {
        # Defect: PowerShell 5.1 native binding can strip the image-path quotes.
        # This fixture prints argv only. It never calls SCM or executes argv.
        $fixtureSource = Join-Path $TestDrive 'compile-argv.ps1'
        $fixtureExe = Join-Path $TestDrive 'argv-fixture.exe'
        @'
Add-Type -TypeDefinition 'using System; using System.Text; public static class ArgvFixture { public static void Main(string[] args) { foreach (var arg in args) Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(arg))); } }' -OutputAssembly $args[0] -OutputType ConsoleApplication -ErrorAction Stop
'@ | Set-Content -LiteralPath $fixtureSource -Encoding UTF8
        & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -File $fixtureSource $fixtureExe
        $LASTEXITCODE | Should Be 0
        $service = 'HostAgentDevScm_00112233445566778899aabbccddeeff'
        $binary = '"C:\ProgramData\HostAgent DEV fixture\app\HostAgent.PipeProbe.exe" isolated-scm-service 00112233445566778899aabbccddeeff S-1-5-21-1-2-3-1001'
        $expected = @(Get-IsolatedScmCreateArguments $service $binary 'HostAgent DEV SCM fixture') +
            @('', 'C:\space here\', 'say "hello"', 'slash\\\"quote', 'unicode-' + [char]0x017e)
        $info = [Diagnostics.ProcessStartInfo]::new()
        $info.FileName = $fixtureExe
        $info.Arguments = ConvertTo-IsolatedScmNativeCommandLine $expected
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($info)
        try {
            $output = $process.StandardOutput.ReadToEndAsync()
            $errorOutput = $process.StandardError.ReadToEndAsync()
            $process.WaitForExit(10000) | Should Be $true
            $process.ExitCode | Should Be 0
            $errorOutput.GetAwaiter().GetResult() | Should Be ''
            $lines = $output.GetAwaiter().GetResult() -split '\r?\n'
            $lines.Count | Should Be ($expected.Count + 1)
            for ($i = 0; $i -lt $expected.Count; $i++) {
                [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($lines[$i])) | Should Be $expected[$i]
            }
        }
        finally {
            if (-not $process.HasExited) { $process.Kill(); [void]$process.WaitForExit(5000) }
            $process.Dispose()
        }
    }
    It 'starts the real watchdog launcher on Windows PowerShell 5.1 with exact argv and no SCM operation' {
        # Defect: ProcessStartInfo.ArgumentList does not exist on .NET Framework.
        # Load only this function, never the runner's top-level executable flow.
        $tokens = $null; $parseErrors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile(
            (Join-Path $repo 'scripts/chaos/run-isolated-scm-candidate.ps1'), [ref]$tokens, [ref]$parseErrors)
        $definition = $ast.Find({ param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Start-TaskMemoryWatchdog'
        }, $true)
        . ([scriptblock]::Create($definition.Extent.Text))
        function Assert-TaskIsolatedService { $script:fixtureIdentityChecks++ }
        $script:fixtureIdentityChecks = 0
        $script:taskMemoryWatchdog = $null
        $script:taskId = '00112233445566778899aabbccddeeff'
        $script:taskAppDirectory = $TestDrive
        $script:taskMemoryMarkerPath = Join-Path $TestDrive 'watchdog captured argv.txt'
        $script:taskWatchdogStopPath = Join-Path $TestDrive 'watchdog stop marker.txt'
        $script:taskServiceExe = Join-Path $TestDrive 'watchdog-fixture.exe'
        $taskRunResult = @{ memoryGuard = 'not_started' }
        $fixtureSource = Join-Path $TestDrive 'compile-watchdog.ps1'
        @'
Add-Type -TypeDefinition 'using System; using System.IO; using System.Text; using System.Threading; public static class WatchdogFixture { public static int Main(string[] args) { if(args.Length != 5) return 41; using(var writer = new StreamWriter(args[2], false, Encoding.UTF8)) foreach(var arg in args) writer.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(arg))); Thread.Sleep(1500); return 0; } }' -OutputAssembly $args[0] -OutputType ConsoleApplication -ErrorAction Stop
'@ | Set-Content -LiteralPath $fixtureSource -Encoding UTF8
        & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -File $fixtureSource $script:taskServiceExe
        $LASTEXITCODE | Should Be 0
        try {
            { Start-TaskMemoryWatchdog } | Should Not Throw
            $script:fixtureIdentityChecks | Should Be 1
            $taskRunResult.memoryGuard | Should Be 'active_250ms_watchdog_and_inline_checks_at_768_mib'
            $script:taskMemoryWatchdog.WaitForExit(5000) | Should Be $true
            $script:taskMemoryWatchdog.ExitCode | Should Be 0
            $expected = @('isolated-scm-watchdog', '00112233445566778899aabbccddeeff',
                $script:taskMemoryMarkerPath, $script:taskWatchdogStopPath, '768')
            $lines = @(Get-Content -LiteralPath $script:taskMemoryMarkerPath)
            $lines.Count | Should Be 5
            for ($i = 0; $i -lt $expected.Count; $i++) {
                [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($lines[$i])) | Should Be $expected[$i]
            }
        }
        finally {
            if ($script:taskMemoryWatchdog) {
                if (-not $script:taskMemoryWatchdog.HasExited) {
                    $script:taskMemoryWatchdog.Kill(); [void]$script:taskMemoryWatchdog.WaitForExit(5000)
                }
                $script:taskMemoryWatchdog.Dispose()
                $script:taskMemoryWatchdog = $null
            }
        }
    }
}
