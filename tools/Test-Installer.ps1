param([Parameter(Mandatory)][string]$PackagePath,[switch]$DevVm)
$ErrorActionPreference='Stop'
$installer=New-Object -ComObject WindowsInstaller.Installer
$db=$installer.OpenDatabase((Join-Path ([IO.Path]::GetFullPath($PackagePath)) 'HostAgent.msi'),0)
function Rows([string]$sql,[int]$columns) {
    $view=$db.OpenView($sql);$null=$view.Execute()
    try {
        while ($null -ne ($row=$view.Fetch())) {
            $values=@();for ($i=1;$i -le $columns;$i++) { $values+= $row.StringData($i) }
            ,$values
        }
    } finally { $null=$view.Close() }
}
$services=@(Rows 'SELECT `Name`,`StartName`,`ServiceType`,`StartType` FROM `ServiceInstall`' 4)
if ($services.Count -ne 2) { throw 'release_installer_services' }
$monitor=@($services | Where-Object { $_[0] -eq 'HostAgent' })
$helper=@($services | Where-Object { $_[0] -eq 'HostAgent.Helper' })
if ($monitor.Count -ne 1 -or $helper.Count -ne 1 -or $monitor[0][1] -ne 'NT SERVICE\HostAgent' -or $helper[0][1] -ne 'LocalSystem') { throw 'release_installer_identity' }
foreach ($service in $services) { if ($service[2] -ne '16' -or $service[3] -ne '2') { throw 'release_installer_start' } }
$controls=@(Rows 'SELECT `Name`,`Event` FROM `ServiceControl`' 2)
if ($controls.Count -ne 2 -or @($controls | Where-Object { ([int]$_[1] -band 163) -ne 163 }).Count -ne 0) { throw 'release_installer_lifecycle' }
$bootstrap=@(Rows "SELECT ``Type``,``Target`` FROM ``CustomAction`` WHERE ``Action``='Bootstrap'" 2)
$preflight=@(Rows "SELECT ``Type``,``Target`` FROM ``CustomAction`` WHERE ``Action``='ValidateInstallTree'" 2)
$sequence=@(Rows 'SELECT `Action`,`Condition`,`Sequence` FROM `InstallExecuteSequence`' 3)
$upgradeSequence=@{}
foreach ($action in @('InstallInitialize','RemoveExistingProducts','ProcessComponents','InstallFiles','InstallFinalize')) {
    $rows=@($sequence | Where-Object { $_[0] -eq $action })
    if ($rows.Count -ne 1) { throw 'release_installer_upgrade_transaction' }
    $upgradeSequence[$action]=[int]$rows[0][2]
    if ($upgradeSequence[$action] -le 0) { throw 'release_installer_upgrade_transaction' }
}
if ($upgradeSequence['InstallInitialize'] -ge $upgradeSequence['RemoveExistingProducts'] -or
    $upgradeSequence['RemoveExistingProducts'] -ge $upgradeSequence['ProcessComponents'] -or
    $upgradeSequence['RemoveExistingProducts'] -ge $upgradeSequence['InstallFiles'] -or
    $upgradeSequence['RemoveExistingProducts'] -ge $upgradeSequence['InstallFinalize']) { throw 'release_installer_upgrade_transaction' }
$preflightSequence=@($sequence | Where-Object { $_[0] -eq 'ValidateInstallTree' })
$firstWrite=@($sequence | Where-Object { $_[0] -in @('CreateFolders','InstallFiles') } | ForEach-Object { [int]$_[2] } | Sort-Object)[0]
if ($preflight.Count -ne 1 -or [int]$preflight[0][0] -ne 3073 -or $preflight[0][1] -ne 'ValidateInstallTree' -or
    $preflightSequence.Count -ne 1 -or [int]$preflightSequence[0][2] -ge $firstWrite -or $preflightSequence[0][1] -ne 'NOT REMOVE~="ALL"') { throw 'release_installer_preflight' }
if ($bootstrap.Count -ne 1 -or ([int]$bootstrap[0][0] -band 3072) -ne 3072 -or $bootstrap[0][1] -ne '--installer-bootstrap') { throw 'release_installer_bootstrap' }
$recovery=@(Rows 'SELECT `ServiceName`,`FirstFailureActionType`,`SecondFailureActionType`,`ThirdFailureActionType` FROM `Wix4ServiceConfig`' 4)
if ($recovery.Count -ne 2 -or @($recovery | Where-Object { $_[1] -ne 'restart' -or $_[2] -ne 'restart' -or $_[3] -ne 'none' }).Count -ne 0) { throw 'release_installer_recovery' }
$dirs=@(Rows 'SELECT `Directory` FROM `Directory`' 1)
if (@($dirs | Where-Object { $_[0] -match 'AppData|ProgramData' }).Count -ne 0) { throw 'release_installer_data_removal' }
$files=@(Rows 'SELECT `File` FROM `File`' 1)
$directoryRows=@(Rows 'SELECT `Directory`,`Directory_Parent`,`DefaultDir` FROM `Directory`' 3)
$directoryMap=@{};foreach($row in $directoryRows){$directoryMap[$row[0]]=$row}
function RelativeDirectory([string]$id) {
    if($id -eq 'ProductDirectory'){return ''}
    if(-not $directoryMap.ContainsKey($id)){throw 'release_installer_directory'}
    $row=$directoryMap[$id];$name=($row[2].Split('|') | Select-Object -Last 1)
    $parent=RelativeDirectory $row[1]
    if($parent){return $parent+'/'+$name};return $name
}
$componentRows=@(Rows 'SELECT `Component`,`Directory_` FROM `Component`' 2)
$componentMap=@{};foreach($row in $componentRows){$componentMap[$row[0]]=$row[1]}
$installedPaths=@(foreach($row in @(Rows 'SELECT `Component_`,`FileName` FROM `File`' 2)) {
    $directory=RelativeDirectory $componentMap[$row[0]]
    $name=($row[1].Split('|') | Select-Object -Last 1)
    if($directory){$directory+'/'+$name}else{$name}
})
foreach($required in @('LICENSE','docs/operations/privacy.md','docs/legal/dotnet-runtime-10.0.12-LICENSE.txt','docs/legal/dotnet-runtime-10.0.12-THIRD-PARTY-NOTICES.txt')) {
    if($required -notin $installedPaths){throw 'release_missing_notices'}
}
$conditions=@(Rows 'SELECT `Condition` FROM `LaunchCondition`' 1)
$counterActions=@{
    PrepareCounterRead=1
    GrantCounterRead=3073
    RollbackCounterReadGrant=3329
    CommitCounterReadGrant=3585
    ReleaseCounterRead=3073
    RollbackCounterReadRelease=3329
    CommitCounterReadRelease=3585
}
$counterSequence=@{}
foreach($name in $counterActions.Keys) {
    $actions=@(Rows ("SELECT ``Type``,``Source``,``Target`` FROM ``CustomAction`` WHERE ``Action``='"+$name+"'") 3)
    $positions=@($sequence | Where-Object {$_[0] -ceq $name})
    $condition=if($name -eq 'PrepareCounterRead'){'1'}elseif($name -match 'Release'){'REMOVE~="ALL" AND NOT UPGRADINGPRODUCTCODE'}else{'NOT REMOVE~="ALL"'}
    if($actions.Count -ne 1 -or [int]$actions[0][0] -ne $counterActions[$name] -or
        $actions[0][1] -cne 'PreflightBinary' -or $actions[0][2] -cne $name -or
        $positions.Count -ne 1 -or $positions[0][1] -cne $condition -or [int]$positions[0][2] -le 0) {
        throw 'release_installer_counter_read'
    }
    $counterSequence[$name]=[int]$positions[0][2]
}
foreach($name in @('StopServices','DeleteServices','InstallServices','StartServices','Bootstrap','InstallInitialize')) {
    $positions=@($sequence|Where-Object {$_[0] -ceq $name})
    if($positions.Count -ne 1){throw 'release_installer_counter_read'}
    $counterSequence[$name]=[int]$positions[0][2]
}
foreach($chain in @(
    @('PrepareCounterRead','InstallInitialize'),
    @('StopServices','RollbackCounterReadRelease','ReleaseCounterRead','CommitCounterReadRelease','DeleteServices'),
    @('InstallServices','Bootstrap','RollbackCounterReadGrant','GrantCounterRead','CommitCounterReadGrant','StartServices')
)) {
    for($i=1;$i -lt $chain.Count;$i++) {
        if($counterSequence[$chain[$i-1]] -ge $counterSequence[$chain[$i]]){throw 'release_installer_counter_read'}
    }
}
if(@($conditions|Where-Object {$_[0] -ceq 'NOT DISABLEROLLBACK AND NOT RollbackDisabled'}).Count -ne 1) {
    throw 'release_installer_counter_read'
}
$guarded=@($conditions | Where-Object { $_[0] -eq 'Installed OR WIX_UPGRADE_DETECTED OR (NOT EXISTING_MONITOR AND NOT EXISTING_HELPER)' }).Count -eq 1
if (-not $guarded) { throw 'release_installer_existing_services_guard' }
$devGuard=@($conditions | Where-Object { $_[0] -like '*DEV_MACHINE_GUID*' }).Count -eq 1
if ($DevVm -and -not $devGuard) { throw 'release_installer_dev_vm_guard' }
$devMatches=$false
if($devGuard){
    # Read-only MSI session: evaluate the actual condition with Windows' own
    # ComputerName property and the local identity value; no install actions.
    $session=$installer.OpenPackage((Join-Path ([IO.Path]::GetFullPath($PackagePath)) 'HostAgent.msi'),1)
    $session.Property('DEV_MACHINE_GUID')=(Get-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography' -Name MachineGuid).MachineGuid
    $condition=@($conditions | Where-Object { $_[0] -like '*DEV_MACHINE_GUID*' })[0][0]
    $devMatches=$session.EvaluateCondition($condition) -eq 1
}
if ($DevVm -and -not $devMatches) { throw 'release_installer_dev_vm_identity' }
$shortcuts=@(Rows 'SELECT `Name`,`Directory_` FROM `Shortcut`' 2)
$desktopShortcuts=@($shortcuts | Where-Object { $_[0].Split('|')[-1] -eq 'Host Agent' -and $_[1] -eq 'ProgramMenuFolder' })
if($installedPaths -contains 'Desktop/HostAgent.Desktop.exe' -and $desktopShortcuts.Count -ne 1){throw 'release_installer_desktop_shortcut'}
@{valid=$true;monitorAccount=$monitor[0][1];helperAccount=$helper[0][1];serviceCount=2;bootstrapIsSystemDeferred=$true;precopyValidationIsSystemDeferred=$true;counterReadTransactionGuarded=$true;uninstallPreservesData=$true;recoveryServiceCount=$recovery.Count;fileCount=$files.Count;installedPaths=$installedPaths;existingServicesGuarded=$guarded;devVmGuard=$devGuard;devVmIdentityMatches=$devMatches;desktopShortcutCount=$desktopShortcuts.Count} | ConvertTo-Json -Compress
