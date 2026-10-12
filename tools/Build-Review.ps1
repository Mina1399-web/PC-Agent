param([Parameter(Mandatory)][string]$PackagePath,[string]$Version='1.0.3')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
try {
    # This path only produces unsigned review payloads. It never calls an installer,
    # obtains a certificate, changes trust, or weakens the separate production gate.
    if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or
        $env:RUNNER_OS -ne 'Windows' -or $env:GITHUB_REPOSITORY -ne 'Mina1399-web/PC-Agent') {
        throw 'review_hosted_runner_required'
    }
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'review_version_invalid' }
    $source=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\','/')
    $output=[IO.Path]::GetFullPath($PackagePath).TrimEnd('\','/')
    if ($output -eq $source -or $output.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'review_output_inside_source' }
    if ((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath ($output+'.zip'))) { throw 'review_output_exists' }
    for ($cursor=Get-Item -LiteralPath ([IO.Path]::GetDirectoryName($output));$null -ne $cursor;$cursor=$cursor.Parent) {
        if ($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'review_reparse_point' }
    }
    $commit=(& git -C $source rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -cne $env:GITHUB_SHA -or (& git -C $source status --porcelain)) { throw 'review_clean_source_required' }
    $null=New-Item -ItemType Directory -Path $output
    foreach($project in @('Service','Helper','Cli','Desktop')) {
        & dotnet publish (Join-Path $source "src/HostAgent.$project/HostAgent.$project.csproj") --configuration Release --runtime win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -p:Version=$Version -p:IncludeSourceRevisionInInformationalVersion=false --output (Join-Path $output $project)
        if ($LASTEXITCODE -ne 0) { throw 'review_publish_failed' }
    }
    Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $output 'LICENSE')
    Copy-Item -LiteralPath (Join-Path $source 'docs') -Destination (Join-Path $output 'docs') -Recurse
    [IO.File]::WriteAllText((Join-Path $output 'REVIEW-ONLY.txt'),"Unsigned prerelease payload for source/build review. No MSI, installation script or production acceptance is supplied. Do not bypass Windows protections. Source: $commit`nRead docs/operations/privacy.md before any use.",[Text.UTF8Encoding]::new($false))
    $binaries=@(Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object { $_.Name -match '^HostAgent.*\.(exe|dll)$' })
    if($binaries.Count -lt 9){throw 'review_missing_component'}
    foreach($file in $binaries) {
        $metadata=[Diagnostics.FileVersionInfo]::GetVersionInfo($file.FullName)
        if($metadata.ProductName -cne 'Host Agent V1' -or $metadata.ProductVersion -cne $Version) { throw 'review_metadata_invalid' }
    }
    $files=@(foreach($file in Get-ChildItem -LiteralPath $output -Recurse -File | Sort-Object FullName) {
        @{path=[IO.Path]::GetRelativePath($output,$file.FullName).Replace('\','/');length=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
    })
    @{format=1;reviewOnly=$true;signed=$false;version=$Version;sourceCommit=$commit;repository=$env:GITHUB_REPOSITORY;runId=$env:GITHUB_RUN_ID;files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'review-manifest.json') -Encoding utf8NoBOM
    Compress-Archive -LiteralPath $output -DestinationPath ($output+'.zip') -CompressionLevel Optimal
    @{reviewOnly=$true;signed=$false;sourceCommit=$commit;zipSha256=(Get-FileHash -LiteralPath ($output+'.zip') -Algorithm SHA256).Hash} | ConvertTo-Json -Compress
} catch {
    $code=[string]$_.Exception.Message
    if($code -notmatch '^review_[a-z_]+$'){$code='review_build_failed'}
    [Console]::Error.WriteLine($code);exit 1
}
