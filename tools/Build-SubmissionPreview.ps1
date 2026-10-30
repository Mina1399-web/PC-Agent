#requires -Version 7.0
param([Parameter(Mandatory)][string]$PackagePath,[Parameter(Mandatory)][string]$WixPath,[Parameter(Mandatory)][string]$DtfRoot,[Parameter(Mandatory)][string]$WixSourceArchive,[string]$Version='1.2.8')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'signing/HostedSubmission.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'signing/SubmissionManifest.psm1') -Force
$stage='host'
try{
 $source=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
 $commit=Assert-SubmissionHost $source
 if($Version -cnotmatch '^\d+\.\d+\.\d+$'){throw 'submission_version_invalid'}
 $output=Assert-NewSubmissionPath $PackagePath $source
 $pins=Import-PowerShellDataFile (Join-Path $PSScriptRoot 'signing/SubmissionInputs.psd1')
 foreach($path in @($WixPath,$DtfRoot,$WixSourceArchive)){Assert-SubmissionPlainPath $path}
 $inputs=@{
  (Join-Path $DtfRoot 'wixtoolset.dtf.customaction/tools/x64/SfxCA.dll')=$pins.SfxCaSha256
  (Join-Path $DtfRoot 'wixtoolset.dtf.windowsinstaller/lib/net20/WixToolset.Dtf.WindowsInstaller.dll')=$pins.DtfAssemblySha256
  $WixSourceArchive=$pins.WixSourceSha256
 }
 foreach($path in $inputs.Keys){if((Get-FileHash -LiteralPath $path).Hash -cne $inputs[$path]){throw 'submission_dependency_hash_mismatch'}}
 $work=Join-Path ([IO.Path]::GetDirectoryName($output)) ('submission-work-'+[guid]::NewGuid().ToString('N'))
 $null=New-Item -ItemType Directory -Path $work
 $stage='publish'
 $payload=Join-Path $work 'review-payload'
 & (Join-Path $PSScriptRoot 'Build-Review.ps1') -PackagePath $payload -Version $Version
 if($LASTEXITCODE -ne 0){throw 'submission_publish_failed'}
 $null=New-Item -ItemType Directory -Path $output
 foreach($name in @('Service','Helper','Cli','Desktop','docs','LICENSE')){Copy-Item -LiteralPath (Join-Path $payload $name) -Destination (Join-Path $output $name) -Recurse}
 $sourceArchiveName='wix-source-'+$pins.WixSourceCommit+'.zip'
 Copy-Item -LiteralPath $WixSourceArchive -Destination (Join-Path $output ('docs/legal/'+$sourceArchiveName))
 $stage='dependency_inventory'
 $inventory=& (Join-Path $PSScriptRoot 'New-SigningDependencyInventory.ps1') -SourcePath $source -SourceCommit $commit -PackagePath $output -OutputPath (Join-Path $output 'docs/legal/dependency-inventory-2026-10-25.json')
 if($inventory.status -cne 'written'){throw 'submission_inventory_failed'}
 $stage='custom_action'
 . (Join-Path $PSScriptRoot 'installer/Build-InstallerAction.ps1')
 . (Join-Path $PSScriptRoot 'installer/New-InstallerSource.ps1')
 $action=Build-HostAgentInstallerAction -WorkDirectory $work -DtfRoot $DtfRoot -Version $Version
 $stage='msi'
 $xml=New-HostAgentInstallerSource -PackageRoot $output -Version $Version -EmbeddedActionPath $action.EmbeddedPath -Channel SubmissionPreview
 $wxs=Join-Path $work 'Product.wxs';[IO.File]::WriteAllText($wxs,$xml,[Text.UTF8Encoding]::new($false))
 & $WixPath build $wxs -arch x64 -ext ('WixToolset.Util.wixext/'+$pins.WixVersion) -pdbtype none -out (Join-Path $output 'HostAgent.msi')
 if($LASTEXITCODE -ne 0){throw 'submission_msi_build_failed'}
 $null=New-Item -ItemType Directory -Path (Join-Path $output 'tools/signing') -Force
 foreach($name in @('Read-SubmissionPlan.ps1','Test-SubmissionPreview.ps1','Test-Installer.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $output ('tools/'+$name))}
 foreach($name in @('SubmissionManifest.psm1','SubmissionInputs.psd1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('signing/'+$name)) -Destination (Join-Path $output ('tools/signing/'+$name))}
 $null=New-Item -ItemType Directory -Path (Join-Path $output 'signing-input')
 Copy-Item -LiteralPath $action.ManagedPath -Destination (Join-Path $output 'signing-input/Preflight.dll')
 [IO.File]::WriteAllText((Join-Path $output 'README-FIRST.txt'),"Host Agent V1 $Version - unsigned submission preview. This package has not been signed or approved by SignPath. First verify the archive hash against its trusted build receipt, then read docs/operations/submission-preview.md, docs/operations/privacy.md and tools/Read-SubmissionPlan.ps1. Install only in an authorized test environment; Windows protections and Shadow/read-only must remain enabled. Native installation acceptance and owner approval are required before public release. Source: $commit",[Text.UTF8Encoding]::new($false))
 $stage='owned_signing_configuration'
 $owned=Get-SubmissionOwnedSigningCandidates -PackageRoot $output
 $configuration=[xml]'<artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1"><parameters><parameter name="version" required="true" /></parameters><zip-file /></artifact-configuration>'
 $zipElement=$configuration.DocumentElement.LastChild
 foreach($file in $owned | Sort-Object FullName){
  if($file.Name -cnotmatch '^(HostAgent\.(Core|Infrastructure|Service|Helper|Cli|Desktop)\.(exe|dll)|Preflight\.dll)$'){throw 'submission_unknown_owned_binary'}
  $metadata=[Diagnostics.FileVersionInfo]::GetVersionInfo($file.FullName)
  if($metadata.ProductName -cne 'Host Agent V1' -or $metadata.ProductVersion -cne $Version){throw 'submission_owned_metadata_invalid'}
  $element=$configuration.CreateElement('pe-file',$configuration.DocumentElement.NamespaceURI)
  $element.SetAttribute('path',[IO.Path]::GetRelativePath($output,$file.FullName).Replace('\','/'))
  $element.SetAttribute('product-name','Host Agent V1');$element.SetAttribute('product-version','${version}')
  $null=$element.AppendChild($configuration.CreateElement('authenticode-sign',$configuration.DocumentElement.NamespaceURI));$null=$zipElement.AppendChild($element)
 }
 $configuration.Save((Join-Path $output 'signing-input/owned-payload.xml'))
 $stage='manifest'
 $entries=@(foreach($file in Get-ChildItem -LiteralPath $output -File -Recurse | Sort-Object FullName){
  if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'submission_reparse_point'}
  [ordered]@{path=[IO.Path]::GetRelativePath($output,$file.FullName).Replace('\','/');length=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash}
 })
 $manifest=Join-Path $output 'manifest.json'
 [ordered]@{format=1;channel='unsigned-submission-preview';signed=$false;version=$Version;sourceCommit=$commit;repository=$env:GITHUB_REPOSITORY;runId=$env:GITHUB_RUN_ID;files=$entries}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $manifest -Encoding utf8NoBOM
 $manifestHash=(Get-FileHash -LiteralPath $manifest).Hash
 $stage='artifact_validation'
 & (Join-Path $PSScriptRoot 'Test-SubmissionPreview.ps1') -PackagePath $output -ExpectedSourceCommit $commit -ExpectedVersion $Version -ExpectedManifestSha256 $manifestHash
 if($LASTEXITCODE -ne 0){throw 'submission_artifact_validation_failed'}
 $stage='archive'
 [IO.Compression.ZipFile]::CreateFromDirectory($output,($output+'.zip'),[IO.Compression.CompressionLevel]::Optimal,$false)
 & (Join-Path $PSScriptRoot 'Test-SubmissionPreview.ps1') -PackagePath $output -ArchivePath ($output+'.zip') -ExpectedSourceCommit $commit -ExpectedVersion $Version -ExpectedManifestSha256 $manifestHash
 if($LASTEXITCODE -ne 0){throw 'submission_archive_validation_failed'}
 @{status='PREPARED_NATIVE_ACCEPTANCE_PENDING';sourceCommit=$commit;version=$Version;runId=$env:GITHUB_RUN_ID;manifestSha256=$manifestHash;msiSha256=(Get-FileHash -LiteralPath (Join-Path $output 'HostAgent.msi')).Hash;zipSha256=(Get-FileHash -LiteralPath ($output+'.zip')).Hash;ownedSigningEntries=$owned.Count;files=$entries.Count;signingPerformed=$false;installerExecuted=$false}|ConvertTo-Json -Compress
}catch{
 $code=[string]$_.Exception.Message
 if($code -notmatch '^(submission_|release_|signing_inventory_)[a-z_]+$'){$code='submission_build_failed'}
 [Console]::Error.WriteLine($code+':stage='+$stage);exit 1
}
