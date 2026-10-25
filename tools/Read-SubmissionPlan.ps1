#requires -Version 7.0
param([Parameter(Mandatory)][string]$PackagePath,[Parameter(Mandatory)][string]$ExpectedSourceCommit,[Parameter(Mandatory)][string]$ExpectedVersion,[Parameter(Mandatory)][string]$ExpectedManifestSha256)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($PackagePath)
& (Join-Path $PSScriptRoot 'Test-SubmissionPreview.ps1') -PackagePath $root -ExpectedSourceCommit $ExpectedSourceCommit -ExpectedVersion $ExpectedVersion -ExpectedManifestSha256 $ExpectedManifestSha256
if($LASTEXITCODE -ne 0){throw 'submission_plan_validation_failed'}
Get-Content -LiteralPath (Join-Path $root 'docs/operations/privacy.md')
Get-Content -LiteralPath (Join-Path $root 'docs/operations/submission-preview.md')
[pscustomobject]@{
 readOnlyPlan=$true;changesPerformed=$false;version=$ExpectedVersion;sourceCommit=$ExpectedSourceCommit
 services=@(@{name='HostAgent';account='NT SERVICE\HostAgent';start='automatic'},@{name='HostAgent.Helper';account='LocalSystem';start='automatic'})
 announcedChanges=@('Program Files\HostAgent payload and Start menu shortcut','protected local ProgramData and configuration','product Event Log sources','HostAgent read-only Performance Monitor Users membership','data, configuration and secrets retained on uninstall')
 freshDefaults=@('Shadow','readOnly=true','network probes off','Pushover off','heartbeat off')
 existingConfiguration='preserved: independently verify Shadow/readOnly and outbound choices before upgrading'
 manualInstallation='After authorized native acceptance preparation, use Windows Installer with HOSTAGENT_INSTALL_ACK=YES and /norestart. This reader performs no installation or elevation.'
} | ConvertTo-Json -Depth 4
