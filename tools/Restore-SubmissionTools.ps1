#requires -Version 7.0
param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'signing/HostedSubmission.psm1') -Force
try{
 $source=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
 $commit=Assert-SubmissionHost $source
 $output=Assert-NewSubmissionPath $OutputPath $source
 $pins=Import-PowerShellDataFile (Join-Path $PSScriptRoot 'signing/SubmissionInputs.psd1')
 $null=New-Item -ItemType Directory -Path $output
 $downloads=Join-Path $output 'downloads';$null=New-Item -ItemType Directory -Path $downloads
 function Save-SubmissionDownload([string]$Uri,[string]$Destination,[string]$ExpectedHash){
  $http=[Net.Http.HttpClient]::new();$http.Timeout=[timespan]::FromMinutes(3)
  try{
   $response=$http.GetAsync($Uri,[Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult();$null=$response.EnsureSuccessStatusCode()
   $inputStream=$response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
   $outputStream=[IO.File]::Open($Destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
   try{$buffer=[byte[]]::new(65536);$count=0L;while(($read=$inputStream.Read($buffer,0,$buffer.Length)) -gt 0){$count+=$read;if($count -gt 128MB){throw 'submission_download_limit'};$outputStream.Write($buffer,0,$read)}}finally{$outputStream.Dispose();$inputStream.Dispose();$response.Dispose()}
  }finally{$http.Dispose()}
  if((Get-FileHash -LiteralPath $Destination).Hash -cne $ExpectedHash){throw 'submission_dependency_hash_mismatch'}
 }
 foreach($package in $pins.DtfPackages){
  $file=Join-Path $downloads ($package.Id+'.'+$package.Version+'.nupkg')
  Save-SubmissionDownload ('https://api.nuget.org/v3-flatcontainer/'+$package.Id+'/'+$package.Version+'/'+$package.Id+'.'+$package.Version+'.nupkg') $file $package.Sha256
  & dotnet nuget verify $file --all
  if($LASTEXITCODE -ne 0){throw 'submission_dependency_signature_invalid'}
  $destination=Join-Path $output $package.Id
  [IO.Compression.ZipFile]::ExtractToDirectory($file,$destination)
 }
 $wix=Join-Path $output 'wix'
 & dotnet tool install wix --version $pins.WixVersion --tool-path $wix --configfile (Join-Path $PSScriptRoot 'signing/NuGet.Config')
 if($LASTEXITCODE -ne 0){throw 'submission_wix_restore_failed'}
 & (Join-Path $wix 'wix.exe') extension add -g ('WixToolset.Util.wixext/'+$pins.WixVersion)
 if($LASTEXITCODE -ne 0){throw 'submission_wix_extension_failed'}
 $archive=Join-Path $output ('wix-source-'+$pins.WixSourceCommit+'.zip')
 Save-SubmissionDownload ('https://codeload.github.com/wixtoolset/wix/zip/'+$pins.WixSourceCommit) $archive $pins.WixSourceSha256
 @{sourceCommit=$commit;wixVersion=$pins.WixVersion;dtfPackageHashesVerified=$true;dtfNugetSignaturesVerified=$true;wixSourceSha256=$pins.WixSourceSha256} | ConvertTo-Json -Compress
}catch{ $code=[string]$_.Exception.Message;if($code -notmatch '^submission_[a-z_]+$'){$code='submission_tool_restore_failed'};[Console]::Error.WriteLine($code);exit 1 }
