param([Parameter(Mandatory)][string]$PackagePath)
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
$conditions=@(Rows 'SELECT `Condition` FROM `LaunchCondition`' 1)
$guarded=@($conditions | Where-Object { $_[0] -eq 'Installed OR WIX_UPGRADE_DETECTED OR (NOT EXISTING_MONITOR AND NOT EXISTING_HELPER)' }).Count -eq 1
$devGuard=@($conditions | Where-Object { $_[0] -like '*DEV_MACHINE_GUID*' }).Count -eq 1
@{valid=$true;monitorAccount=$monitor[0][1];helperAccount=$helper[0][1];serviceCount=2;bootstrapIsSystemDeferred=$true;precopyValidationIsSystemDeferred=$true;uninstallPreservesData=$true;recoveryServiceCount=$recovery.Count;fileCount=$files.Count;existingServicesGuarded=$guarded;devVmGuard=$devGuard} | ConvertTo-Json -Compress
