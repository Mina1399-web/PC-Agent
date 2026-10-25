param([Parameter(Mandatory)][string]$PackagePath,[Parameter(Mandatory)][string]$WixPath,[string]$Version='1.0.0',[switch]$DevVm,[Security.Cryptography.X509Certificates.X509Certificate2]$SigningCertificate)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if ($env:COMPUTERNAME -ne 'AgentPC' -or (Get-CimInstance Win32_ComputerSystem).Model -ne 'VirtualBox' -or
    (Get-CimInstance Win32_ComputerSystemProduct).UUID -ne '46543236-8C5E-49DA-953C-DB17F9B1E402' -or
    (Get-Service VBoxService).Status -ne 'Running') { throw 'release_dev_vm_required' }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'release_version_invalid' }
if (-not $DevVm -and ($null -eq $SigningCertificate -or -not $SigningCertificate.HasPrivateKey)) { throw 'publisher_signature_required' }
$root=[IO.Path]::GetFullPath($PackagePath)
$output=Join-Path $root 'HostAgent.msi'
if (Test-Path -LiteralPath $output) { throw 'release_output_exists' }
$work=Join-Path ([IO.Path]::GetTempPath()) ('HostAgent-msi-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $work
$dtfRoot=Join-Path (Split-Path (Split-Path ([IO.Path]::GetFullPath($WixPath)))) 'dtf-5.0.2'
. (Join-Path $PSScriptRoot 'installer/Build-InstallerAction.ps1')
. (Join-Path $PSScriptRoot 'installer/New-InstallerSource.ps1')
$actionCertificate=if($DevVm){$null}else{$SigningCertificate}
$action=Build-HostAgentInstallerAction -WorkDirectory $work -DtfRoot $dtfRoot -Version $Version -SigningCertificate $actionCertificate
$embedded=$action.EmbeddedPath
$channel=if($DevVm){'DevVm'}else{'SignedRelease'}
$devMachineGuid=if($DevVm){(Get-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography' -Name MachineGuid).MachineGuid}else{''}
$xml=New-HostAgentInstallerSource -PackageRoot $root -Version $Version -EmbeddedActionPath $embedded -Channel $channel -DevMachineGuid $devMachineGuid
$source=Join-Path $work 'Product.wxs'
[IO.File]::WriteAllText($source,$xml,[Text.UTF8Encoding]::new($false))
& $WixPath build $source -arch x64 -ext WixToolset.Util.wixext/5.0.2 -pdbtype none -out $output | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'release_installer_build_failed' }
@{built=$true;installer=$output;intermediate=$work} | ConvertTo-Json -Compress
