$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$module=Import-Module (Join-Path $repo 'scripts/chaos/CompleteHelperTargets.psm1') -Force -PassThru
$root=Join-Path $env:LOCALAPPDATA ('HostAgent-DEV-evidence\copy-output-regression-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root|Out-Null
$source=Join-Path $root 'source.fixture';$destination=Join-Path $root 'copy.fixture'
[IO.File]::WriteAllBytes($source,[byte[]](0..255))
$result=@(& $module {param($source,$destination) Copy-CompleteHelperFile $source $destination {}} $source $destination)
if($result.Count-ne0){
 $types=@($result|ForEach-Object {$_.GetType().FullName}|Select-Object -Unique)-join ','
 throw ('copy_return_values_leak_into_factory:count='+ $result.Count+ ';types='+ $types)
}
if((Get-FileHash $source).Hash-cne(Get-FileHash $destination).Hash){throw 'copy_content_not_preserved'}
'complete_helper_copy_output_passed:2;native_service_operations=0;real_file_copy=1'
