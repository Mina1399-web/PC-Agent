#requires -Version 7.0
param([Parameter(Mandatory)][string]$PackagePath,[Parameter(Mandatory)][string]$ExpectedSourceCommit,[Parameter(Mandatory)][string]$ExpectedVersion,[Parameter(Mandatory)][string]$ExpectedManifestSha256,[string]$ArchivePath)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'signing/SubmissionManifest.psm1') -Force
try{
 $root=[IO.Path]::GetFullPath($PackagePath).TrimEnd('\','/')
 $manifestPath=Join-Path $root 'manifest.json'
 $manifest=Read-SubmissionManifest -LiteralPath $manifestPath -ExpectedSourceCommit $ExpectedSourceCommit -ExpectedVersion $ExpectedVersion -ExpectedManifestSha256 $ExpectedManifestSha256
 $items=@(Get-ChildItem -LiteralPath $root -Recurse -Force)
 if($items.Count -gt 12000 -or @($items | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'submission_payload_tree_invalid'}
 $expected=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
 foreach($row in $manifest.Files){$expected.Add($row.Path,$row)}
 $actual=@($items | Where-Object {-not $_.PSIsContainer -and $_.FullName -ne $manifestPath})
 if($actual.Count -ne $expected.Count){throw 'submission_payload_membership_mismatch'}
 foreach($file in $actual){
  $relative=[IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')
  if($file.Extension -match '^\.(db|sqlite|pfx|key|pem|etl|evtx|vbox|trx|pdb|log)$' -or $relative -match '(^|/)(bin|obj|\.git|secrets)(/|$)'){throw 'submission_forbidden_payload'}
  if(-not $expected.ContainsKey($relative) -or $file.Length -ne $expected[$relative].Length -or (Get-FileHash -LiteralPath $file.FullName).Hash -cne $expected[$relative].Sha256.ToUpperInvariant()){throw 'submission_payload_hash_mismatch'}
 }
 $pins=Import-PowerShellDataFile (Join-Path $PSScriptRoot 'signing/SubmissionInputs.psd1')
 $required=@('HostAgent.msi','LICENSE','docs/operations/privacy.md','docs/operations/submission-preview.md','docs/legal/dependency-inventory-2026-10-25.json','docs/legal/dotnet-runtime-10.0.12-LICENSE.txt','docs/legal/dotnet-runtime-10.0.12-THIRD-PARTY-NOTICES.txt','docs/legal/windowsdesktop-runtime-10.0.12-LICENSE.txt','docs/legal/wix-5.0.2-LICENSE.txt',('docs/legal/wix-source-'+$pins.WixSourceCommit+'.zip'),'signing-input/Preflight.dll','signing-input/owned-payload.xml')
 foreach($project in @('Service','Helper','Cli','Desktop')){$required+=($project+'/HostAgent.'+$project+'.exe')}
 foreach($path in $required){if(-not $expected.ContainsKey($path)){throw 'submission_required_payload_missing'}}
 $wixSource=Join-Path $root ('docs/legal/wix-source-'+$pins.WixSourceCommit+'.zip')
 if((Get-FileHash -LiteralPath $wixSource).Hash -cne $pins.WixSourceSha256){throw 'submission_wix_source_mismatch'}
 $inventory=Get-Content -LiteralPath (Join-Path $root 'docs/legal/dependency-inventory-2026-10-25.json') -Raw | ConvertFrom-Json
 if($inventory.sourceCommit -cne $ExpectedSourceCommit -or -not $inventory.publishedGraphAvailable -or $inventory.runtimePacks.Count -lt 2){throw 'submission_inventory_origin_invalid'}
 $owned=@($actual | Where-Object {$_.Name -match '^HostAgent\..*\.(exe|dll)$' -or $_.FullName -eq (Join-Path $root 'signing-input/Preflight.dll')})
 $ownedNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($file in $owned){
  if($file.Name -cnotmatch '^(HostAgent\.(Core|Infrastructure|Service|Helper|Cli|Desktop)\.(exe|dll)|Preflight\.dll)$'){throw 'submission_unknown_owned_binary'}
  $metadata=[Diagnostics.FileVersionInfo]::GetVersionInfo($file.FullName)
  if($metadata.ProductName -cne 'Host Agent V1' -or $metadata.ProductVersion -cne $ExpectedVersion){throw 'submission_owned_metadata_invalid'}
  $null=$ownedNames.Add([IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/'))
 }
 $xmlSettings=[Xml.XmlReaderSettings]::new();$xmlSettings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$xmlSettings.XmlResolver=$null
 $reader=[Xml.XmlReader]::Create((Join-Path $root 'signing-input/owned-payload.xml'),$xmlSettings)
 try{$configuration=[Xml.XmlDocument]::new();$configuration.XmlResolver=$null;$configuration.Load($reader)}finally{$reader.Dispose()}
 $ns=[Xml.XmlNamespaceManager]::new($configuration.NameTable);$ns.AddNamespace('s','http://signpath.io/artifact-configuration/v1')
 $entries=@($configuration.SelectNodes('/s:artifact-configuration/s:zip-file/s:pe-file',$ns))
 if($entries.Count -ne $ownedNames.Count -or $configuration.SelectNodes('//s:msi-file',$ns).Count){throw 'submission_signing_scope_invalid'}
 foreach($entry in $entries){if(-not $ownedNames.Remove($entry.GetAttribute('path')) -or $entry.GetAttribute('product-name') -cne 'Host Agent V1' -or $entry.GetAttribute('product-version') -cne '${version}' -or $entry.SelectNodes('s:authenticode-sign',$ns).Count -ne 1){throw 'submission_signing_scope_invalid'}}
 if((Get-AuthenticodeSignature -LiteralPath (Join-Path $root 'HostAgent.msi')).Status -ne 'NotSigned'){throw 'submission_unsigned_state_invalid'}
 $inspection=(& (Join-Path $PSScriptRoot 'Test-Installer.ps1') -PackagePath $root -ArtifactOnly -SubmissionPreview | Out-String) | ConvertFrom-Json
 if(-not $inspection.valid -or $inspection.devVmGuard -or $inspection.devVmIdentityStatus -cne 'NOTRUN'){throw 'submission_installer_tables_invalid'}
 $payloadPaths=@($manifest.Files | Where-Object {$_.Path -ceq 'LICENSE' -or $_.Path -cmatch '^(Service|Helper|Cli|Desktop|docs)/'} | ForEach-Object {$_.Path})
 $installed=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach($path in $inspection.installedPaths){if(-not $installed.Add($path)){throw 'submission_installer_payload_mismatch'}}
 if(-not $installed.SetEquals([string[]]$payloadPaths)){throw 'submission_installer_payload_mismatch'}
 if($ArchivePath){
  $archive=[IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ArchivePath))
  try{
   $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
   foreach($entry in $archive.Entries){
    $name=$entry.FullName
    if($name.EndsWith('/')){if($name.TrimEnd('/') -cnotmatch '^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$' -or @($name.Split('/')|Where-Object {$_ -in '.','..'}).Count){throw 'submission_archive_path_invalid'};continue}
    if(-not $seen.Add($name)){throw 'submission_archive_duplicate'}
    if($name -ceq 'manifest.json'){$length=(Get-Item -LiteralPath $manifestPath).Length;$hash=$ExpectedManifestSha256.ToUpperInvariant()}elseif($expected.ContainsKey($name)){$length=$expected[$name].Length;$hash=$expected[$name].Sha256.ToUpperInvariant()}else{throw 'submission_archive_membership_mismatch'}
    if($entry.Length -ne $length){throw 'submission_archive_length_mismatch'}
    $stream=$entry.Open();$algorithm=[Security.Cryptography.SHA256]::Create()
    try{$actualHash=[Convert]::ToHexString($algorithm.ComputeHash($stream))}finally{$algorithm.Dispose();$stream.Dispose()}
    if($actualHash -cne $hash){throw 'submission_archive_hash_mismatch'}
   }
   if($seen.Count -ne $expected.Count+1){throw 'submission_archive_membership_mismatch'}
  }finally{$archive.Dispose()}
 }
 @{status='PASS';scope='unsigned_candidate_artifact_only';sourceCommit=$manifest.SourceCommit;version=$manifest.Version;files=$manifest.Files.Count;manifestSha256=$ExpectedManifestSha256.ToUpperInvariant();ownedSigningEntries=$owned.Count;msiTableInspectionOnly=$true;installerExecuted=$false;archiveChecked=[bool]$ArchivePath}|ConvertTo-Json -Compress
 exit 0
}catch{$code=[string]$_.Exception.Message;if($code -notmatch '^submission_[a-z_]+$'){$code='submission_artifact_check_failed'};[Console]::Error.WriteLine($code);exit 1}
