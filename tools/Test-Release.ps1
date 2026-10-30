param([Parameter(Mandatory)][string]$PackagePath,[switch]$DevVm,[string]$TrustedPublisherThumbprint)
# Kept in both standalone trusted entry points: no external code or manifest evaluation.
function Read-ReleaseManifest([string]$LiteralPath) {
    function Get-ReleaseExpression($block) {
        if($block -is [Management.Automation.Language.PipelineAst]){$pipeline=$block}
        elseif($block -is [Management.Automation.Language.NamedBlockAst] -or $block -is [Management.Automation.Language.StatementBlockAst]) {
            if($block.Statements.Count -ne 1 -or $block.Traps){throw 'release_manifest_invalid'}
            $pipeline=$block.Statements[0]
        }else{throw 'release_manifest_invalid'}
        if($pipeline -isnot [Management.Automation.Language.PipelineAst] -or $pipeline.PipelineElements.Count -ne 1 -or $pipeline.Background){throw 'release_manifest_invalid'}
        $command=$pipeline.PipelineElements[0]
        if($command -isnot [Management.Automation.Language.CommandExpressionAst] -or $command.Redirections.Count){throw 'release_manifest_invalid'}
        return $command.Expression
    }
    function Get-ReleaseMap($expression,[string[]]$keys) {
        if($expression -isnot [Management.Automation.Language.HashtableAst] -or $expression.KeyValuePairs.Count -ne $keys.Count){throw 'release_manifest_invalid'}
        $map=@{}
        foreach($pair in $expression.KeyValuePairs) {
            if($pair.Item1 -isnot [Management.Automation.Language.StringConstantExpressionAst]){throw 'release_manifest_invalid'}
            $key=$pair.Item1.Value
            if($key -cnotin $keys -or $map.ContainsKey($key)){throw 'release_manifest_invalid'}
            $map[$key]=Get-ReleaseExpression $pair.Item2
        }
        return $map
    }
    function Get-ReleaseString($expression) {
        if($expression -isnot [Management.Automation.Language.StringConstantExpressionAst]){throw 'release_manifest_invalid'}
        return $expression.Value
    }
    $stream=[IO.File]::Open($LiteralPath,'Open','Read','Read')
    try {
        if($stream.Length -gt 4MB){throw 'release_manifest_limit'}
        $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true,4096,$true)
        try{$text=$reader.ReadToEnd()}finally{$reader.Dispose()}
    }finally{$stream.Dispose()}
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseInput($text,[ref]$tokens,[ref]$errors)
    if($errors -or $ast.ParamBlock -or $ast.DynamicParamBlock -or -not $ast.EndBlock.Unnamed -or $ast.BeginBlock -or $ast.ProcessBlock -or $ast.CleanBlock -or $ast.UsingStatements.Count){throw 'release_manifest_invalid'}
    $root=Get-ReleaseMap (Get-ReleaseExpression $ast.EndBlock) @('Format','Version','SourceCommit','Files')
    if($root.Format -isnot [Management.Automation.Language.ConstantExpressionAst] -or $root.Format.Value -isnot [int] -or $root.Format.Value -ne 1){throw 'release_manifest_invalid'}
    $version=Get-ReleaseString $root.Version;$commit=Get-ReleaseString $root.SourceCommit
    if($version -cnotmatch '^\d+\.\d+\.\d+$' -or $commit -cnotmatch '^[a-f0-9]{40}$' -or $root.Files -isnot [Management.Automation.Language.ArrayExpressionAst]){throw 'release_manifest_invalid'}
    $items=@()
    if($root.Files.SubExpression.Statements.Count) {
        $expression=Get-ReleaseExpression $root.Files.SubExpression
        $items=if($expression -is [Management.Automation.Language.ArrayLiteralAst]){@($expression.Elements)}else{@($expression)}
    }
    if($items.Count -gt 10000){throw 'release_manifest_limit'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $files=@(foreach($item in $items) {
        $entry=Get-ReleaseMap $item @('Path','Sha256','Length')
        $path=Get-ReleaseString $entry.Path;$hash=Get-ReleaseString $entry.Sha256;$length=$entry['Length']
        if($path -cnotmatch '^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$' -or ($path.Split('/') | Where-Object {$_ -in '.','..'}) -or $path -eq 'manifest.psd1' -or -not $seen.Add($path) -or $hash -cnotmatch '^[A-Fa-f0-9]{64}$' -or $length -isnot [Management.Automation.Language.ConstantExpressionAst]){throw 'release_manifest_invalid'}
        $bytes=$length.Value
        if(($bytes -isnot [int] -and $bytes -isnot [long]) -or $bytes -lt 0){throw 'release_manifest_invalid'}
        @{Path=$path;Sha256=$hash;Length=[long]$bytes}
    })
    return @{Format=1;Version=$version;SourceCommit=$commit;Files=$files}
}
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
try {
    if ($DevVm) {
        $machine=Get-CimInstance Win32_ComputerSystem
        $product=Get-CimInstance Win32_ComputerSystemProduct
        if ($env:COMPUTERNAME -ne 'AgentPC' -or $machine.Model -ne 'VirtualBox' -or
            $product.UUID -ne '0bb543d1-7bb0-43ed-9c94-596d63ef3310' -or
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
    $data=Read-ReleaseManifest -LiteralPath $manifest
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
    if ([version]$data.Version -ge [version]'1.1.0' -and -not $seen.Contains('Desktop/HostAgent.Desktop.exe')) { throw 'release_missing_component' }
    @{valid=$true;devOnly=[bool]$DevVm;sourceCommit=$data.SourceCommit;files=$seen.Count} | ConvertTo-Json -Compress
} catch {
    $code=[string]$_.Exception.Message
    if ($code -notmatch '^(release_[a-z_]+|publisher_signature_required)$') { $code='release_validation_failed' }
    [Console]::Error.WriteLine($code)
    exit 1
}
