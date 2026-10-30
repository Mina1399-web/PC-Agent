$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if($PSVersionTable.PSVersion.Major -lt 7){throw 'installer_source_tests_require_pwsh7'}
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$generator=Join-Path $repository 'tools/installer/New-InstallerSource.ps1'
$legacyBuilder=Join-Path $repository 'tools/Build-Installer.ps1'
$fixtureParent=Join-Path ([IO.Path]::GetTempPath()) ('HostAgent-InstallerSource-'+[guid]::NewGuid().ToString('N'))
$fixture=Join-Path $fixtureParent 'package'
$action=Join-Path $fixtureParent 'Preflight.CA.dll'
$devGuid='24381333-0909-4444-a555-901922223333'
$passed=0

function Assert-Source([bool]$Condition,[string]$Code){if(-not $Condition){throw ('installer_source_assertion:'+ $Code)}}
function Get-Source([string]$Root,[string]$Version,[string]$Action,[string]$Channel,[string]$Guid=''){
    if(Test-Path -LiteralPath $generator -PathType Leaf){
        . $generator
        return New-HostAgentInstallerSource -PackageRoot $Root -Version $Version -EmbeddedActionPath $Action -Channel $Channel -DevMachineGuid $Guid
    }
    # Red-phase compatibility adapter: execute only the repository's existing
    # deterministic XML template against disposable files. Never execute the
    # builder's compilation, OS checks, signing, installer, or output steps.
    $legacy=[IO.File]::ReadAllText($legacyBuilder)
    $start=$legacy.IndexOf('function Escape([string]$value)')
    $end=$legacy.IndexOf("`$source=Join-Path `$work 'Product.wxs'")
    if($start -lt 0 -or $end -le $start){throw 'installer_source_legacy_adapter_unavailable'}
    $template=$legacy.Substring($start,$end-$start)
    return & {
        param($Template,$Root,$Version,$Action,$Channel,$Guid)
        $root=$Root;$embedded=$Action;$DevVm=$Channel -ceq 'DevVm'
        function Get-ItemProperty {param([string]$LiteralPath,[string]$Name) [pscustomobject]@{MachineGuid=$Guid}}
        . ([scriptblock]::Create($Template))
        return $xml
    } $template $Root $Version $Action $Channel $Guid
}
function Read-Source([string]$Channel){
    [xml]$xml=Get-Source $fixture '1.2.8' $action $Channel $devGuid
    $ns=[Xml.XmlNamespaceManager]::new($xml.NameTable)
    $ns.AddNamespace('w','http://wixtoolset.org/schemas/v4/wxs')
    return @{Xml=$xml;Ns=$ns;Conditions=@($xml.SelectNodes('//w:Launch',$ns)|ForEach-Object {$_.GetAttribute('Condition')})}
}
function Assert-Common($Source){
    $xml=$Source.Xml;$ns=$Source.Ns
    $services=@($xml.SelectNodes('//w:ServiceInstall',$ns))
    Assert-Source ($services.Count -eq 2) 'two_product_services'
    Assert-Source (@($services|Where-Object {$_.GetAttribute('Name') -ceq 'HostAgent' -and $_.Account -ceq 'NT SERVICE\HostAgent'}).Count -eq 1) 'monitor_identity'
    Assert-Source (@($services|Where-Object {$_.GetAttribute('Name') -ceq 'HostAgent.Helper' -and $_.Account -ceq 'LocalSystem'}).Count -eq 1) 'helper_identity'
    foreach($service in $services){
        Assert-Source ($service.Start -ceq 'auto' -and $service.Type -ceq 'ownProcess' -and $service.ErrorControl -ceq 'normal') 'service_lifecycle'
    }
    $package=$xml.SelectSingleNode('/w:Wix/w:Package',$ns)
    Assert-Source ($package.Scope -ceq 'perMachine' -and $package.Version -ceq '1.2.8' -and $package.GetAttribute('Name') -ceq 'Host Agent V1' -and $package.UpgradeCode -ceq 'E839C131-FD8D-45AA-927E-A2364BD39AE2') 'package_identity'
    Assert-Source ($xml.SelectSingleNode('//w:MajorUpgrade',$ns).Schedule -ceq 'afterInstallInitialize') 'transactional_upgrade'
    $actions=@($xml.SelectNodes('//w:CustomAction',$ns)|ForEach-Object {$_.Id})
    Assert-Source ($actions.Count -eq 9) 'only_declared_actions'
    foreach($name in @('ValidateInstallTree','Bootstrap','PrepareCounterRead','GrantCounterRead','RollbackCounterReadGrant','CommitCounterReadGrant','ReleaseCounterRead','RollbackCounterReadRelease','CommitCounterReadRelease')){
        Assert-Source ($name -cin $actions) ('required_action_'+$name)
    }
    $sequence=@($xml.SelectNodes('//w:InstallExecuteSequence/w:Custom',$ns))
    Assert-Source ($sequence.Count -eq 9) 'only_declared_sequence'
    foreach($pair in @(@('PrepareCounterRead','Before','InstallInitialize','1'),@('ValidateInstallTree','Before','CreateFolders','NOT REMOVE~="ALL"'),@('Bootstrap','After','InstallServices','NOT REMOVE~="ALL"'),@('RollbackCounterReadGrant','After','Bootstrap','NOT REMOVE~="ALL"'),@('GrantCounterRead','After','RollbackCounterReadGrant','NOT REMOVE~="ALL"'),@('CommitCounterReadGrant','After','GrantCounterRead','NOT REMOVE~="ALL"'),@('RollbackCounterReadRelease','After','StopServices','REMOVE~="ALL" AND NOT UPGRADINGPRODUCTCODE'),@('ReleaseCounterRead','After','RollbackCounterReadRelease','REMOVE~="ALL" AND NOT UPGRADINGPRODUCTCODE'),@('CommitCounterReadRelease','After','ReleaseCounterRead','REMOVE~="ALL" AND NOT UPGRADINGPRODUCTCODE'))){
        $entry=@($sequence|Where-Object {$_.Action -ceq $pair[0]})
        Assert-Source ($entry.Count -eq 1 -and $entry[0].GetAttribute($pair[1]) -ceq $pair[2] -and $entry[0].Condition -ceq $pair[3]) ('sequence_'+$pair[0])
    }
    foreach($name in @('ValidateInstallTree','GrantCounterRead','ReleaseCounterRead')){
        $node=$xml.SelectSingleNode(('//w:CustomAction[@Id="'+$name+'"]'),$ns)
        Assert-Source ($node.BinaryRef -ceq 'PreflightBinary' -and $node.Execute -ceq 'deferred' -and $node.Impersonate -ceq 'no' -and $node.Return -ceq 'check') ('system_deferred_'+$name)
    }
    foreach($name in @('RollbackCounterReadGrant','RollbackCounterReadRelease','CommitCounterReadGrant','CommitCounterReadRelease')){
        $node=$xml.SelectSingleNode(('//w:CustomAction[@Id="'+$name+'"]'),$ns)
        $expected=if($name.StartsWith('Rollback')){'rollback'}else{'commit'}
        Assert-Source ($node.BinaryRef -ceq 'PreflightBinary' -and $node.Execute -ceq $expected -and $node.Impersonate -ceq 'no' -and $node.Return -ceq 'check') ('transaction_action_'+$name)
    }
    Assert-Source ('NOT DISABLEROLLBACK AND NOT RollbackDisabled' -cin $Source.Conditions) 'rollback_required'
    Assert-Source ('Installed OR WIX_UPGRADE_DETECTED OR (NOT EXISTING_MONITOR AND NOT EXISTING_HELPER)' -cin $Source.Conditions) 'existing_service_guard'
    $paths=@($xml.SelectNodes('//w:File',$ns)|ForEach-Object {$_.Source})
    Assert-Source ((Join-Path $fixture 'docs/operations/privacy.md') -cin $paths) 'privacy_bundled'
}
function Expect-Failure([scriptblock]$Check,[string]$Code){
    $caught=$false
    try{& $Check|Out-Null}catch{if($_.Exception.Message -ceq $Code){$caught=$true}else{throw}}
    Assert-Source $caught ('expected_failure_'+$Code)
}
function Pass([string]$Name){$script:passed++;Write-Output ('PASS '+$Name)}

try{
    $null=[IO.Directory]::CreateDirectory($fixture)
    foreach($relative in @('Service/HostAgent.Service.exe','Helper/HostAgent.Helper.exe','Cli/HostAgent.Cli.exe','Desktop/HostAgent.Desktop.exe','LICENSE','docs/operations/privacy.md','docs/legal/dotnet-runtime-10.0.12-LICENSE.txt','docs/legal/dotnet-runtime-10.0.12-THIRD-PARTY-NOTICES.txt')){
        $path=Join-Path $fixture $relative
        $null=[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
        [IO.File]::WriteAllText($path,'synthetic fixture')
    }
    [IO.File]::WriteAllText($action,'synthetic custom action')

    $dev=Read-Source 'DevVm';Assert-Common $dev
    Assert-Source (@($dev.Conditions|Where-Object {$_ -ceq ('ComputerName~="AgentPC" AND DEV_MACHINE_GUID="'+$devGuid+'"')}).Count -eq 1) 'exact_dev_machine_guard'
    Assert-Source (@($dev.Conditions|Where-Object {$_ -like '*HOSTAGENT_INSTALL_ACK*'}).Count -eq 0) 'dev_semantics_preserved'
    Pass 'DevVm exact machine guard and existing MSI semantics'

    $signed=Read-Source 'SignedRelease';Assert-Common $signed
    Assert-Source (@($signed.Conditions|Where-Object {$_ -like '*DEV_MACHINE_GUID*' -or $_ -like '*HOSTAGENT_INSTALL_ACK*'}).Count -eq 0) 'signed_release_semantics_preserved'
    Pass 'SignedRelease existing MSI semantics'

    $preview=Read-Source 'SubmissionPreview';Assert-Common $preview
    Assert-Source ('REMOVE~="ALL" OR HOSTAGENT_INSTALL_ACK="YES"' -cin $preview.Conditions) 'preview_requires_explicit_ack'
    Assert-Source (@($preview.Conditions|Where-Object {$_ -like '*DEV_MACHINE_GUID*'}).Count -eq 0) 'preview_no_dev_identity'
    $ack=@($preview.Xml.SelectNodes('//w:Property[@Id="HOSTAGENT_INSTALL_ACK"]',$preview.Ns))
    Assert-Source ($ack.Count -eq 1 -and $ack[0].Secure -ceq 'yes' -and -not $ack[0].HasAttribute('Value')) 'preview_no_implicit_ack'
    $disclosure=@($preview.Xml.SelectNodes('//w:Launch[contains(@Condition,"HOSTAGENT_INSTALL_ACK")]',$preview.Ns))[0].Message
    foreach($term in @('NT SERVICE\HostAgent','LocalSystem','Performance Monitor Users','ProgramData','Shadow','off','privacy.md')){
        Assert-Source ($disclosure.Contains($term,[StringComparison]::Ordinal)) ('disclosure_'+$term)
    }
    Pass 'SubmissionPreview explicit acknowledgement and bounded disclosure'

    Expect-Failure {Get-Source $fixture '1.2' $action 'SignedRelease'} 'release_version_invalid'
    Expect-Failure {Get-Source 'relative/package' '1.2.8' $action 'SignedRelease'} 'release_path_invalid'
    Expect-Failure {Get-Source $fixture '1.2.8' 'relative/action.dll' 'SignedRelease'} 'release_path_invalid'
    Expect-Failure {Get-Source $fixture '1.2.8' $action 'DevVm' 'invalid'} 'release_dev_vm_required'
    Pass 'Invalid version, paths and DEV identity rejected'

    $notice=Join-Path $fixture 'docs/legal/dotnet-runtime-10.0.12-LICENSE.txt'
    [IO.File]::Move($notice,($notice+'.saved'))
    Expect-Failure {Get-Source $fixture '1.2.8' $action 'SignedRelease'} 'release_missing_notices'
    [IO.File]::Move(($notice+'.saved'),$notice)
    Pass 'Missing required notice rejected'

    $junction=Join-Path $fixtureParent 'package-junction'
    $null=New-Item -ItemType Junction -Path $junction -Target $fixture
    Expect-Failure {Get-Source $junction '1.2.8' $action 'SignedRelease'} 'release_reparse_point'
    Remove-Item -LiteralPath $junction -Force
    Pass 'Reparse package path rejected without following it'

    $builder=[IO.File]::ReadAllText($legacyBuilder)
    $vmGuard=$builder.IndexOf("if (`$env:COMPUTERNAME -ne 'AgentPC'")
    $compile=$builder.IndexOf('Build-HostAgentInstallerAction')
    Assert-Source ($vmGuard -ge 0 -and $compile -gt $vmGuard -and $builder.Contains('0bb543d1-7bb0-43ed-9c94-596d63ef3310') -and $builder.Contains("(Get-Service VBoxService).Status -ne 'Running'")) 'build_installer_preserves_vm_guard_before_compile'
    Assert-Source ($builder.IndexOf('publisher_signature_required') -lt $compile) 'build_installer_preserves_certificate_guard'
    Pass 'Existing builder retains VM/certificate guards before action compilation'

    # These invalid flag combinations must stop before creating even a
    # read-only Windows Installer COM object. No MSI file is supplied.
    $inspector=Join-Path $repository 'tools/Test-Installer.ps1'
    Expect-Failure {& $inspector -PackagePath $fixture -DevVm -ArtifactOnly} 'release_installer_artifact_only_dev_identity'
    Expect-Failure {& $inspector -PackagePath $fixture -SubmissionPreview} 'release_installer_submission_requires_artifact_only'
    Pass 'Artifact-only inspector rejects incompatible modes before COM'

    $tokens=$null;$errors=$null
    $inspectorAst=[Management.Automation.Language.Parser]::ParseFile($inspector,[ref]$tokens,[ref]$errors)
    $tableValidator=$inspectorAst.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Assert-HostAgentSubmissionPreviewTables'},$false)
    Assert-Source ($null -ne $tableValidator) 'pure_preview_table_validator_available'
    # Load only this pure trusted function declaration, never the COM reader.
    . ([scriptblock]::Create($tableValidator.Extent.Text))
    $tableConditions=@($preview.Xml.SelectNodes('//w:Launch',$preview.Ns)|ForEach-Object {,@($_.Condition,$_.Message)})
    $tableProperties=@(,@('SecureCustomProperties','HOSTAGENT_INSTALL_ACK'))
    Assert-HostAgentSubmissionPreviewTables $tableConditions $tableProperties
    $withDevGuards=$tableConditions+ ,@('DEV_MACHINE_GUID="one"','guard one')+ ,@('DEV_MACHINE_GUID="two"','guard two')
    Expect-Failure {Assert-HostAgentSubmissionPreviewTables $withDevGuards $tableProperties} 'release_installer_submission_identity'
    Expect-Failure {Assert-HostAgentSubmissionPreviewTables @() $tableProperties} 'release_installer_submission_acknowledgement'
    $badDisclosure=@(,@('REMOVE~="ALL" OR HOSTAGENT_INSTALL_ACK="YES"','incomplete'))
    Expect-Failure {Assert-HostAgentSubmissionPreviewTables $badDisclosure $tableProperties} 'release_installer_submission_disclosure'
    $implicitAck=$tableProperties+ ,@('HOSTAGENT_INSTALL_ACK','YES')
    Expect-Failure {Assert-HostAgentSubmissionPreviewTables $tableConditions $implicitAck} 'release_installer_submission_acknowledgement'
    $notSecure=@(,@('SecureCustomProperties','OTHER_PROPERTY'))
    Expect-Failure {Assert-HostAgentSubmissionPreviewTables $tableConditions $notSecure} 'release_installer_submission_acknowledgement'
    Pass 'Preview table validation rejects DEV guards, missing acknowledgement, incomplete disclosure and implicit consent'

    . (Join-Path $repository 'tools/installer/Build-InstallerAction.ps1')
    $originalActionHash=(Get-FileHash -LiteralPath $action -Algorithm SHA256).Hash
    Expect-Failure {Build-HostAgentInstallerAction -WorkDirectory $fixtureParent -DtfRoot $fixtureParent -Version '1.2'} 'release_version_invalid'
    Expect-Failure {Build-HostAgentInstallerAction -WorkDirectory 'relative/work' -DtfRoot $fixtureParent -Version '1.2.8'} 'release_path_invalid'
    Expect-Failure {Build-HostAgentInstallerAction -WorkDirectory $fixtureParent -DtfRoot $fixtureParent -Version '1.2.8'} 'release_output_exists'
    Assert-Source ((Get-FileHash -LiteralPath $action -Algorithm SHA256).Hash -ceq $originalActionHash -and -not (Test-Path -LiteralPath (Join-Path $fixtureParent 'AssemblyInfo.cs'))) 'action_helper_did_not_overwrite_or_compile'
    Pass 'Action helper rejects unsafe input/output before compilation'

    foreach($relative in @('tools/installer/New-InstallerSource.ps1','tools/installer/Build-InstallerAction.ps1','tools/Build-Installer.ps1','tools/Test-Installer.ps1')){
        $tokens=$null;$errors=$null
        $null=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repository $relative),[ref]$tokens,[ref]$errors)
        Assert-Source ($errors.Count -eq 0) ('parser_'+$relative)
    }
    Pass 'Installer source and action helper parse'
    Write-Output ('installer_source_tests=PASS count='+$passed)
}catch{
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}finally{
    if(Test-Path -LiteralPath $fixtureParent){
        $resolved=[IO.Path]::GetFullPath($fixtureParent)
        $temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')
        if([IO.Path]::GetDirectoryName($resolved) -ceq $temp -and [IO.Path]::GetFileName($resolved) -cmatch '^HostAgent-InstallerSource-[a-f0-9]{32}$'){
            $junction=Join-Path $resolved 'package-junction'
            if(Test-Path -LiteralPath $junction){Remove-Item -LiteralPath $junction -Force}
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
