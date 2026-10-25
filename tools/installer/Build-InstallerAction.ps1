function Build-HostAgentInstallerAction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$WorkDirectory,
        [Parameter(Mandatory)][string]$DtfRoot,
        [Parameter(Mandatory)][string]$Version,
        [Security.Cryptography.X509Certificates.X509Certificate2]$SigningCertificate
    )
    $ErrorActionPreference='Stop'
    Set-StrictMode -Version Latest
    if($Version -cnotmatch '^\d+\.\d+\.\d+$'){throw 'release_version_invalid'}
    if($null -ne $SigningCertificate -and -not $SigningCertificate.HasPrivateKey){throw 'publisher_signature_required'}
    if(-not [IO.Path]::IsPathFullyQualified($WorkDirectory) -or -not [IO.Path]::IsPathFullyQualified($DtfRoot)){throw 'release_path_invalid'}
    $work=[IO.Path]::GetFullPath($WorkDirectory)
    $dtfRoot=[IO.Path]::GetFullPath($DtfRoot)
    foreach($directory in @($work,$dtfRoot)){
        $item=Get-Item -LiteralPath $directory -Force
        if(-not $item.PSIsContainer){throw 'release_path_invalid'}
        for($cursor=$item;$null -ne $cursor;$cursor=$cursor.Parent){
            if($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'release_reparse_point'}
        }
    }
    foreach($name in @('Preflight.dll','Preflight.CA.dll','AssemblyInfo.cs')){
        if(Test-Path -LiteralPath (Join-Path $work $name)){throw 'release_output_exists'}
    }
$dtfAssembly=Join-Path $dtfRoot 'wixtoolset.dtf.windowsinstaller/lib/net20/WixToolset.Dtf.WindowsInstaller.dll'
$dtfTools=Join-Path $dtfRoot 'wixtoolset.dtf.customaction/tools'
$managed=Join-Path $work 'Preflight.dll'
$embedded=Join-Path $work 'Preflight.CA.dll'
$metadata=Join-Path $work 'AssemblyInfo.cs'
[IO.File]::WriteAllText($metadata,'using System.Reflection; [assembly: AssemblyProduct("Host Agent V1")] [assembly: AssemblyInformationalVersion("'+$Version+'")] [assembly: AssemblyFileVersion("'+$Version+'.0")]',[Text.UTF8Encoding]::new($false))
& "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize+ /target:library ("/out:"+$managed) ("/reference:"+$dtfAssembly) $metadata (Join-Path $PSScriptRoot 'Preflight.cs') (Join-Path $PSScriptRoot 'CounterReadTransaction.cs') (Join-Path $PSScriptRoot 'WindowsCounterReadState.cs') (Join-Path $PSScriptRoot 'CounterReadActions.cs') (Join-Path $PSScriptRoot '../../src/HostAgent.Infrastructure/Security/InstallationTreeSecurity.cs') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'release_preflight_compile_failed' }
if ($null -ne $SigningCertificate) {
    if ((Set-AuthenticodeSignature -LiteralPath $managed -Certificate $SigningCertificate -HashAlgorithm SHA256).Status -ne 'Valid') { throw 'publisher_signature_required' }
}
& (Join-Path $dtfTools 'WixToolset.Dtf.MakeSfxCA.exe') $embedded (Join-Path $dtfTools 'x64/SfxCA.dll') $managed (Join-Path $PSScriptRoot 'CustomAction.config') $dtfAssembly | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'release_preflight_package_failed' }
if ($null -ne $SigningCertificate) {
    if ((Set-AuthenticodeSignature -LiteralPath $embedded -Certificate $SigningCertificate -HashAlgorithm SHA256).Status -ne 'Valid') { throw 'publisher_signature_required' }
}

    return [pscustomobject]@{ManagedPath=$managed;EmbeddedPath=$embedded}
}
