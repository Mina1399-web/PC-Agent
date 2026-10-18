param([Parameter(Mandatory=$true)][ValidateSet('storage','collector','remediation','wpf')][string]$Scenario,
      [Parameter(Mandatory=$true)][string]$Bundle,[Parameter(Mandatory=$true)][string]$LogDirectory)
$ErrorActionPreference='Stop'
if($env:COMPUTERNAME -ne 'AGENTPC' -or (Get-CimInstance Win32_ComputerSystem -OperationTimeoutSec 5).Model -ne 'VirtualBox' -or
   (Get-CimInstance Win32_ComputerSystemProduct -OperationTimeoutSec 5).UUID -ne '46543236-8C5E-49DA-953C-DB17F9B1E402'){throw 'fixture_vm_mismatch'}
$scratch=Join-Path $LogDirectory 'scratch'
if(Test-Path -LiteralPath $scratch){throw 'scratch_must_be_new'}
New-Item -ItemType Directory -Path $scratch | Out-Null
$env:TEMP=$scratch;$env:TMP=$scratch
$env:HOSTAGENT_DEV_FIXTURE_ROOT=$scratch
$env:DOTNET_ROOT='C:\Users\AgentPCdevtest\AppData\Local\HostAgent-dev-sdk\10.0.401'
$env:DOTNET_CLI_HOME=$scratch
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1';$env:DOTNET_NOLOGO='1'
$filters=@{
    storage='FullyQualifiedName~DisposableStorageResilienceTests'
    collector='FullyQualifiedName~CollectionCoordinatorTests|FullyQualifiedName~HealthStabilizationTests.Mandatory_collector_health_recovers_after_failure_and_backoff_without_hiding_other_workers'
    remediation='FullyQualifiedName~RemediationGuardTests'
    wpf='FullyQualifiedName~DashboardUiTests.Real_wpf_window_renders_synthetic_data_and_clears_disconnected_metrics'
}
$assembly=if($Scenario -eq 'wpf'){'desktop\HostAgent.Desktop.Tests.dll'}else{'infrastructure\HostAgent.Infrastructure.Tests.dll'}
$dotnet=Join-Path $env:DOTNET_ROOT 'dotnet.exe'
& $dotnet vstest (Join-Path $Bundle $assembly) ('/TestCaseFilter:'+$filters[$Scenario]) ('/ResultsDirectory:'+$LogDirectory) '/Logger:trx;LogFileName=tests.trx'
exit $LASTEXITCODE
