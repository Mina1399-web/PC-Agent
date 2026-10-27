<# Private GUID fixture builder. Default is read-only and emits a review-manifest
template. Build is operator-only, offline, and requires the exact reviewed
manifest hash. This script never creates accounts, groups or services. #>
[CmdletBinding()]
param([Alias('RunId')][string]$PreparationRunId,[Alias('FixtureRoot')][string]$PreparationRoot,
    [Alias('ManifestPath')][string]$PreparationManifestPath,[Alias('ExpectedManifestSha256')][string]$PreparationManifestHash,
    [Alias('Build')][switch]$PreparationBuild,[Alias('Execute')][switch]$PreparationExecute)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

function Assert-CounterFixtureRunId([string]$Value) {
    $parsed=[Guid]::Empty
    if($Value -cnotmatch '^[a-f0-9]{32}$' -or -not [Guid]::TryParseExact($Value,'N',[ref]$parsed) -or $parsed -eq [Guid]::Empty){throw 'fixture_run_id_invalid'}
}
function Assert-CounterFixturePlainPath([string]$Path) {
    $cursor=[IO.Path]::GetFullPath($Path)
    while($cursor){if(Test-Path -LiteralPath $cursor){if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'fixture_reparse_path_rejected'}};$cursor=[IO.Path]::GetDirectoryName($cursor)}
}
function Get-CounterFixtureScope([string]$Id,[string]$Root) {
    Assert-CounterFixtureRunId $Id
    $expected=[IO.Path]::GetFullPath((Join-Path 'C:\ProgramData' ('HostAgentCounterFixture-'+$Id)))
    if($Root -and [IO.Path]::GetFullPath($Root).TrimEnd('\') -cne $expected){throw 'fixture_root_invalid'}
    Assert-CounterFixturePlainPath $expected
    [pscustomobject]@{Id=$Id;Root=$expected;Name=('HostAgentFixture_'+$Id);
        Registry=('SOFTWARE\HostAgent.CounterReadFixture.'+$Id);
        ProgramFiles=(Join-Path 'C:\Program Files' ('HostAgentCounterFixture-'+$Id))}
}
function Assert-CounterFixtureVm {
    if($PSVersionTable.PSVersion.Major -lt 7 -or [IntPtr]::Size -ne 8){throw 'powershell7_x64_required'}
    if($env:WINDIR -ine 'C:\Windows' -or $env:COMPUTERNAME -ne 'AGENTPC' -or [Security.Principal.WindowsIdentity]::GetCurrent().Name -ne 'AgentPC\AgentPCdevtest' -or
        (Get-CimInstance Win32_ComputerSystem).Model -ne 'VirtualBox' -or
        (Get-CimInstance Win32_ComputerSystemProduct).UUID -ne '46543236-8C5E-49DA-953C-DB17F9B1E402' -or
        (Get-Service VBoxService).Status -ne 'Running'){throw 'approved_dev_vm_required'}
}
function Assert-CounterFixtureAdmin {
    $principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'elevated_operator_required'}
}
function Assert-CounterFixtureResources {
    param([switch]$DuringRun)
    $freeMiB=(Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 5).FreePhysicalMemory/1024
    if($DuringRun){
        if($freeMiB -le 512){throw 'hard_memory_floor_512'}
        if($freeMiB -lt 768){Write-Warning 'soft_memory_floor_768'}
    }else{
        if($freeMiB -le 768){throw 'initial_ram_not_above_768_mib'}
        $script:FixtureInitialFreeMiB=$freeMiB
    }
    if((Get-PSDrive C).Free -lt 10GB){throw 'initial_disk_below_10_gib'}
}
function Get-CounterFixtureInputs {
    $repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    $tools=Join-Path (Split-Path $repo) 'HostAgent-DEV-tools'
    $paths=@((Join-Path $repo 'tools\installer\CounterReadTransaction.cs'),(Join-Path $repo 'tools\installer\CounterReadActions.cs'),
        (Join-Path $repo 'tools\installer\WindowsCounterReadState.cs'),(Join-Path $repo 'tools\installer\CustomAction.config'),
        (Join-Path $PSScriptRoot 'Prepare-CounterReadMsiFixture.ps1'),(Join-Path $PSScriptRoot 'Test-CounterReadMsiFixture.ps1'),
        (Join-Path $PSScriptRoot 'CounterReadMsiFixtureController.cs'),(Join-Path $PSScriptRoot 'CounterReadMsiFixtureFailureCA.cs'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),(Join-Path $env:WINDIR 'System32\sc.exe'))
    foreach($directory in @((Join-Path $tools 'wix'),(Join-Path $tools 'dtf-5.0.2'))){
        Assert-CounterFixturePlainPath $directory
        $paths+=@(Get-ChildItem -LiteralPath $directory -File -Recurse|ForEach-Object FullName)
    }
    if($paths.Count -gt 4096){throw 'fixture_input_limit'}
    foreach($path in ($paths|Sort-Object -Unique)){Assert-CounterFixturePlainPath $path;Get-Item -LiteralPath $path|ForEach-Object {[pscustomobject]@{Path=$_.FullName;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash;Length=$_.Length}}}
}
function Get-CounterFixtureMarkers {
    @('const string MonitorSid="S-1-5-80-2080860389-2647237053-3465382480-4241646294-1393942697";',
        'new NTAccount("NT SERVICE","HostAgent")','new SecurityIdentifier("S-1-5-32-558")',
        'const string path="SOFTWARE\\HostAgent.InstallerCounterRead";')
}
function Read-CounterFixtureManifest([string]$Path,[string]$Hash,[string]$Id) {
    if($Hash -cnotmatch '^[A-F0-9]{64}$' -or -not $Path){throw 'reviewed_manifest_pin_required'}
    Assert-CounterFixturePlainPath $Path
    if((Get-Item -LiteralPath $Path).Length -gt 4MB -or (Get-FileHash -LiteralPath $Path).Hash -cne $Hash){throw 'reviewed_manifest_mismatch'}
    $manifest=Get-Content -LiteralPath $Path -Raw|ConvertFrom-Json
    if($manifest.Format -ne 1 -or $manifest.RunId -cne $Id){throw 'reviewed_manifest_invalid'}
    $actual=@(Get-CounterFixtureInputs)
    if(@($manifest.Files).Count -ne $actual.Count){throw 'reviewed_input_set_mismatch'}
    foreach($file in $actual){$entry=@($manifest.Files|Where-Object {$_.Path -ceq $file.Path});if($entry.Count -ne 1 -or $entry[0].Sha256 -cne $file.Sha256 -or $entry[0].Length -ne $file.Length){throw 'reviewed_input_pin_mismatch'}}
    $manifest
}
function Invoke-CounterFixtureTool([string]$File,[string[]]$Arguments,[string]$Log) {
    Assert-CounterFixtureResources -DuringRun
    $info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=$File;$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.WorkingDirectory=[IO.Path]::GetDirectoryName($Log)
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    $child=[Diagnostics.Process]::Start($info);$stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync();$watch=[Diagnostics.Stopwatch]::StartNew()
    try{
        while(-not $child.WaitForExit(100)){
            if(($script:FixtureMonitor -and $script:FixtureMonitor.FailureCode) -or $watch.Elapsed.TotalSeconds -ge 90){$child.Kill();$null=$child.WaitForExit(5000);throw 'fixture_build_resource_or_deadline'}
        }
        [IO.File]::WriteAllText($Log+'.stdout.txt',$stdout.GetAwaiter().GetResult());[IO.File]::WriteAllText($Log+'.stderr.txt',$stderr.GetAwaiter().GetResult())
        if($child.ExitCode -ne 0){throw 'fixture_build_tool_failed'}
    }finally{$child.Dispose()}
}
function Assert-CounterFixtureMsiShape([string]$Path,$Scope,[string]$Product,[string]$Upgrade) {
    # Database mode 0 only. Never OpenPackage, DoAction or an execute sequence.
    $installer=New-Object -ComObject WindowsInstaller.Installer
    $db=$installer.OpenDatabase($Path,0)
    function FixtureRows([string]$Sql,[int]$Count){$view=$db.OpenView($Sql);$null=$view.Execute();try{while($null-ne($row=$view.Fetch())){$values=@();for($i=1;$i-le$Count;$i++){$values+=$row.StringData($i)};,$values}}finally{$null=$view.Close()}}
    $properties=@{};foreach($row in @(FixtureRows 'SELECT `Property`,`Value` FROM `Property`' 2)){$properties[$row[0]]=$row[1]}
    if($properties['ProductCode']-ine$Product-or$properties['UpgradeCode']-ine$Upgrade){throw 'fixture_msi_schema_invalid'}
    $services=@(FixtureRows 'SELECT `Name`,`StartName`,`ServiceType`,`StartType` FROM `ServiceInstall`' 4)
    $controls=@(FixtureRows 'SELECT `Name`,`Event` FROM `ServiceControl`' 2)
    if($services.Count-ne1-or$services[0][0]-cne$Scope.Name-or$services[0][1]-cne('NT SERVICE\'+$Scope.Name)-or$services[0][2]-ne'16'-or$services[0][3]-ne'3'-or
        $controls.Count-ne1-or$controls[0][0]-cne$Scope.Name-or$controls[0][1]-ne'128'){throw 'fixture_msi_schema_invalid'}
    $files=@(FixtureRows 'SELECT `File`,`Component_`,`FileName` FROM `File`' 3)
    $components=@(FixtureRows 'SELECT `Component`,`Directory_` FROM `Component`' 2)
    $directories=@(FixtureRows 'SELECT `Directory`,`Directory_Parent`,`DefaultDir` FROM `Directory`' 3)
    $target=@($directories|Where-Object {$_[0]-ceq'FixtureDir'})
    if($files.Count-ne1-or$files[0][0]-cne'FixtureExe'-or$files[0][1]-cne'FixtureComponent'-or$files[0][2].Split('|')[-1]-cne'Inert.exe'-or
        $components.Count-ne1-or$components[0][0]-cne'FixtureComponent'-or$components[0][1]-cne'FixtureDir'-or
        $target.Count-ne1-or$target[0][1]-cne'ProgramFiles64Folder'-or$target[0][2].Split('|')[-1]-cne('HostAgentCounterFixture-'+$Scope.Id)){throw 'fixture_msi_schema_invalid'}
    $expected=@{PrepareCounterRead=@('1','PrepareCounterRead');GrantCounterRead=@('3073','GrantCounterRead');RollbackCounterReadGrant=@('3329','RollbackCounterReadGrant');CommitCounterReadGrant=@('3585','CommitCounterReadGrant');
        ReleaseCounterRead=@('3073','ReleaseCounterRead');RollbackCounterReadRelease=@('3329','RollbackCounterReadRelease');CommitCounterReadRelease=@('3585','CommitCounterReadRelease');
        FailGrant=@('3073','ForceFailure');FailRelease=@('3073','ForceFailure');FailCommit=@('3585','ForceFailure')}
    $actions=@(FixtureRows 'SELECT `Action`,`Type`,`Source`,`Target` FROM `CustomAction`' 4)
    if($actions.Count-ne$expected.Count){throw 'fixture_msi_schema_invalid'}
    foreach($action in $actions){if(-not$expected.ContainsKey($action[0])-or$action[1]-ne$expected[$action[0]][0]-or$action[2]-cne'FixtureBinary'-or$action[3]-cne$expected[$action[0]][1]){throw 'fixture_msi_schema_invalid'}}
    $sequence=@{};foreach($row in @(FixtureRows 'SELECT `Action`,`Sequence` FROM `InstallExecuteSequence`' 2)){$sequence[$row[0]]=[int]$row[1]}
    foreach($chain in @(@('PrepareCounterRead','InstallInitialize'),@('InstallServices','RollbackCounterReadGrant','GrantCounterRead','CommitCounterReadGrant','FailGrant','FailCommit','InstallFinalize'),@('StopServices','RollbackCounterReadRelease','ReleaseCounterRead','CommitCounterReadRelease','FailRelease','DeleteServices'))){
        for($i=1;$i-lt$chain.Count;$i++){if(-not$sequence.ContainsKey($chain[$i-1])-or-not$sequence.ContainsKey($chain[$i])-or$sequence[$chain[$i-1]]-ge$sequence[$chain[$i]]){throw 'fixture_msi_schema_invalid'}}
    }
}
function Build-CounterFixture($Scope,[string]$TargetMember,[string]$TargetGroup,$Manifest) {
    Assert-CounterFixtureAdmin;Assert-CounterFixtureVm;Assert-CounterFixtureResources -DuringRun
    if($TargetMember -cnotmatch '^S-1-5-80-(\d+-){4}\d+$' -or $TargetMember -eq 'S-1-5-80-2080860389-2647237053-3465382480-4241646294-1393942697' -or
        $TargetGroup -cnotmatch '^S-1-5-21-(\d+-){3}\d+$'){throw 'fixture_sid_scope_invalid'}
    Assert-CounterFixturePlainPath $Scope.Root
    $work=Join-Path $Scope.Root 'build';if(Test-Path -LiteralPath $work){throw 'fixture_output_exists_preserve'}
    $null=New-Item -ItemType Directory -Path $work
    $repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'));$tools=Join-Path (Split-Path $repo) 'HostAgent-DEV-tools'
    $nativePath=Join-Path $repo 'tools\installer\WindowsCounterReadState.cs';$original=[IO.File]::ReadAllText($nativePath);$copy=$original
    $markers=@(Get-CounterFixtureMarkers)
    $replacements=@(('const string MonitorSid="'+$TargetMember+'";'),('new NTAccount("NT SERVICE","'+$Scope.Name+'")'),
        ('new SecurityIdentifier("'+$TargetGroup+'")'),('const string path="'+$Scope.Registry.Replace('\','\\')+'";'))
    for($i=0;$i-lt4;$i++){if([regex]::Matches($copy,[regex]::Escape($markers[$i])).Count-ne1){throw 'fixture_native_target_marker_mismatch'};$copy=$copy.Replace($markers[$i],$replacements[$i])}
    $normalized=$copy;for($i=0;$i-lt4;$i++){$normalized=$normalized.Replace($replacements[$i],$markers[$i])}
    if($normalized -cne $original){throw 'fixture_private_source_diff_invalid'}
    $privateNative=Join-Path $work 'WindowsCounterReadState.cs';[IO.File]::WriteAllText($privateNative,$copy)
    $dtf=Join-Path $tools 'dtf-5.0.2\wixtoolset.dtf.windowsinstaller\lib\net20\WixToolset.Dtf.WindowsInstaller.dll'
    $dtfTools=Join-Path $tools 'dtf-5.0.2\wixtoolset.dtf.customaction\tools'
    $csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $managed=Join-Path $work 'FixtureActions.dll';$embedded=Join-Path $work 'FixtureActions.CA.dll'
    Invoke-CounterFixtureTool $csc @('/nologo','/langversion:5','/target:library','/platform:x64',('/out:'+$managed),('/reference:'+$dtf),
        (Join-Path $repo 'tools\installer\CounterReadTransaction.cs'),(Join-Path $repo 'tools\installer\CounterReadActions.cs'),$privateNative,
        (Join-Path $PSScriptRoot 'CounterReadMsiFixtureFailureCA.cs')) (Join-Path $work 'compile-actions')
    Invoke-CounterFixtureTool (Join-Path $dtfTools 'WixToolset.Dtf.MakeSfxCA.exe') @($embedded,(Join-Path $dtfTools 'x64\SfxCA.dll'),$managed,
        (Join-Path $repo 'tools\installer\CustomAction.config'),$dtf) (Join-Path $work 'package-actions')
    $inert=Join-Path $work 'Inert.cs';[IO.File]::WriteAllText($inert,'internal static class Inert { static int Main(){return 73;} }')
    $payload=Join-Path $work 'Inert.exe';Invoke-CounterFixtureTool $csc @('/nologo','/langversion:5','/target:exe','/platform:x64',('/out:'+$payload),$inert) (Join-Path $work 'compile-inert')
    $product=[Guid]::NewGuid().ToString('B');$upgrade=[Guid]::NewGuid().ToString('B');$msi=Join-Path $work 'Fixture.msi'
    $ca=[Security.SecurityElement]::Escape($embedded);$exe=[Security.SecurityElement]::Escape($payload)
    $xml=@"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Package Name="HostAgent GUID counter fixture" Manufacturer="HostAgent DEV fixture" Version="1.0.0" ProductCode="$product" UpgradeCode="$upgrade" Scope="perMachine" InstallerVersion="500">
<MediaTemplate EmbedCab="yes"/><Property Id="FAIL_PHASE" Value="none"/>
<Launch Condition="NOT DISABLEROLLBACK AND NOT RollbackDisabled" Message="Fixture requires rollback."/>
<StandardDirectory Id="ProgramFiles64Folder"><Directory Id="FixtureDir" Name="HostAgentCounterFixture-$($Scope.Id)"><Component Id="FixtureComponent" Guid="*"><File Id="FixtureExe" Source="$exe" KeyPath="yes"/>
<ServiceInstall Id="FixtureService" Name="$($Scope.Name)" DisplayName="$($Scope.Name)" Type="ownProcess" Start="demand" ErrorControl="normal" Account="NT SERVICE\$($Scope.Name)" Vital="yes"/>
<ServiceControl Id="RemoveFixture" Name="$($Scope.Name)" Remove="uninstall" Wait="yes"/></Component></Directory></StandardDirectory><Feature Id="FixtureFeature" Level="1"><ComponentRef Id="FixtureComponent"/></Feature>
<Binary Id="FixtureBinary" SourceFile="$ca"/>
<CustomAction Id="PrepareCounterRead" BinaryRef="FixtureBinary" DllEntry="PrepareCounterRead" Return="check"/>
<CustomAction Id="GrantCounterRead" BinaryRef="FixtureBinary" DllEntry="GrantCounterRead" Execute="deferred" Impersonate="no" Return="check"/>
<CustomAction Id="RollbackCounterReadGrant" BinaryRef="FixtureBinary" DllEntry="RollbackCounterReadGrant" Execute="rollback" Impersonate="no" Return="check"/>
<CustomAction Id="CommitCounterReadGrant" BinaryRef="FixtureBinary" DllEntry="CommitCounterReadGrant" Execute="commit" Impersonate="no" Return="check"/>
<CustomAction Id="ReleaseCounterRead" BinaryRef="FixtureBinary" DllEntry="ReleaseCounterRead" Execute="deferred" Impersonate="no" Return="check"/>
<CustomAction Id="RollbackCounterReadRelease" BinaryRef="FixtureBinary" DllEntry="RollbackCounterReadRelease" Execute="rollback" Impersonate="no" Return="check"/>
<CustomAction Id="CommitCounterReadRelease" BinaryRef="FixtureBinary" DllEntry="CommitCounterReadRelease" Execute="commit" Impersonate="no" Return="check"/>
<CustomAction Id="FailGrant" BinaryRef="FixtureBinary" DllEntry="ForceFailure" Execute="deferred" Impersonate="no" Return="check"/>
<CustomAction Id="FailRelease" BinaryRef="FixtureBinary" DllEntry="ForceFailure" Execute="deferred" Impersonate="no" Return="check"/>
<CustomAction Id="FailCommit" BinaryRef="FixtureBinary" DllEntry="ForceFailure" Execute="commit" Impersonate="no" Return="check"/>
<InstallExecuteSequence>
<Custom Action="PrepareCounterRead" Before="InstallInitialize" Condition="1"/>
<Custom Action="RollbackCounterReadGrant" After="InstallServices" Condition="NOT REMOVE~=&quot;ALL&quot;"/>
<Custom Action="GrantCounterRead" After="RollbackCounterReadGrant" Condition="NOT REMOVE~=&quot;ALL&quot;"/>
<Custom Action="CommitCounterReadGrant" After="GrantCounterRead" Condition="NOT REMOVE~=&quot;ALL&quot;"/>
<Custom Action="FailGrant" After="CommitCounterReadGrant" Condition="NOT REMOVE~=&quot;ALL&quot; AND FAIL_PHASE=&quot;deferred_grant&quot;"/>
<Custom Action="FailCommit" After="FailGrant" Condition="NOT REMOVE~=&quot;ALL&quot; AND FAIL_PHASE=&quot;commit_grant&quot;"/>
<Custom Action="RollbackCounterReadRelease" After="StopServices" Condition="REMOVE~=&quot;ALL&quot;"/>
<Custom Action="ReleaseCounterRead" After="RollbackCounterReadRelease" Condition="REMOVE~=&quot;ALL&quot;"/>
<Custom Action="CommitCounterReadRelease" After="ReleaseCounterRead" Condition="REMOVE~=&quot;ALL&quot;"/>
<Custom Action="FailRelease" After="CommitCounterReadRelease" Condition="REMOVE~=&quot;ALL&quot; AND FAIL_PHASE=&quot;deferred_release&quot;"/>
</InstallExecuteSequence></Package></Wix>
"@
    $wxs=Join-Path $work 'Fixture.wxs';[IO.File]::WriteAllText($wxs,$xml)
    Invoke-CounterFixtureTool (Join-Path $tools 'wix\wix.exe') @('build',$wxs,'-arch','x64','-pdbtype','none','-out',$msi) (Join-Path $work 'wix-build')
    Assert-CounterFixtureMsiShape $msi $Scope $product $upgrade
    $msiHash=(Get-FileHash -LiteralPath $msi).Hash
    $targetSource=Join-Path $work 'FixtureTargets.cs'
    [IO.File]::WriteAllText($targetSource,('internal static class FixtureTargets { public const string RunId="'+$Scope.Id+'", GroupName="'+$Scope.Name+'", GroupSid="'+$TargetGroup+'", MemberSid="'+$TargetMember+'", RegistryRoot=@"'+$Scope.Registry+'", MsiPath=@"'+$msi+'", MsiSha256="'+$msiHash+'"; }'))
    $controller=Join-Path $work 'FixtureController.exe'
    Invoke-CounterFixtureTool $csc @('/nologo','/langversion:5','/target:exe','/platform:x64',('/out:'+$controller),(Join-Path $PSScriptRoot 'CounterReadMsiFixtureController.cs'),$targetSource) (Join-Path $work 'compile-controller')
    $artifacts=@(Get-ChildItem -LiteralPath $work -File|ForEach-Object {[pscustomobject]@{Path=$_.FullName;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash;Length=$_.Length}})
    $receipt=[pscustomobject]@{Scope=$Scope;Msi=$msi;MsiSha256=$msiHash;Controller=$controller;ControllerSha256=(Get-FileHash -LiteralPath $controller).Hash;
        ProductCode=$product;UpgradeCode=$upgrade;MemberSid=$TargetMember;GroupSid=$TargetGroup;PrivateNativeSha256=(Get-FileHash -LiteralPath $privateNative).Hash;ExactSubstitutionCount=4;MsiSchemaValidated=$true;Artifacts=$artifacts}
    $receipt|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $Scope.Root 'build-receipt.json')
    $receipt
}

# Dot-sourcing defines helpers only. The operator entry owns all execution.
if($MyInvocation.InvocationName -eq '.'){return}
try{
    if($PreparationExecute -or $PreparationBuild){Assert-CounterFixtureAdmin}
    $scope=Get-CounterFixtureScope $PreparationRunId $PreparationRoot
    Assert-CounterFixtureVm;Assert-CounterFixtureResources
    if($PreparationBuild -or $PreparationExecute){throw 'fixture_build_requires_guarded_operator_entry'}
    $files=@(Get-CounterFixtureInputs)
    [pscustomobject]@{scenario='counter_read_fixture_review_manifest_template';status='READ_ONLY_PLAN';changesPerformed=$false;nativeMatrixRun=$false;
        manifestTemplate=[pscustomobject]@{Format=1;RunId=$PreparationRunId;Files=$files;InstalledFiles=@()};
        requiredInstalledRelativePaths=@('Service/HostAgent.Service.exe','Service/HostAgent.Service.dll','Service/HostAgent.Core.dll','Service/HostAgent.Infrastructure.dll','Helper/HostAgent.Helper.exe','Helper/HostAgent.Helper.dll');
        note='Owner must populate six expected installed hashes from the reviewed installed release and pin the completed external manifest before elevation.'}|ConvertTo-Json -Depth 6
}catch{
    $known=@('fixture_run_id_invalid','fixture_root_invalid','fixture_reparse_path_rejected','elevated_operator_required','approved_dev_vm_required','powershell7_x64_required','initial_ram_not_above_768_mib','hard_memory_floor_512','initial_disk_below_10_gib','fixture_build_requires_guarded_operator_entry')
    $reason=if($_.Exception.Message -in $known){$_.Exception.Message}else{'fixture_preparation_dependency_unavailable'}
    [pscustomobject]@{status='NOTRUN';reason=$reason;changesPerformed=$false;nativeMatrixRun=$false;planPrepared=$false}|ConvertTo-Json;exit 1
}
