param([Parameter(Mandatory)][string]$PackagePath,[Parameter(Mandatory)][string]$WixPath,[switch]$DevVm,[string]$SigningCertificateThumbprint,[string]$Version='1.0.0')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
try {
    $certificate=$null
    if (-not $DevVm) {
        if ($SigningCertificateThumbprint -notmatch '^[A-Fa-f0-9]{40}$') { throw 'publisher_signature_required' }
        $certificate=Get-Item -LiteralPath ('Cert:\CurrentUser\My\'+$SigningCertificateThumbprint)
        if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -lt (Get-Date) -or
            -not ($certificate.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3')) { throw 'publisher_signature_required' }
    }
    & {
        $machine=Get-CimInstance Win32_ComputerSystem
        if ($env:COMPUTERNAME -ne 'AgentPC' -or $machine.Model -ne 'VirtualBox' -or
            (Get-CimInstance Win32_ComputerSystemProduct).UUID -ne '46543236-8C5E-49DA-953C-DB17F9B1E402' -or
            (Get-Service VBoxService).Status -ne 'Running') { throw 'release_dev_vm_required' }
    }
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'release_version_invalid' }
    $output=[IO.Path]::GetFullPath($PackagePath).TrimEnd('\','/')
    if ((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath ($output+'.zip'))) { throw 'release_output_exists' }
    $source=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\','/')
    if ($output.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase) -or $output -eq $source) { throw 'release_output_inside_source' }
    $parent=Get-Item -LiteralPath ([IO.Path]::GetDirectoryName($output))
    for ($cursor=$parent;$null -ne $cursor;$cursor=$cursor.Parent) {
        if ($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'release_reparse_point' }
    }
    $commit=(& git -C $source rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or (& git -C $source status --porcelain)) { throw 'release_clean_source_required' }
    $null=New-Item -ItemType Directory -Path $output
    foreach ($project in @('Service','Helper','Cli','Desktop')) {
        & dotnet publish (Join-Path $source "src/HostAgent.$project/HostAgent.$project.csproj") --configuration Release --runtime win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -p:Version=$Version --output (Join-Path $output $project)
        if ($LASTEXITCODE -ne 0) { throw 'release_publish_failed' }
    }
    Copy-Item -LiteralPath (Join-Path $source 'docs') -Destination (Join-Path $output 'docs') -Recurse
    Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $output 'LICENSE')
    $null=New-Item -ItemType Directory -Path (Join-Path $output 'tools')
    foreach ($script in @('Test-Release.ps1','Install-HostAgent.ps1','Test-Installer.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination (Join-Path $output 'tools')
    }
    function Sign([string]$path) {
        $result=Set-AuthenticodeSignature -LiteralPath $path -Certificate $certificate -HashAlgorithm SHA256
        if ($result.Status -ne 'Valid') { throw 'publisher_signature_required' }
    }
    if (-not $DevVm) {
        foreach ($file in Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.Name -match '^HostAgent.*\.(exe|dll)$' -or $_.Extension -eq '.ps1' }) { Sign $file.FullName }
    }
    $null=& (Join-Path $PSScriptRoot 'Build-Installer.ps1') -PackagePath $output -WixPath $WixPath -Version $Version -DevVm:$DevVm -SigningCertificate $certificate
    if ($LASTEXITCODE -ne 0) { throw 'release_installer_build_failed' }
    $null=& (Join-Path $PSScriptRoot 'Test-Installer.ps1') -PackagePath $output
    if (-not $DevVm) { Sign (Join-Path $output 'HostAgent.msi') }
    $entries=@()
    foreach ($file in Get-ChildItem -LiteralPath $output -Recurse -File | Sort-Object FullName) {
        $relative=[IO.Path]::GetRelativePath($output,$file.FullName).Replace('\','/')
        if ($relative -notmatch '^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$') { throw 'release_path_invalid' }
        $entries+="@{Path='$relative';Sha256='$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)';Length=$($file.Length)}"
    }
    $manifest=Join-Path $output 'manifest.psd1'
    [IO.File]::WriteAllText($manifest,"@{Format=1;Version='$Version';SourceCommit='$commit';Files=@("+($entries -join ",`n")+")}",[Text.UTF8Encoding]::new($false))
    if (-not $DevVm) { Sign $manifest }
    $null=& (Join-Path $PSScriptRoot 'Test-Release.ps1') -PackagePath $output -DevVm:$DevVm -TrustedPublisherThumbprint $SigningCertificateThumbprint
    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { throw 'release_validation_failed' }
    Compress-Archive -LiteralPath $output -DestinationPath ($output+'.zip') -CompressionLevel Optimal
    @{package=$output;zip=($output+'.zip');sourceCommit=$commit;devOnly=[bool]$DevVm;zipSha256=(Get-FileHash -LiteralPath ($output+'.zip') -Algorithm SHA256).Hash} | ConvertTo-Json -Compress
} catch {
    $code=[string]$_.Exception.Message
    if ($code -notmatch '^(release_[a-z_]+|publisher_signature_required)$') { $code='release_build_failed' }
    [Console]::Error.WriteLine($code);exit 1
}
