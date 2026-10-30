param([Parameter(Mandatory)][string]$PackagePath,[switch]$DevVm,[string]$TrustedPublisherThumbprint,[switch]$PlanOnly)
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
function New-ProtectedInstallerStage([string]$Source,[string]$Parent,[Security.Principal.SecurityIdentifier]$Owner) {
    $parentPath=[IO.Path]::GetFullPath($Parent)
    Assert-InstallerPlainPath ([IO.Path]::GetDirectoryName($parentPath))
    $trusted=@($Owner.Value,'S-1-5-18','S-1-5-32-544') | Select-Object -Unique
    $security=[Security.AccessControl.DirectorySecurity]::new()
    $security.SetOwner($Owner);$security.SetAccessRuleProtection($true,$false)
    foreach($sid in $trusted){$security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid),'FullControl','ContainerInherit,ObjectInherit','None','Allow'))}
    [IO.FileSystemAclExtensions]::CreateDirectory($security,$parentPath) | Out-Null
    Assert-InstallerPlainPath $parentPath
    $acl=Get-Acl -LiteralPath $parentPath
    if(-not $acl.AreAccessRulesProtected -or $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted){throw 'release_staging_unsafe'}
    foreach($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
        if($rule.IdentityReference.Value -notin $trusted -or $rule.AccessControlType -ne 'Allow'){throw 'release_staging_unsafe'}
    }
    $stage=Join-Path $parentPath ([guid]::NewGuid().ToString('N'))
    [IO.FileSystemAclExtensions]::CreateDirectory($security,$stage) | Out-Null
    $fileSecurity=[Security.AccessControl.FileSecurity]::new()
    $fileSecurity.SetOwner($Owner);$fileSecurity.SetAccessRuleProtection($true,$false)
    foreach($sid in $trusted){$fileSecurity.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid),'FullControl','Allow'))}
    foreach($name in @('HostAgent.msi','manifest.psd1')){
        $original=Join-Path ([IO.Path]::GetFullPath($Source)) $name
        Assert-InstallerPlainPath $original
        $inputFile=[IO.File]::Open($original,'Open','Read','Read')
        try{
            if(($name -eq 'manifest.psd1' -and $inputFile.Length -gt 4MB) -or $inputFile.Length -gt 1GB){throw 'release_manifest_limit'}
            $outputFile=[IO.FileSystemAclExtensions]::Create([IO.FileInfo]::new((Join-Path $stage $name)),[IO.FileMode]::CreateNew,[Security.AccessControl.FileSystemRights]::Write,[IO.FileShare]::None,65536,[IO.FileOptions]::WriteThrough,$fileSecurity)
            try{$inputFile.CopyTo($outputFile);$outputFile.Flush($true)}finally{$outputFile.Dispose()}
        }finally{$inputFile.Dispose()}
    }
    return $stage
}
function Assert-InstallerPlainPath([string]$Path) {
    $cursor=Get-Item -LiteralPath $Path -Force
    while($null -ne $cursor){
        if($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'release_reparse_point'}
        $cursor=if($cursor -is [IO.DirectoryInfo]){$cursor.Parent}else{$cursor.Directory}
    }
}
function Test-StagedInstaller([string]$Stage,[bool]$Development,[string]$Publisher) {
    $manifest=Join-Path $Stage 'manifest.psd1';$msi=Join-Path $Stage 'HostAgent.msi'
    Assert-InstallerPlainPath $manifest;Assert-InstallerPlainPath $msi
    if($Development){
        if($env:COMPUTERNAME -ne 'AgentPC' -or (Get-CimInstance Win32_ComputerSystem).Model -ne 'VirtualBox' -or
            (Get-CimInstance Win32_ComputerSystemProduct).UUID -ne '0bb543d1-7bb0-43ed-9c94-596d63ef3310' -or (Get-Service VBoxService).Status -ne 'Running'){throw 'release_dev_vm_required'}
    }else{
        if($Publisher -notmatch '^[A-Fa-f0-9]{40}$'){throw 'publisher_signature_required'}
        foreach($path in @($manifest,$msi)){
            $signature=Get-AuthenticodeSignature -LiteralPath $path
            if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $Publisher){throw 'publisher_signature_required'}
        }
    }
    if((Get-Item -LiteralPath $manifest).Length -gt 4MB){throw 'release_manifest_limit'}
    $data=Read-ReleaseManifest -LiteralPath $manifest
    if($data.Format -ne 1 -or $data.SourceCommit -notmatch '^[a-f0-9]{40}$' -or $data.Version -notmatch '^\d+\.\d+\.\d+$' -or $data.Files.Count -gt 10000){throw 'release_manifest_invalid'}
    $entry=@($data.Files | Where-Object { $_.Path -ceq 'HostAgent.msi' })
    if($entry.Count -ne 1 -or $entry[0].Sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-Item -LiteralPath $msi).Length -ne $entry[0].Length -or
        (Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash -ne $entry[0].Sha256){throw 'release_integrity_failed'}
}
$stage=$null;$msiLock=$null;$manifestLock=$null
try {
    if ($PlanOnly) {
        $validation=@{PackagePath=$PackagePath;DevVm=$DevVm;TrustedPublisherThumbprint=$TrustedPublisherThumbprint}
        # Script success does not reset LASTEXITCODE. Ignore a caller's stale
        # native exit and initialize it for strict-mode callers with no prior native call.
        $LASTEXITCODE=0
        $validationOutput=@(& (Join-Path $PSScriptRoot 'Test-Release.ps1') @validation)
        if ($LASTEXITCODE -ne 0 -or $validationOutput.Count -ne 1) { throw 'release_validation_failed' }
        $receipt=$validationOutput[0] | ConvertFrom-Json -ErrorAction Stop
        if ($receipt.valid -isnot [bool] -or -not $receipt.valid -or
            $receipt.devOnly -isnot [bool] -or $receipt.devOnly -ne [bool]$DevVm) { throw 'release_validation_failed' }
        @{mode='Shadow';readOnly=$true;monitorAccount='NT SERVICE\HostAgent';helperAccount='LocalSystem';changesPerformed=$false} | ConvertTo-Json -Compress
        exit 0
    }
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'administrator_required'
    }
    $stagingParent=Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'HostAgent.InstallStaging'
    # Administrators owns these objects. Using the current user as owner would
    # also grant its unelevated token implicit WRITE_DAC access.
    $stage=New-ProtectedInstallerStage $PackagePath $stagingParent ([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    $msi=Join-Path $stage 'HostAgent.msi'
    $msiLock=[IO.File]::Open($msi,'Open','Read','Read')
    $manifestLock=[IO.File]::Open((Join-Path $stage 'manifest.psd1'),'Open','Read','Read')
    Test-StagedInstaller $stage ([bool]$DevVm) $TrustedPublisherThumbprint
    $process=Start-Process -FilePath "$env:SystemRoot\System32\msiexec.exe" -ArgumentList @('/i',('"'+$msi+'"'),'/qn','/norestart') -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -notin @(0,3010)) { throw 'installation_failed' }
    @{installed=$true;restartRequired=($process.ExitCode -eq 3010)} | ConvertTo-Json -Compress
} catch {
    $code=[string]$_.Exception.Message
    if ($code -notmatch '^(administrator_required|release_[a-z_]+|installation_failed|publisher_signature_required)$') { $code='installation_failed' }
    [Console]::Error.WriteLine($code);exit 1
} finally {
    if($null -ne $msiLock){$msiLock.Dispose()};if($null -ne $manifestLock){$manifestLock.Dispose()}
    if($null -ne $stage){
        $resolved=[IO.Path]::GetFullPath($stage)
        if([IO.Path]::GetDirectoryName($resolved) -eq [IO.Path]::GetFullPath($stagingParent) -and [IO.Path]::GetFileName($resolved) -match '^[a-f0-9]{32}$'){
            foreach($name in @('HostAgent.msi','manifest.psd1')){Remove-Item -LiteralPath (Join-Path $resolved $name) -Force -ErrorAction SilentlyContinue}
            Remove-Item -LiteralPath $resolved -Force -ErrorAction SilentlyContinue
        }
    }
}
