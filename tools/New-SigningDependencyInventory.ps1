#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$SourcePath,
    [Parameter(Mandatory)][string]$SourceCommit,
    [string]$PackagePath,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Only data files are read. No restore, package scripts, binaries or build targets
# are executed. The caller binds SourceCommit to its clean, reviewed checkout.
function Assert-PlainInventoryPath([string]$Path) {
    for ($item = Get-Item -LiteralPath $Path -Force; $null -ne $item; $item = $item.Parent) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'signing_inventory_reparse_point' }
        if ($item -is [IO.FileInfo]) { $item = $item.Directory; if ($null -eq $item) { break } }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'signing_inventory_reparse_point' }
    }
}
function Read-InventoryBytes([string]$Path, [long]$Maximum) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'signing_inventory_metadata_missing' }
    Assert-PlainInventoryPath $Path
    if ((Get-Item -LiteralPath $Path).Length -gt $Maximum) { throw 'signing_inventory_metadata_too_large' }
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -gt $Maximum) { throw 'signing_inventory_metadata_too_large' }
    return ,$bytes
}
function Get-InventoryHash([byte[]]$Bytes) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes))
}
function Read-InventoryJson([string]$Path) {
    $bytes = Read-InventoryBytes $Path 32MB
    try { $data = [Text.UTF8Encoding]::new($false, $true).GetString($bytes).TrimStart([char]0xFEFF) | ConvertFrom-Json -AsHashtable -Depth 100 }
    catch { throw 'signing_inventory_json_invalid' }
    if ($data -isnot [Collections.IDictionary]) { throw 'signing_inventory_json_invalid' }
    return @{ Data = $data; Sha256 = (Get-InventoryHash $bytes) }
}
function Get-InventoryRelativePath([string]$Root, [string]$Path) {
    $relative = [IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or $relative.StartsWith('../', [StringComparison]::Ordinal)) { throw 'signing_inventory_path_outside_root' }
    return $relative
}
function Find-InventoryFiles([string]$Root, [string]$Pattern, [bool]$SkipBuildDirectories) {
    $pending = [Collections.Generic.Stack[string]]::new()
    $found = [Collections.Generic.List[string]]::new()
    $pending.Push($Root)
    $visited = 0
    $filesSeen = 0
    while ($pending.Count) {
        if (++$visited -gt 2048) { throw 'signing_inventory_directory_limit' }
        $directory = $pending.Pop()
        Assert-PlainInventoryPath $directory
        foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'signing_inventory_reparse_point' }
            if ($item.PSIsContainer) {
                if ($SkipBuildDirectories -and $item.Name -in @('bin', 'obj', '.git')) { continue }
                $pending.Push($item.FullName)
            } else {
                if (++$filesSeen -gt 20000) { throw 'signing_inventory_file_limit' }
                if ($item.Name -like $Pattern) { $found.Add($item.FullName) }
            }
        }
    }
    return ,$found.ToArray()
}
function Split-InventoryPackageKey([string]$Key) {
    $match = [regex]::Match($Key, '^([A-Za-z0-9][A-Za-z0-9._-]{0,99})/([A-Za-z0-9][A-Za-z0-9.+-]{0,127})$')
    if (-not $match.Success) { throw 'signing_inventory_package_key_invalid' }
    return @{ Id = $match.Groups[1].Value; Version = $match.Groups[2].Value }
}
function Assert-PublicMetadataUri([string]$Value, [string]$Code) {
    $uri = $null
    $address = $null
    if (-not [uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -notin @('https', 'http') -or $uri.UserInfo -or $uri.Query -or $uri.Fragment -or
        -not $uri.Host.Contains('.') -or $uri.Host.EndsWith('.local', [StringComparison]::OrdinalIgnoreCase) -or
        $uri.Host.EndsWith('.internal', [StringComparison]::OrdinalIgnoreCase) -or
        [Net.IPAddress]::TryParse($uri.Host, [ref]$address)) { throw $Code }
}
function Get-NuspecText([Xml.XmlNode]$Metadata, [string]$Name) {
    $nodes = $Metadata.SelectNodes("*[local-name()='$Name']")
    if ($nodes.Count -gt 1) { throw 'signing_inventory_nuspec_invalid' }
    if ($nodes.Count -eq 0) { return $null }
    return $nodes[0].InnerText.Trim()
}
function Get-InventoryNuspec([string]$Id, [string]$Version, [string[]]$PackageFolders, [bool]$ProductRequired) {
    $selected = $null
    $selectedHash = $null
    $bytes = $null
    foreach ($folder in $PackageFolders) {
        $path = Join-Path $folder ($Id.ToLowerInvariant() + '/' + $Version.ToLowerInvariant() + '/' + $Id.ToLowerInvariant() + '.nuspec')
        $null = Get-InventoryRelativePath $folder ([IO.Path]::GetFullPath($path))
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        $candidateBytes = Read-InventoryBytes $path 1MB
        $hash = Get-InventoryHash $candidateBytes
        if ($selectedHash -and $selectedHash -cne $hash) { throw 'signing_inventory_nuspec_conflict' }
        $selected = $path; $selectedHash = $hash; $bytes = $candidateBytes
    }
    if (-not $selected) { throw 'signing_inventory_nuspec_missing' }
    $reader = $null
    $stream = [IO.MemoryStream]::new($bytes, $false)
    try {
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $settings.MaxCharactersInDocument = 1MB
        $reader = [Xml.XmlReader]::Create($stream, $settings)
        $xml = [Xml.XmlDocument]::new(); $xml.XmlResolver = $null; $xml.Load($reader)
        $nodes = $xml.SelectNodes("/*[local-name()='package']/*[local-name()='metadata']")
        if ($nodes.Count -ne 1) { throw 'invalid_nuspec' }
        $metadata = $nodes[0]
    } catch { throw 'signing_inventory_nuspec_invalid' }
    finally { if ($reader) { $reader.Dispose() }; $stream.Dispose() }
    if ((Get-NuspecText $metadata 'id') -ine $Id -or (Get-NuspecText $metadata 'version') -ine $Version) { throw 'signing_inventory_nuspec_identity_mismatch' }
    $licenseNodes = $metadata.SelectNodes("*[local-name()='license']")
    if ($licenseNodes.Count -gt 1) { throw 'signing_inventory_nuspec_invalid' }
    $license = $null; $licenseType = $null; $licenseFileHash = $null
    if ($licenseNodes.Count) {
        $license = $licenseNodes[0].InnerText.Trim()
        $licenseType = $licenseNodes[0].GetAttribute('type')
        if ($licenseType -ceq 'expression') {
            if ($license -notmatch '^[A-Za-z0-9][A-Za-z0-9 .()+-]{0,199}$') { throw 'signing_inventory_license_invalid' }
        } elseif ($licenseType -ceq 'file') {
            if ($license -notmatch '^[A-Za-z0-9_./-]{1,200}$' -or $license.Split('/') -contains '..' -or $license.StartsWith('/')) { throw 'signing_inventory_license_invalid' }
            $licensePath = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($selected)) $license))
            $null = Get-InventoryRelativePath ([IO.Path]::GetDirectoryName($selected)) $licensePath
            $licenseFileHash = Get-InventoryHash (Read-InventoryBytes $licensePath 1MB)
        } else { throw 'signing_inventory_license_invalid' }
    }
    $licenseUrl = Get-NuspecText $metadata 'licenseUrl'
    if ($licenseUrl) { Assert-PublicMetadataUri $licenseUrl 'signing_inventory_license_url_invalid' }
    if ($ProductRequired -and -not $license) { throw 'signing_inventory_product_license_missing' }
    $evidence = if ($license) { 'nuspec_' + $licenseType } elseif ($licenseUrl) { 'legacy_url_only' } else { 'missing_test_only' }
    $repositoryUrl = $null; $repositoryCommit = $null
    $repositories = $metadata.SelectNodes("*[local-name()='repository']")
    if ($repositories.Count -gt 1) { throw 'signing_inventory_nuspec_invalid' }
    if ($repositories.Count) {
        $repositoryUrl = $repositories[0].GetAttribute('url')
        if ($repositoryUrl) { Assert-PublicMetadataUri $repositoryUrl 'signing_inventory_repository_invalid' }
        $commit = $repositories[0].GetAttribute('commit')
        if ($commit -match '^(?:[a-fA-F0-9]{40}|[a-fA-F0-9]{64})$') { $repositoryCommit = $commit.ToLowerInvariant() }
    }
    return [ordered]@{ id = $Id; version = $Version; declaredLicense = $license; licenseType = $licenseType; licenseEvidence = $evidence; licenseUrl = $licenseUrl; licenseFileSha256 = $licenseFileHash; repositoryUrl = $repositoryUrl; repositoryCommit = $repositoryCommit; nuspecSha256 = $selectedHash }
}

try {
    if ($SourceCommit -notmatch '^[a-fA-F0-9]{40}$') { throw 'signing_inventory_source_commit_invalid' }
    $source = [IO.Path]::GetFullPath($SourcePath)
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw 'signing_inventory_source_missing' }
    Assert-PlainInventoryPath $source
    if (-not $OutputPath) { $OutputPath = Join-Path ([IO.Path]::GetTempPath()) ('HostAgent-signing-dependencies-' + [guid]::NewGuid().ToString('N') + '.json') }
    $output = [IO.Path]::GetFullPath($OutputPath)
    if (Test-Path -LiteralPath $output) { throw 'signing_inventory_output_exists' }
    if ([IO.Path]::GetExtension($output) -ine '.json') { throw 'signing_inventory_output_invalid' }
    Assert-PlainInventoryPath ([IO.Path]::GetDirectoryName($output))
    $projects = [Collections.Generic.List[object]]::new()
    $packages = @{}
    $folders = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($scopeRoot in @('src', 'tests')) {
        $root = Join-Path $source $scopeRoot
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        foreach ($project in (Find-InventoryFiles $root '*.csproj' $true | Sort-Object)) {
            if ($projects.Count -ge 64) { throw 'signing_inventory_project_limit' }
            $relative = Get-InventoryRelativePath $source $project
            $assetsPath = Join-Path ([IO.Path]::GetDirectoryName($project)) 'obj/project.assets.json'
            $read = Read-InventoryJson $assetsPath
            $data = $read.Data
            if (-not $data.Contains('libraries') -or $data.libraries -isnot [Collections.IDictionary] -or -not $data.Contains('packageFolders') -or $data.packageFolders -isnot [Collections.IDictionary]) { throw 'signing_inventory_assets_invalid' }
            $scope = if ($scopeRoot -ceq 'src') { 'product' } else { 'test-only' }
            $projects.Add([ordered]@{ path = $relative; scope = $scope; assetsSha256 = $read.Sha256 })
            foreach ($folder in $data.packageFolders.Keys) {
                if (-not [IO.Path]::IsPathFullyQualified($folder)) { throw 'signing_inventory_package_folder_invalid' }
                $full = [IO.Path]::GetFullPath($folder)
                if (-not (Test-Path -LiteralPath $full -PathType Container)) { throw 'signing_inventory_package_folder_invalid' }
                Assert-PlainInventoryPath $full
                $null = $folders.Add($full)
                if ($folders.Count -gt 16) { throw 'signing_inventory_package_folder_limit' }
            }
            foreach ($key in $data.libraries.Keys) {
                $library = $data.libraries[$key]
                if ($library -isnot [Collections.IDictionary] -or -not $library.Contains('type')) { throw 'signing_inventory_assets_invalid' }
                if ($library.type -cne 'package') { continue }
                $identity = Split-InventoryPackageKey $key
                if (-not $packages.ContainsKey($key)) { $packages[$key] = @{ Identity = $identity; Product = $false; Projects = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal) } }
                if ($scope -ceq 'product') { $packages[$key].Product = $true }
                $null = $packages[$key].Projects.Add($relative)
                if ($packages.Count -gt 512) { throw 'signing_inventory_package_limit' }
            }
        }
    }
    if (-not $projects.Count -or -not $packages.Count) { throw 'signing_inventory_projects_or_packages_missing' }
    $folderArray = @($folders | Sort-Object)
    $records = [Collections.Generic.List[object]]::new()
    foreach ($key in $packages.Keys | Sort-Object) {
        $item = $packages[$key]
        $record = Get-InventoryNuspec $item.Identity.Id $item.Identity.Version $folderArray $item.Product
        $record.scope = if ($item.Product) { 'product' } else { 'test-only' }
        $record.projects = @($item.Projects | Sort-Object)
        $records.Add($record)
    }
    $published = [Collections.Generic.List[object]]::new()
    $runtimeMap = @{}
    if ($PackagePath) {
        $payload = [IO.Path]::GetFullPath($PackagePath)
        if (-not (Test-Path -LiteralPath $payload -PathType Container)) { throw 'signing_inventory_payload_missing' }
        $depsPaths = Find-InventoryFiles $payload '*.deps.json' $false
        if (-not $depsPaths.Count -or $depsPaths.Count -gt 64) { throw 'signing_inventory_published_deps_limit' }
        foreach ($path in ($depsPaths | Sort-Object)) {
            $relative = Get-InventoryRelativePath $payload $path
            $read = Read-InventoryJson $path
            if (-not $read.Data.Contains('libraries') -or $read.Data.libraries -isnot [Collections.IDictionary]) { throw 'signing_inventory_published_deps_invalid' }
            foreach ($key in $read.Data.libraries.Keys | Sort-Object) {
                $library = $read.Data.libraries[$key]
                if ($library -isnot [Collections.IDictionary] -or -not $library.Contains('type')) { throw 'signing_inventory_published_deps_invalid' }
                if ($library.type -notin @('package', 'runtimepack')) { continue }
                $identity = Split-InventoryPackageKey $key
                $id = $identity.Id
                if ($library.type -ceq 'package') {
                    if (-not $packages.ContainsKey($key) -or -not $packages[$key].Product) { throw 'signing_inventory_published_package_unmapped' }
                } else {
                    if (-not $id.StartsWith('runtimepack.', [StringComparison]::Ordinal)) { throw 'signing_inventory_runtime_key_invalid' }
                    $id = $id.Substring('runtimepack.'.Length)
                    $runtimeKey = $id + '/' + $identity.Version
                    if (-not $runtimeMap.ContainsKey($runtimeKey)) {
                        $record = Get-InventoryNuspec $id $identity.Version $folderArray $true
                        $record.scope = 'product-runtime'
                        $runtimeMap[$runtimeKey] = $record
                    }
                }
                $published.Add([ordered]@{ payload = $relative; depsSha256 = $read.Sha256; id = $id; version = $identity.Version; type = $library.type })
                if ($published.Count -gt 4096) { throw 'signing_inventory_published_dependency_limit' }
            }
        }
    }
    $runtimes = @($runtimeMap.Keys | Sort-Object | ForEach-Object { $runtimeMap[$_] })
    $inventory = [ordered]@{ format = 1; metadataOnly = $true; sourceCommit = $SourceCommit.ToLowerInvariant(); projectCount = $projects.Count; packageCount = $records.Count; projects = $projects.ToArray(); packages = $records.ToArray(); publishedGraphAvailable = [bool]$PackagePath; runtimePacks = $runtimes; publishedDependencies = $published.ToArray() }
    $json = $inventory | ConvertTo-Json -Depth 15
    if ($json.Length -gt 16MB) { throw 'signing_inventory_output_too_large' }
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json + "`n")
    $stream = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
    [pscustomobject]@{ status = 'written'; outputFileName = [IO.Path]::GetFileName($output); projectCount = $projects.Count; packageCount = $records.Count; sha256 = (Get-InventoryHash $bytes) }
} catch {
    if ($_.Exception.Message -cmatch '^signing_inventory_[a-z_]+$') { throw }
    throw 'signing_inventory_metadata_invalid'
}
