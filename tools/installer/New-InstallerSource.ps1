function New-HostAgentInstallerSource {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PackageRoot,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$EmbeddedActionPath,
        [Parameter(Mandatory)][string]$Channel,
        [string]$DevMachineGuid=''
    )
    $ErrorActionPreference='Stop'
    Set-StrictMode -Version Latest
    if($Version -cnotmatch '^\d+\.\d+\.\d+$'){throw 'release_version_invalid'}
    if($Channel -cnotin @('DevVm','SignedRelease','SubmissionPreview')){throw 'release_channel_invalid'}
    if($Channel -ceq 'DevVm' -and $DevMachineGuid -notmatch '^[A-Fa-f0-9-]{36}$'){throw 'release_dev_vm_required'}
    function Resolve-SourcePath([string]$Path,[bool]$Directory){
        if(-not [IO.Path]::IsPathFullyQualified($Path)){throw 'release_path_invalid'}
        $full=[IO.Path]::GetFullPath($Path).TrimEnd('\','/')
        if(-not (Test-Path -LiteralPath $full)){throw 'release_missing_component'}
        $item=Get-Item -LiteralPath $full -Force
        if($item.PSIsContainer -ne $Directory){throw 'release_path_invalid'}
        for($cursor=$item;$null -ne $cursor;$cursor=if($cursor -is [IO.DirectoryInfo]){$cursor.Parent}else{$cursor.Directory}){
            if($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'release_reparse_point'}
        }
        return $full
    }
    $root=Resolve-SourcePath $PackageRoot $true
    $embedded=Resolve-SourcePath $EmbeddedActionPath $false
    foreach($project in @('Service','Helper','Cli','Desktop','docs')){
        $null=Resolve-SourcePath (Join-Path $root $project) $true
    }
    $DevVm=$Channel -ceq 'DevVm'
function Escape([string]$value) { [Security.SecurityElement]::Escape($value) }
$components=[Text.StringBuilder]::new()
$refs=[Text.StringBuilder]::new()
$directories=[Text.StringBuilder]::new()
$counter=0
$cliId=$null
foreach ($required in @('LICENSE','docs/operations/privacy.md','docs/legal/dotnet-runtime-10.0.12-LICENSE.txt','docs/legal/dotnet-runtime-10.0.12-THIRD-PARTY-NOTICES.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $required) -PathType Leaf)) { throw 'release_missing_notices' }
    $null=Resolve-SourcePath (Join-Path $root $required) $false
}
$counter++
$null=$components.Append('<DirectoryRef Id="ProductDirectory"><Component Id="Component'+$counter+'" Guid="*"><File Id="File'+$counter+'" Source="'+(Escape (Join-Path $root 'LICENSE'))+'" KeyPath="yes" /></Component></DirectoryRef>')
$null=$refs.Append('<ComponentRef Id="Component'+$counter+'" />')
foreach ($project in @('Service','Helper','Cli','Desktop','docs')) {
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
        $shortcut=if($project -eq 'Desktop' -and $file.Name -eq 'HostAgent.Desktop.exe'){'<Shortcut Id="DesktopShortcut" Directory="ProgramMenuFolder" Name="Host Agent" WorkingDirectory="DirDesktop" Advertise="yes" />'}else{''}
        $null=$components.Append('<DirectoryRef Id="'+$directoryIds[$file.DirectoryName]+'"><Component Id="Component'+$counter+'" Guid="*"><File Id="'+$id+'" Source="'+(Escape $file.FullName)+'" KeyPath="yes">'+$shortcut+'</File>')
        if ($file.DirectoryName -eq $projectRoot -and $file.Name -eq ('HostAgent.'+$project+'.exe') -and $project -in @('Service','Helper')) {
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
if ($null -eq $cliId) { throw 'release_missing_component' }
$devCondition=''
if ($DevVm) {
    $machineGuid=$DevMachineGuid
    $devCondition='<Property Id="DEV_MACHINE_GUID"><RegistrySearch Id="FindDevMachine" Root="HKLM" Key="SOFTWARE\Microsoft\Cryptography" Name="MachineGuid" Type="raw" /></Property><Launch Condition="ComputerName~=&quot;AgentPC&quot; AND DEV_MACHINE_GUID=&quot;'+$machineGuid+'&quot;" Message="This unsigned DEV build is restricted to the approved development VM." />'
}
$previewCondition=''
if ($Channel -ceq 'SubmissionPreview') {
    $disclosure='Read the bundled docs/operations/privacy.md before installation. Host Agent installs HostAgent (NT SERVICE\HostAgent) and HostAgent.Helper (LocalSystem), writes Program Files and protected ProgramData, adds read-only Performance Monitor Users membership for HostAgent, and retains local data/configuration on uninstall. Initial defaults are Shadow/read-only and outbound features off; existing configuration is preserved. To acknowledge these changes, pass HOSTAGENT_INSTALL_ACK=YES. Uninstall remains available without acknowledgement.'
    $previewCondition='<Property Id="HOSTAGENT_INSTALL_ACK" Secure="yes" /><Launch Condition="REMOVE~=&quot;ALL&quot; OR HOSTAGENT_INSTALL_ACK=&quot;YES&quot;" Message="'+(Escape $disclosure)+'" />'
}
$xml=@"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">
<Package Name="Host Agent V1" Manufacturer="Host Agent" Version="$Version" UpgradeCode="E839C131-FD8D-45AA-927E-A2364BD39AE2" Scope="perMachine" InstallerVersion="500">
<MajorUpgrade DowngradeErrorMessage="A newer Host Agent version is installed." Schedule="afterInstallInitialize" />
<MediaTemplate EmbedCab="yes" CompressionLevel="high" />
<Launch Condition="NOT DISABLEROLLBACK AND NOT RollbackDisabled" Message="Host Agent requires Windows Installer rollback to safely configure read-only performance counters." />
$devCondition
$previewCondition
<Property Id="EXISTING_MONITOR"><RegistrySearch Id="FindMonitor" Root="HKLM" Key="SYSTEM\CurrentControlSet\Services\HostAgent" Name="ImagePath" Type="raw" /></Property>
<Property Id="EXISTING_HELPER"><RegistrySearch Id="FindHelper" Root="HKLM" Key="SYSTEM\CurrentControlSet\Services\HostAgent.Helper" Name="ImagePath" Type="raw" /></Property>
<Launch Condition="Installed OR WIX_UPGRADE_DETECTED OR (NOT EXISTING_MONITOR AND NOT EXISTING_HELPER)" Message="Existing HostAgent services were found. Repair the registered product or preserve and remove the old installation first." />
<StandardDirectory Id="ProgramFiles64Folder"><Directory Id="ProductDirectory" Name="HostAgent">$directories</Directory></StandardDirectory>
<StandardDirectory Id="ProgramMenuFolder" />
$components
<Feature Id="MainFeature" Title="Host Agent" Level="1">$refs</Feature>
<Binary Id="PreflightBinary" SourceFile="$(Escape $embedded)" />
<CustomAction Id="ValidateInstallTree" BinaryRef="PreflightBinary" DllEntry="ValidateInstallTree" Execute="deferred" Impersonate="no" Return="check" />
<CustomAction Id="Bootstrap" FileRef="$cliId" ExeCommand="--installer-bootstrap" Execute="deferred" Impersonate="no" Return="check" />
<CustomAction Id="PrepareCounterRead" BinaryRef="PreflightBinary" DllEntry="PrepareCounterRead" Return="check" />
<CustomAction Id="GrantCounterRead" BinaryRef="PreflightBinary" DllEntry="GrantCounterRead" Execute="deferred" Impersonate="no" Return="check" />
<CustomAction Id="RollbackCounterReadGrant" BinaryRef="PreflightBinary" DllEntry="RollbackCounterReadGrant" Execute="rollback" Impersonate="no" Return="check" />
<CustomAction Id="CommitCounterReadGrant" BinaryRef="PreflightBinary" DllEntry="CommitCounterReadGrant" Execute="commit" Impersonate="no" Return="check" />
<CustomAction Id="ReleaseCounterRead" BinaryRef="PreflightBinary" DllEntry="ReleaseCounterRead" Execute="deferred" Impersonate="no" Return="check" />
<CustomAction Id="RollbackCounterReadRelease" BinaryRef="PreflightBinary" DllEntry="RollbackCounterReadRelease" Execute="rollback" Impersonate="no" Return="check" />
<CustomAction Id="CommitCounterReadRelease" BinaryRef="PreflightBinary" DllEntry="CommitCounterReadRelease" Execute="commit" Impersonate="no" Return="check" />
<InstallExecuteSequence>
<Custom Action="PrepareCounterRead" Before="InstallInitialize" Condition="1" />
<Custom Action="ValidateInstallTree" Before="CreateFolders" Condition="NOT REMOVE~=&quot;ALL&quot;" />
<Custom Action="Bootstrap" After="InstallServices" Condition="NOT REMOVE~=&quot;ALL&quot;" />
<Custom Action="RollbackCounterReadGrant" After="Bootstrap" Condition="NOT REMOVE~=&quot;ALL&quot;" />
<Custom Action="GrantCounterRead" After="RollbackCounterReadGrant" Condition="NOT REMOVE~=&quot;ALL&quot;" />
<Custom Action="CommitCounterReadGrant" After="GrantCounterRead" Condition="NOT REMOVE~=&quot;ALL&quot;" />
<Custom Action="RollbackCounterReadRelease" After="StopServices" Condition="REMOVE~=&quot;ALL&quot; AND NOT UPGRADINGPRODUCTCODE" />
<Custom Action="ReleaseCounterRead" After="RollbackCounterReadRelease" Condition="REMOVE~=&quot;ALL&quot; AND NOT UPGRADINGPRODUCTCODE" />
<Custom Action="CommitCounterReadRelease" After="ReleaseCounterRead" Condition="REMOVE~=&quot;ALL&quot; AND NOT UPGRADINGPRODUCTCODE" />
</InstallExecuteSequence>
</Package></Wix>
"@

    return $xml
}
