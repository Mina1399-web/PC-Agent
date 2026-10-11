param([Parameter(Mandatory)][string]$PackagePath,[switch]$DevVm,[string]$TrustedPublisherThumbprint)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
try {
    if ($DevVm) {
        $machine=Get-CimInstance Win32_ComputerSystem
        $product=Get-CimInstance Win32_ComputerSystemProduct
        if ($env:COMPUTERNAME -ne 'AgentPC' -or $machine.Model -ne 'VirtualBox' -or
            $product.UUID -ne '46543236-8C5E-49DA-953C-DB17F9B1E402' -or
            (Get-Service VBoxService).Status -ne 'Running') { throw 'release_dev_vm_required' }
    }
    $root=[IO.Path]::GetFullPath($PackagePath).TrimEnd('\','/')
    $cursor=Get-Item -LiteralPath $root
    while ($null -ne $cursor) {
        if ($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'release_reparse_point' }
        $cursor=$cursor.Parent
    }
    $manifest=Join-Path $root 'manifest.psd1'
    if (-not $DevVm) {
        if ($TrustedPublisherThumbprint -notmatch '^[A-Fa-f0-9]{40}$') { throw 'publisher_signature_required' }
        $signature=Get-AuthenticodeSignature -LiteralPath $manifest
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $TrustedPublisherThumbprint) {
            throw 'publisher_signature_required'
        }
    }
    if ((Get-Item -LiteralPath $manifest).Length -gt 4MB) { throw 'release_manifest_limit' }
    $data=Import-PowerShellDataFile -LiteralPath $manifest
    if ($data.Format -ne 1 -or $data.SourceCommit -notmatch '^[a-f0-9]{40}$' -or
        $data.Version -notmatch '^\d+\.\d+\.\d+$' -or $data.Files.Count -gt 10000) { throw 'release_manifest_invalid' }
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $data.Files) {
        $relative=[string]$entry.Path
        if ($relative -notmatch '^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$' -or
            ($relative.Split('/') | Where-Object { $_ -eq '.' -or $_ -eq '..' }) -or
            $relative -eq 'manifest.psd1' -or -not $seen.Add($relative) -or
            $entry.Sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or $entry.Length -lt 0) { throw 'release_path_invalid' }
        $path=Join-Path $root $relative
        $file=Get-Item -LiteralPath $path
        if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            $file.Length -ne $entry.Length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Sha256) {
            throw 'release_integrity_failed'
        }
        if (-not $DevVm -and ($file.Name -match '^HostAgent.*\.(exe|dll|msi)$' -or $file.Extension -eq '.ps1')) {
            $signature=Get-AuthenticodeSignature -LiteralPath $path
            if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $TrustedPublisherThumbprint) {
                throw 'publisher_signature_required'
            }
        }
    }
    foreach ($item in Get-ChildItem -LiteralPath $root -Recurse -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'release_reparse_point' }
        if (-not $item.PSIsContainer) {
            $relative=[IO.Path]::GetRelativePath($root,$item.FullName).Replace('\','/')
            if ($relative -ne 'manifest.psd1' -and -not $seen.Contains($relative)) { throw 'release_unlisted_file' }
        }
    }
    foreach ($required in @('Service/HostAgent.Service.exe','Helper/HostAgent.Helper.exe','Cli/HostAgent.Cli.exe')) {
        if (-not $seen.Contains($required)) { throw 'release_missing_component' }
    }
    @{valid=$true;devOnly=[bool]$DevVm;sourceCommit=$data.SourceCommit;files=$seen.Count} | ConvertTo-Json -Compress
} catch {
    $code=[string]$_.Exception.Message
    if ($code -notmatch '^(release_[a-z_]+|publisher_signature_required)$') { $code='release_validation_failed' }
    [Console]::Error.WriteLine($code)
    exit 1
}
