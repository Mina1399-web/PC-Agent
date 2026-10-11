using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
[Collection("ReleaseTooling")]
public sealed class ReleaseToolingTests
{
    [Fact]
    public async Task Release_builder_refuses_unsigned_production_and_overwriting_existing_output()
    {
        using var fixture=new PackageFixture();
        var production=await Run("Build-Release.ps1",fixture.Path,"-WixPath","not-used");
        Assert.NotEqual(0,production.Exit);Assert.Contains("publisher_signature_required",production.Error);
        var existing=await Run("Build-Release.ps1",fixture.Path,"-WixPath","not-used","-DevVm");
        Assert.NotEqual(0,existing.Exit);Assert.Contains("release_output_exists",existing.Error);
    }
    // Defect: distribution has no inspectable MSI with pinned identities, service stop/remove and SYSTEM-only bootstrap.
    [Fact]
    public async Task Built_msi_has_only_product_services_and_transactional_installation_actions()
    {
        using var fixture=new PackageFixture();
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../../"));
        var wix=System.IO.Path.Combine(root,"HostAgent-DEV-tools","wix","wix.exe");
        var build=await Run("Build-Installer.ps1",fixture.Path,"-WixPath",wix,"-DevVm");
        Assert.True(build.Exit==0,build.Error+build.Out);Assert.True(File.Exists(System.IO.Path.Combine(fixture.Path,"HostAgent.msi")));
        var inspect=await Run("Test-Installer.ps1",fixture.Path);
        Assert.True(inspect.Exit==0,inspect.Error+inspect.Out);using var json=JsonDocument.Parse(inspect.Out);
        Assert.Equal("NT SERVICE\\HostAgent",json.RootElement.GetProperty("monitorAccount").GetString());
        Assert.Equal("LocalSystem",json.RootElement.GetProperty("helperAccount").GetString());
        Assert.Equal(2,json.RootElement.GetProperty("serviceCount").GetInt32());
        Assert.True(json.RootElement.GetProperty("bootstrapIsSystemDeferred").GetBoolean());
        Assert.True(json.RootElement.GetProperty("precopyValidationIsSystemDeferred").GetBoolean());
        Assert.True(json.RootElement.GetProperty("uninstallPreservesData").GetBoolean());
        Assert.Equal(2,json.RootElement.GetProperty("recoveryServiceCount").GetInt32());
        Assert.Equal(4,json.RootElement.GetProperty("fileCount").GetInt32());
        Assert.True(json.RootElement.GetProperty("existingServicesGuarded").GetBoolean());
        Assert.True(json.RootElement.GetProperty("devVmGuard").GetBoolean());
        Assert.True(json.RootElement.GetProperty("devVmIdentityMatches").GetBoolean(),
            "The actual MSI condition engine must accept the verified VM's Windows ComputerName casing.");
    }
    // Defect: verified release package cannot be validated without silently disabling the production signature gate.
    [Fact]
    public async Task Dev_package_integrity_passes_and_production_unsigned_package_is_refused()
    {
        using var fixture=new PackageFixture();
        var dev=await Run("Test-Release.ps1",fixture.Path,"-DevVm");
        Assert.Equal(0,dev.Exit);using var result=JsonDocument.Parse(dev.Out);
        Assert.True(result.RootElement.GetProperty("valid").GetBoolean());Assert.True(result.RootElement.GetProperty("devOnly").GetBoolean());
        var production=await Run("Test-Release.ps1",fixture.Path);
        Assert.NotEqual(0,production.Exit);Assert.Contains("publisher_signature_required",production.Error);
    }
    [Theory]
    [InlineData("tamper")]
    [InlineData("extra")]
    [InlineData("escape")]
    public async Task Tampered_extra_and_escaping_artifacts_are_refused(string change)
    {
        using var fixture=new PackageFixture();
        if(change=="tamper")File.AppendAllText(System.IO.Path.Combine(fixture.Path,"Service","HostAgent.Service.exe"),"tampered");
        if(change=="extra")File.WriteAllText(System.IO.Path.Combine(fixture.Path,"unexpected.dll"),"untrusted");
        if(change=="escape")File.WriteAllText(System.IO.Path.Combine(fixture.Path,"manifest.psd1"),"@{Format=1;Version='1.0.0';SourceCommit='2932cb44749283af54cc2f2f52472405eec29c6d';Files=@(@{Path='../escape';Sha256='"+new string('A',64)+"';Length=1})}");
        var result=await Run("Test-Release.ps1",fixture.Path,"-DevVm");
        Assert.NotEqual(0,result.Exit);Assert.Contains("release_",result.Error);
    }
    // Defect: installer performs changes during preflight, selects a privileged monitor, or hides missing elevation.
    [Fact]
    public async Task Installation_plan_is_read_only_and_defaults_to_shadow_and_virtual_service_account()
    {
        using var fixture=new PackageFixture();
        var plan=await Run("Install-HostAgent.ps1",fixture.Path,"-DevVm","-PlanOnly");
        Assert.Equal(0,plan.Exit);using var json=JsonDocument.Parse(plan.Out);
        Assert.Equal("Shadow",json.RootElement.GetProperty("mode").GetString());
        Assert.True(json.RootElement.GetProperty("readOnly").GetBoolean());
        Assert.Equal("NT SERVICE\\HostAgent",json.RootElement.GetProperty("monitorAccount").GetString());
        Assert.Equal("LocalSystem",json.RootElement.GetProperty("helperAccount").GetString());
        Assert.False(json.RootElement.GetProperty("changesPerformed").GetBoolean());
        var denied=await Run("Install-HostAgent.ps1",fixture.Path,"-DevVm");
        Assert.NotEqual(0,denied.Exit);Assert.Contains("administrator_required",denied.Error);
    }
    internal static async Task<(int Exit,string Out,string Error)> Run(string script,string package,params string[] args)
    {
        var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var shell="pwsh.exe";
        var info=new ProcessStartInfo(shell){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"-NoProfile","-NonInteractive","-File",System.IO.Path.Combine(root,"tools",script),"-PackagePath",package}.Concat(args))info.ArgumentList.Add(arg);
        using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)); }
        catch (TimeoutException) { process.Kill(entireProcessTree:true);await process.WaitForExitAsync();throw; }
        return(process.ExitCode,await output,await errors);
    }
    private sealed class PackageFixture:IDisposable
    {
        public string Path{get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"release-"+Guid.NewGuid().ToString("N"));
        public PackageFixture()
        {
            Directory.CreateDirectory(Path);var entries=new List<string>();
            foreach(var project in new[]{"Service","Helper","Cli"})
            {
                var relative=project+"/HostAgent."+project+".exe";var path=System.IO.Path.Combine(Path,relative);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                var bytes=Encoding.UTF8.GetBytes("synthetic offline package fixture");File.WriteAllBytes(path,bytes);
                entries.Add("@{Path='"+relative+"';Sha256='"+Convert.ToHexString(SHA256.HashData(bytes))+"';Length="+bytes.Length+"}");
            }
            Directory.CreateDirectory(System.IO.Path.Combine(Path,"Cli","cs"));
            var satellite=Encoding.UTF8.GetBytes("synthetic satellite");File.WriteAllBytes(System.IO.Path.Combine(Path,"Cli","cs","Synthetic.resources.dll"),satellite);
            entries.Add("@{Path='Cli/cs/Synthetic.resources.dll';Sha256='"+Convert.ToHexString(SHA256.HashData(satellite))+"';Length="+satellite.Length+"}");
            File.WriteAllText(System.IO.Path.Combine(Path,"manifest.psd1"),"@{Format=1;Version='1.0.0';SourceCommit='2932cb44749283af54cc2f2f52472405eec29c6d';Files=@("+
                string.Join(",",entries)+")}",new UTF8Encoding(false));
        }
        public void Dispose()=>Directory.Delete(Path,true);
    }
}
