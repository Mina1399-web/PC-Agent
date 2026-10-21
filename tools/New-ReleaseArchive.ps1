param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$ArchivePath
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

function Assert-ArchivePlainPath([string]$Path) {
    for ($cursor=$Path; -not [string]::IsNullOrEmpty($cursor); $cursor=[IO.Path]::GetDirectoryName($cursor)) {
        $item=Get-Item -LiteralPath $cursor -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'release_archive_reparse_point' }
    }
}

if (-not [IO.Path]::IsPathFullyQualified($PackagePath) -or -not [IO.Path]::IsPathFullyQualified($ArchivePath)) {
    throw 'release_archive_path_invalid'
}
$source=[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($PackagePath))
$archive=[IO.Path]::GetFullPath($ArchivePath)
if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw 'release_archive_source_missing' }
Assert-ArchivePlainPath $source
if ([string]::IsNullOrEmpty([IO.Path]::GetDirectoryName($source))) { throw 'release_archive_source_root' }
if ((Test-Path -LiteralPath $archive) -or $null -ne (Get-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue)) {
    throw 'release_archive_output_exists'
}
if ($archive.Equals($source,[StringComparison]::OrdinalIgnoreCase) -or
    $archive.StartsWith($source+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'release_archive_output_inside_source'
}
$parent=[IO.Path]::GetDirectoryName($archive)
if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw 'release_archive_parent_missing' }
Assert-ArchivePlainPath $parent

$pending=[Collections.Generic.Stack[string]]::new()
$pending.Push($source)
while ($pending.Count -gt 0) {
    foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'release_archive_reparse_point' }
        if ($item.PSIsContainer) { $pending.Push($item.FullName) }
    }
}

# Create mode streams each file to the new ZIP instead of retaining Update-mode
# entry buffers. IncludeBaseDirectory preserves Compress-Archive's root folder.
# CreateFromDirectory uses FileMode.CreateNew: a concurrent output creation also
# fails without overwriting. Any partial output is preserved on failure.
[IO.Compression.ZipFile]::CreateFromDirectory($source,$archive,[IO.Compression.CompressionLevel]::Optimal,$true)
