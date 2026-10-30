#requires -Version 7.0
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Text.Json

function Assert-SubmissionPlainPath([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    while ($null -ne $item) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'submission_manifest_reparse_point' }
        $item = if ($item -is [IO.FileInfo]) { $item.Directory } else { $item.Parent }
    }
}
function Assert-SubmissionUniqueProperties([System.Text.Json.JsonElement]$Element) {
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) { throw 'submission_manifest_duplicate_property' }
            Assert-SubmissionUniqueProperties $property.Value
        }
    } elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($value in $Element.EnumerateArray()) { Assert-SubmissionUniqueProperties $value }
    }
}
function Assert-SubmissionObjectShape([System.Text.Json.JsonElement]$Element, [string[]]$Keys, [string]$Code) {
    if ($Element.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { throw $Code }
    $expected = [Collections.Generic.HashSet[string]]::new($Keys, [StringComparer]::Ordinal)
    $count = 0
    foreach ($property in $Element.EnumerateObject()) {
        if (-not $expected.Contains($property.Name)) { throw $Code }
        $count++
    }
    if ($count -ne $Keys.Count) { throw $Code }
}
function Get-SubmissionString([System.Text.Json.JsonElement]$Element, [string]$Code) {
    if ($Element.ValueKind -ne [System.Text.Json.JsonValueKind]::String) { throw $Code }
    return $Element.GetString()
}

function Read-SubmissionManifest {
    <#
    Reads only bounded JSON bytes, after checking their externally supplied
    SHA-256. It never imports a manifest as code or opens any payload file.
    #>
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [Parameter(Mandatory)][string]$ExpectedSourceCommit,
        [Parameter(Mandatory)][string]$ExpectedVersion,
        [Parameter(Mandatory)][string]$ExpectedManifestSha256
    )
    $document = $null
    try {
        if ($ExpectedSourceCommit -cnotmatch '\A[a-fA-F0-9]{40}\z' -or
            $ExpectedVersion -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+\z' -or
            $ExpectedManifestSha256 -cnotmatch '\A[a-fA-F0-9]{64}\z') { throw 'submission_manifest_expected_identity_invalid' }

        $path = [IO.Path]::GetFullPath($LiteralPath)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'submission_manifest_file_missing' }
        Assert-SubmissionPlainPath $path
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try {
            if ($stream.Length -gt 4MB) { throw 'submission_manifest_size_limit' }
            $bytes = [byte[]]::new([int]$stream.Length)
            $offset = 0
            while ($offset -lt $bytes.Length) {
                $read = $stream.Read($bytes, $offset, $bytes.Length - $offset)
                if ($read -le 0) { throw 'submission_manifest_read_failed' }
                $offset += $read
            }
            if ($stream.Length -ne $bytes.Length) { throw 'submission_manifest_read_failed' }
        } finally { $stream.Dispose() }

        # Hash and parser consume the same bytes; no unanchored JSON is parsed.
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($hasher.ComputeHash($bytes)).Replace('-', '') }
        finally { $hasher.Dispose() }
        if (-not [string]::Equals($hash, $ExpectedManifestSha256, [StringComparison]::OrdinalIgnoreCase)) { throw 'submission_manifest_hash_mismatch' }

        try {
            $options = [System.Text.Json.JsonDocumentOptions]::new()
            $options.MaxDepth = 16
            $options.AllowTrailingCommas = $false
            $options.CommentHandling = [System.Text.Json.JsonCommentHandling]::Disallow
            $document = [System.Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($bytes), $options)
        } catch { throw 'submission_manifest_json_invalid' }

        $root = $document.RootElement
        Assert-SubmissionUniqueProperties $root
        Assert-SubmissionObjectShape $root @('format', 'channel', 'signed', 'version', 'sourceCommit', 'repository', 'runId', 'files') 'submission_manifest_root_invalid'
        if ($root.GetProperty('format').ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or $root.GetProperty('format').GetRawText() -cne '1') { throw 'submission_manifest_root_invalid' }
        $channel = Get-SubmissionString ($root.GetProperty('channel')) 'submission_manifest_channel_invalid'
        if ($channel -cne 'unsigned-submission-preview') { throw 'submission_manifest_channel_invalid' }
        if ($root.GetProperty('signed').ValueKind -ne [System.Text.Json.JsonValueKind]::False) { throw 'submission_manifest_signed_invalid' }
        $source = Get-SubmissionString ($root.GetProperty('sourceCommit')) 'submission_manifest_source_mismatch'
        if ($source -cne $ExpectedSourceCommit) { throw 'submission_manifest_source_mismatch' }
        $version = Get-SubmissionString ($root.GetProperty('version')) 'submission_manifest_version_mismatch'
        if ($version -cne $ExpectedVersion) { throw 'submission_manifest_version_mismatch' }
        $repository = Get-SubmissionString ($root.GetProperty('repository')) 'submission_manifest_repository_mismatch'
        if ($repository -cne 'Mina1399-web/PC-Agent') { throw 'submission_manifest_repository_mismatch' }
        $runId = Get-SubmissionString ($root.GetProperty('runId')) 'submission_manifest_run_id_invalid'
        if ($runId -cnotmatch '\A[0-9]+\z') { throw 'submission_manifest_run_id_invalid' }
        $rows = $root.GetProperty('files')
        if ($rows.ValueKind -ne [System.Text.Json.JsonValueKind]::Array -or $rows.GetArrayLength() -lt 1 -or $rows.GetArrayLength() -gt 10000) { throw 'submission_manifest_file_count_invalid' }

        $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $files = [Collections.Generic.List[object]]::new()
        $total = [long]0
        foreach ($row in $rows.EnumerateArray()) {
            Assert-SubmissionObjectShape $row @('path', 'length', 'sha256') 'submission_manifest_file_invalid'
            $relative = Get-SubmissionString ($row.GetProperty('path')) 'submission_manifest_path_invalid'
            if ($relative -cnotmatch '\A[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*\z' -or
                [IO.Path]::IsPathRooted($relative) -or $relative -ieq 'manifest.json' -or
                @($relative.Split('/') | Where-Object { $_ -ceq '.' -or $_ -ceq '..' }).Count) { throw 'submission_manifest_path_invalid' }
            if (-not $paths.Add($relative)) { throw 'submission_manifest_duplicate_path' }
            $lengthElement = $row.GetProperty('length')
            $length = [long]0
            if ($lengthElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
                $lengthElement.GetRawText() -cnotmatch '\A(?:0|[1-9][0-9]*)\z' -or
                -not $lengthElement.TryGetInt64([ref]$length) -or $length -lt 0 -or $length -gt 1GB) { throw 'submission_manifest_length_invalid' }
            $total += $length
            if ($total -gt 2GB) { throw 'submission_manifest_total_length_limit' }
            $fileHash = Get-SubmissionString ($row.GetProperty('sha256')) 'submission_manifest_file_hash_invalid'
            if ($fileHash -cnotmatch '\A[a-fA-F0-9]{64}\z') { throw 'submission_manifest_file_hash_invalid' }
            $files.Add([pscustomobject][ordered]@{ Path = $relative; Length = $length; Sha256 = $fileHash.ToUpperInvariant() })
        }
        return [pscustomobject][ordered]@{ Format = 1; Version = $version; SourceCommit = $source; RunId = $runId; Files = $files.ToArray() }
    } catch {
        if ($_.Exception.Message -cmatch '\Asubmission_manifest_[a-z_]+\z') { throw }
        throw 'submission_manifest_read_failed'
    } finally { if ($null -ne $document) { $document.Dispose() } }
}

function Get-SubmissionOwnedSigningCandidates {
    param([Parameter(Mandatory)][string]$PackageRoot)
    $root = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\', '/')
    $preflight = [IO.Path]::GetFullPath((Join-Path $root 'signing-input/Preflight.dll'))
    return @(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object {
        (($_.Name -match '^HostAgent\..*\.(exe|dll)$') -and ($_.Name -notmatch '\.resources\.dll$')) -or
        [string]::Equals([IO.Path]::GetFullPath($_.FullName), $preflight, [StringComparison]::OrdinalIgnoreCase)
    })
}

Export-ModuleMember -Function Read-SubmissionManifest, Get-SubmissionOwnedSigningCandidates
