$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$chaos=Join-Path $repo 'scripts\chaos'
$tokens=$null;$errors=$null
$main=[Management.Automation.Language.Parser]::ParseFile((Join-Path $chaos 'run-complete-dev-resilience.ps1'),[ref]$tokens,[ref]$errors)
if(@($errors).Count){throw 'bootstrap_source_parse_failed'}
$imports=@($main.FindAll({param($node)
    $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Import-Module'
},$true))
if($imports.Count -lt 4){throw 'bootstrap_import_sequence_missing'}
# Defect: a nested -Force reload removes native argument encoding from the
# actual main-entry import sequence, before any child process is started.
$TestChaosRoot=$chaos
foreach($command in $imports){
    if(-not $command.Extent.Text.StartsWith('Import-Module (Join-Path $PSScriptRoot ')){throw 'bootstrap_import_contract_changed'}
    . ([scriptblock]::Create($command.Extent.Text.Replace('$PSScriptRoot','$TestChaosRoot')))
}
Add-Type -Path (Join-Path $chaos 'BoundedScmPressure.cs')
$root=Join-Path $env:LOCALAPPDATA ('HostAgent-DEV-evidence\bootstrap-regression-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root|Out-Null
$guard={
    if([BoundedScmPressure]::FreeMiB() -lt 768){throw 'bootstrap_memory_abort'}
    if(([IO.DriveInfo]::new('C:\')).AvailableFreeSpace -lt 20GB){throw 'bootstrap_disk_abort'}
}
$env:DOTNET_ROOT='C:\Program Files\dotnet'
$env:DOTNET_ROOT_X64=$env:DOTNET_ROOT
$candidate=Join-Path (Split-Path $repo -Parent) 'HostAgent-DEV-SCM-candidates\scm-release-candidate-20261017-complete-r5'
$observed=$null;$failureType=$null;$failureSymbol=$null
try {
    # Program.cs returns 2 immediately for "noop": no services, IPC, database,
    # collection or remediation. Still exercise the published .NET apphost,
    # native argument encoder, job assignment and real process/output handling.
    $observed=Invoke-CompleteBoundedProcess (Join-Path $candidate 'HostAgent.PipeProbe.exe') @('noop') 15 $root $guard
}catch{$failureType=$_.Exception.GetType().FullName;$failureSymbol=$_.CategoryInfo.TargetName}
if(-not $observed -or $observed.exitCode -ne 2 -or
   -not (Test-Path -LiteralPath (Join-Path $root 'child.json')) -or
   -not (Test-Path -LiteralPath (Join-Path $root 'stdout.txt')) -or
   -not (Test-Path -LiteralPath (Join-Path $root 'stderr.txt'))){
    throw ('bootstrap_native_child_expected_exit_2_actual_failure_type='+$failureType+'; symbol='+$failureSymbol+'; evidence='+$root)
}
$missing=@('ConvertTo-IsolatedScmNativeCommandLine','Get-IsolatedScmCreateArguments','Get-CompleteFailureDiagnostic')|Where-Object {-not(Get-Command $_ -ErrorAction SilentlyContinue)}
if(@($missing).Count){throw 'bootstrap_lost_native_dependency'}
[pscustomobject]@{scenario='actual_campaign_module_imports_and_bounded_published_noop';ok=$true;childExit=$observed.exitCode;servicesControlled=0;databaseOpened=$false;logDirectory=$root}|ConvertTo-Json -Compress
