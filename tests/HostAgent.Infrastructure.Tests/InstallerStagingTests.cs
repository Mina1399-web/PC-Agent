using System.Diagnostics;
using System.Text;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
[Collection("ReleaseTooling")]
public sealed class InstallerStagingTests
{
    [Fact]
    public async Task Staging_is_isolated_protected_and_validated_after_source_changes()
    {
        var result=await Run("""
            $owner=[Security.Principal.WindowsIdentity]::GetCurrent().User
            $stage=New-ProtectedInstallerStage $source (Join-Path $root 'staging') $owner
            if($stage -eq $source){throw 'source_path_reused'}
            [IO.File]::WriteAllText((Join-Path $source 'HostAgent.msi'),'substituted')
            Test-StagedInstaller $stage $true ''
            if([IO.File]::ReadAllText((Join-Path $stage 'HostAgent.msi')) -ne 'original'){throw 'copy_changed'}
            foreach($path in @($stage,(Join-Path $stage 'HostAgent.msi'),(Join-Path $stage 'manifest.psd1'))){
                $acl=Get-Acl -LiteralPath $path
                if(-not $acl.AreAccessRulesProtected -or $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne $owner.Value){throw 'unprotected_stage'}
                foreach($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
                    if($rule.IdentityReference.Value -notin @($owner.Value,'S-1-5-18','S-1-5-32-544')){throw 'unexpected_stage_grant'}
                }
            }
            $lock=[IO.File]::Open((Join-Path $stage 'HostAgent.msi'),'Open','Read','Read')
            try{$blocked=$false;try{[IO.File]::WriteAllText((Join-Path $stage 'HostAgent.msi'),'changed')}catch [IO.IOException]{$blocked=$true};if(-not $blocked){throw 'write_not_blocked'}}finally{$lock.Dispose()}
            'verified'
            """);
        Assert.True(result.Exit==0,result.Error+result.Out);Assert.Contains("verified",result.Out);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Staged_tampering_and_unsigned_production_are_refused(bool production)
    {
        var result=await Run("""
            $owner=[Security.Principal.WindowsIdentity]::GetCurrent().User
            $stage=New-ProtectedInstallerStage $source (Join-Path $root 'staging') $owner
            """+"\n"+(production?"Test-StagedInstaller $stage $false ''":"[IO.File]::WriteAllText((Join-Path $stage 'HostAgent.msi'),'tampered');Test-StagedInstaller $stage $true ''"));
        Assert.NotEqual(0,result.Exit);Assert.Contains(production?"publisher_signature_required":"release_integrity_failed",result.Error);
    }
    private static async Task<(int Exit,string Out,string Error)> Run(string body)
    {
        var root=Path.Combine(Path.GetTempPath(),"installer-stage-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var source=Path.Combine(root,"source");Directory.CreateDirectory(source);File.WriteAllText(Path.Combine(source,"HostAgent.msi"),"original",new UTF8Encoding(false));
            var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("original")));
            File.WriteAllText(Path.Combine(source,"manifest.psd1"),"@{Format=1;Version='1.0.0';SourceCommit='2932cb44749283af54cc2f2f52472405eec29c6d';Files=@(@{Path='HostAgent.msi';Sha256='"+hash+"';Length=8})}");
            var repository=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
            var wrapper=Path.Combine(repository,"tools","Install-HostAgent.ps1");var harness=Path.Combine(root,"probe.ps1");
            var setup="$ErrorActionPreference='Stop'\n$root='"+root.Replace("'","''")+"'\n$source='"+source.Replace("'","''")+"'\n"+
                "$ast=[Management.Automation.Language.Parser]::ParseFile('"+wrapper.Replace("'","''")+"',[ref]$null,[ref]$null)\n"+
                "$ast.FindAll({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst]},$false) | ForEach-Object { Invoke-Expression $_.Extent.Text }\n";
            File.WriteAllText(harness,setup+body,new UTF8Encoding(false));
            var info=new ProcessStartInfo("pwsh.exe"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"-NoProfile","-NonInteractive","-File",harness})info.ArgumentList.Add(arg);
            using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)); }
            catch (TimeoutException) { process.Kill(entireProcessTree:true);await process.WaitForExitAsync();throw; }
            return(process.ExitCode,await output,await errors);
        }
        finally{Directory.Delete(root,true);}
    }
}
