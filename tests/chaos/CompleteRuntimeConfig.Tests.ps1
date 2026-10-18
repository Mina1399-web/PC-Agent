$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
foreach($relative in @('tests\HostAgent.PipeProbe\bin\Release\net10.0-windows\HostAgent.PipeProbe.runtimeconfig.json','tests\HostAgent.Infrastructure.Tests\bin\Release\net10.0-windows\HostAgent.Infrastructure.Tests.runtimeconfig.json')) {
 $config=Get-Content -LiteralPath (Join-Path $repo $relative) -Raw | ConvertFrom-Json
 $names=@($config.runtimeOptions.frameworks|ForEach-Object {$_.name})
 if($names -notcontains 'Microsoft.WindowsDesktop.App'){throw 'current_wpf_runtime_framework_not_resolved'}
}
'complete_wpf_runtime_configs_passed'
