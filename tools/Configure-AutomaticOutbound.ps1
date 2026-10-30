#requires -Version 7.0
#requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$cli = Join-Path $env:ProgramFiles 'HostAgent/Cli/HostAgent.Cli.exe'
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) { throw 'installed_cli_required' }
Write-Host 'Pushover and heartbeat will send automatically after configuration.'
Write-Host 'Enter secrets only into the masked local CLI prompts. No transcript is created.'
Write-Host 'Pushover: obtain your application API token and recipient user key from your own Pushover account.'
Write-Host 'Heartbeat: obtain your own HTTPS ping URL from Healthchecks or a compatible provider.'
foreach ($name in @('PushoverToken', 'PushoverUser', 'HeartbeatUrl')) {
    Write-Host "Configure $name (Esc cancels)."
    & $cli secret-set $name
    if ($LASTEXITCODE -ne 0) { throw "secret_setup_not_completed:$name" }
}
$config = Get-Content -LiteralPath (Join-Path $env:ProgramData 'HostAgent/service.json') -Raw | ConvertFrom-Json
if ($config.mode -notin @('Shadow', 'Confirmation')) { throw 'configuration_unavailable' }
# Existing CLI explicitly reviews and confirms service configuration with APPLY.
& $cli configure $config.mode on on
if ($LASTEXITCODE -ne 0) { throw 'outbound_configuration_not_completed' }
Write-Host 'Automatic Pushover and heartbeat enabled. Check actual delivery and provider history.'
Read-Host 'Press Enter to close'
