#requires -Version 7.0
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$module=Join-Path $PSScriptRoot '../signing/SubmissionManifest.psm1'
Import-Module $module -Force
$root=Join-Path ([IO.Path]::GetTempPath()) ('HostAgent-submission-signing-scope-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $root
try{
 foreach($relative in @(
  'Desktop/HostAgent.Desktop.exe',
  'Desktop/HostAgent.Desktop.dll',
  'Desktop/de/HostAgent.Desktop.resources.dll',
  'Desktop/cs-CZ/HostAgent.Desktop.resources.dll',
  'Desktop/HostAgent.Plugin.dll',
  'Desktop/Other.resources.dll',
  'signing-input/Preflight.dll'
 )){
  $path=Join-Path $root $relative
  $null=New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($path)) -Force
  [IO.File]::WriteAllBytes($path,[byte[]]::new(0))
 }
 $candidates=@(Get-SubmissionOwnedSigningCandidates -PackageRoot $root | ForEach-Object {[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/') } | Sort-Object)
 $expected=@('Desktop/HostAgent.Desktop.dll','Desktop/HostAgent.Desktop.exe','Desktop/HostAgent.Plugin.dll','signing-input/Preflight.dll') | Sort-Object
 if(($candidates -join "`n") -cne ($expected -join "`n")){throw 'submission_signing_scope_candidates_invalid'}
 foreach($satellite in @('Desktop/de/HostAgent.Desktop.resources.dll','Desktop/cs-CZ/HostAgent.Desktop.resources.dll','Desktop/Other.resources.dll')){
  if(-not(Test-Path -LiteralPath (Join-Path $root $satellite) -PathType Leaf)){throw 'submission_satellite_payload_removed'}
 }
 [pscustomobject]@{status='PASS';scope='satellite_resources_excluded_from_signing_candidates_but_retained_in_payload';ownedCandidates=$candidates.Count}|ConvertTo-Json -Compress
}finally{
 $resolved=[IO.Path]::GetFullPath($root)
 $temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
 if(-not$resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^HostAgent-submission-signing-scope-[a-f0-9]{32}$'){throw 'submission_signing_scope_cleanup_mismatch'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
