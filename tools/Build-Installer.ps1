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
$dtfAssembly=Join-Path $dtfRoot 'wixtoolset.dtf.windowsinstaller/lib/net20/WixToolset.Dtf.WindowsInstaller.dll'
$dtfTools=Join-Path $dtfRoot 'wixtoolset.dtf.customaction/tools'
$managed=Join-Path $work 'Preflight.dll'
$embedded=Join-Path $work 'Preflight.CA.dll'
& "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize+ /target:library ("/out:"+$managed) ("/reference:"+$dtfAssembly) (Join-Path $PSScriptRoot 'installer/Preflight.cs') (Join-Path $PSScriptRoot '../src/HostAgent.Infrastructure/Security/InstallationTreeSecurity.cs') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'release_preflight_compile_failed' }
if (-not $DevVm) {
    if ((Set-AuthenticodeSignature -LiteralPath $managed -Certificate $SigningCertificate -HashAlgorithm SHA256).Status -ne 'Valid') { throw 'publisher_signature_required' }
}
& (Join-Path $dtfTools 'WixToolset.Dtf.MakeSfxCA.exe') $embedded (Join-Path $dtfTools 'x64/SfxCA.dll') $managed (Join-Path $PSScriptRoot 'installer/CustomAction.config') $dtfAssembly | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'release_preflight_package_failed' }
if (-not $DevVm) {
    if ((Set-AuthenticodeSignature -LiteralPath $embedded -Certificate $SigningCertificate -HashAlgorithm SHA256).Status -ne 'Valid') { throw 'publisher_signature_required' }
}
function Escape([string]$value) { [Security.SecurityElement]::Escape($value) }
$components=[Text.StringBuilder]::new()
$refs=[Text.StringBuilder]::new()
$directories=[Text.StringBuilder]::new()
$counter=0
foreach ($project in @('Service','Helper','Cli')) {
    $null=$directories.Append('<Directory Id="Dir'+$project+'" Name="'+$project+'" />')
    $projectRoot=[IO.Path]::GetFullPath((Join-Path $root $project))
    $directoryIds=@{};$directoryIds[$projectRoot]='Dir'+$project
    foreach ($directory in Get-ChildItem -LiteralPath $projectRoot -Directory -Recurse | Sort-Object FullName) {
        if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'release_reparse_point' }
        $directoryId='Dir'+$project+'_'+[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetRelativePath($root,$directory.FullName)))).Substring(0,16)
        $directoryIds[$directory.FullName]=$directoryId
        $null=$components.Append('<DirectoryRef Id="'+$directoryIds[$directory.Parent.FullName]+'"><Directory Id="'+$directoryId+'" Name="'+(Escape $directory.Name)+'" /></DirectoryRef>')
    }
    foreach ($file in Get-ChildItem -LiteralPath $projectRoot -File -Recurse | Sort-Object FullName) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'release_reparse_point' }
        $counter++;$id='File'+$counter
        $null=$components.Append('<DirectoryRef Id="'+$directoryIds[$file.DirectoryName]+'"><Component Id="Component'+$counter+'" Guid="*"><File Id="'+$id+'" Source="'+(Escape $file.FullName)+'" KeyPath="yes" />')
        if ($file.DirectoryName -eq $projectRoot -and $file.Name -eq ('HostAgent.'+$project+'.exe') -and $project -ne 'Cli') {
            $name=if ($project -eq 'Service') {'HostAgent'} else {'HostAgent.Helper'}
            $account=if ($project -eq 'Service') {'NT SERVICE\HostAgent'} else {'LocalSystem'}
            $null=$components.Append('<ServiceInstall Id="Install'+$project+'" Name="'+$name+'" DisplayName="'+$name+'" Type="ownProcess" Start="auto" ErrorControl="normal" Account="'+$account+'" Vital="yes"><util:ServiceConfig FirstFailureActionType="restart" SecondFailureActionType="restart" ThirdFailureActionType="none" ResetPeriodInDays="1" RestartServiceDelayInSeconds="10" /></ServiceInstall><ServiceControl Id="Control'+$project+'" Name="'+$name+'" Start="install" Stop="both" Remove="uninstall" Wait="yes" />')
            $null=$components.Append('<RegistryValue Root="HKLM" Key="SYSTEM\CurrentControlSet\Services\EventLog\Application\HostAgent.'+$project+'" Name="EventMessageFile" Type="expandable" Value="[SystemFolder]mscoree.dll" /><RegistryValue Root="HKLM" Key="SYSTEM\CurrentControlSet\Services\EventLog\Application\HostAgent.'+$project+'" Name="TypesSupported" Type="integer" Value="7" />')
        }
        if ($file.DirectoryName -eq $projectRoot -and $file.Name -eq 'HostAgent.Cli.exe') { $cliId=$id }
        $null=$components.Append('</Component></DirectoryRef>')
        $null=$refs.Append('<ComponentRef Id="Component'+$counter+'" />')
    }
}
if (-not (Get-Variable cliId -ErrorAction SilentlyContinue)) { throw 'release_missing_component' }
$devCondition=''
if ($DevVm) {
    $machineGuid=(Get-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography' -Name MachineGuid).MachineGuid
    if ($machineGuid -notmatch '^[A-Fa-f0-9-]{36}$') { throw 'release_dev_vm_required' }
    $devCondition='<Property Id="DEV_MACHINE_GUID"><RegistrySearch Id="FindDevMachine" Root="HKLM" Key="SOFTWARE\Microsoft\Cryptography" Name="MachineGuid" Type="raw" /></Property><Launch Condition="ComputerName~=&quot;AgentPC&quot; AND DEV_MACHINE_GUID=&quot;'+$machineGuid+'&quot;" Message="This unsigned DEV build is restricted to the approved development VM." />'
}
$xml=@"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">
<Package Name="Host Agent V1" Manufacturer="Host Agent" Version="$Version" UpgradeCode="E839C131-FD8D-45AA-927E-A2364BD39AE2" Scope="perMachine" InstallerVersion="500">
<MajorUpgrade DowngradeErrorMessage="A newer Host Agent version is installed." />
<MediaTemplate EmbedCab="yes" CompressionLevel="high" />
$devCondition
<Property Id="EXISTING_MONITOR"><RegistrySearch Id="FindMonitor" Root="HKLM" Key="SYSTEM\CurrentControlSet\Services\HostAgent" Name="ImagePath" Type="raw" /></Property>
<Property Id="EXISTING_HELPER"><RegistrySearch Id="FindHelper" Root="HKLM" Key="SYSTEM\CurrentControlSet\Services\HostAgent.Helper" Name="ImagePath" Type="raw" /></Property>
<Launch Condition="Installed OR WIX_UPGRADE_DETECTED OR (NOT EXISTING_MONITOR AND NOT EXISTING_HELPER)" Message="Existing HostAgent services were found. Repair the registered product or preserve and remove the old installation first." />
<StandardDirectory Id="ProgramFiles64Folder"><Directory Id="ProductDirectory" Name="HostAgent">$directories</Directory></StandardDirectory>
$components
<Feature Id="MainFeature" Title="Host Agent" Level="1">$refs</Feature>
<Binary Id="PreflightBinary" SourceFile="$(Escape $embedded)" />
<CustomAction Id="ValidateInstallTree" BinaryRef="PreflightBinary" DllEntry="ValidateInstallTree" Execute="deferred" Impersonate="no" Return="check" />
<CustomAction Id="Bootstrap" FileRef="$cliId" ExeCommand="--installer-bootstrap" Execute="deferred" Impersonate="no" Return="check" />
<InstallExecuteSequence><Custom Action="ValidateInstallTree" Before="CreateFolders" Condition="NOT REMOVE~=&quot;ALL&quot;" /><Custom Action="Bootstrap" After="InstallServices" Condition="NOT REMOVE~=&quot;ALL&quot;" /></InstallExecuteSequence>
</Package></Wix>
"@
$source=Join-Path $work 'Product.wxs'
[IO.File]::WriteAllText($source,$xml,[Text.UTF8Encoding]::new($false))
& $WixPath build $source -arch x64 -ext WixToolset.Util.wixext/5.0.2 -pdbtype none -out $output | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'release_installer_build_failed' }
@{built=$true;installer=$output;intermediate=$work} | ConvertTo-Json -Compress
