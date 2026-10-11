using System.Security.Principal;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Security;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class InstallationBootstrapTests
{
    // Defect: first install cannot create valid protected defaults, or repair overwrites operator configuration/data.
    [Fact]
    public void Provisioning_creates_valid_protected_shadow_defaults_and_preserves_existing_files()
    {
        var root=Path.Combine(Path.GetTempPath(),"bootstrap-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var sid=WindowsIdentity.GetCurrent().User!;
            InstallationBootstrap.Provision(root,sid,sid);
            var servicePath=Path.Combine(root,"HostAgent","service.json");
            var helperPath=Path.Combine(root,"HostAgent.Helper","helper.json");
            var service=ServiceConfiguration.LoadSafe(servicePath,sid);var helper=HelperConfiguration.LoadSafe(helperPath);
            Assert.True(service.ConfigurationAvailable);Assert.Equal(OperatingMode.Shadow,service.Mode);Assert.True(service.ReadOnly);
            Assert.False(service.EnablePushover);Assert.False(service.EnableHeartbeat);
            Assert.True(helper.ConfigurationAvailable);Assert.True(helper.Shadow);Assert.True(helper.ReadOnly);
            Assert.Empty(helper.Services);Assert.Empty(helper.Processes);Assert.Empty(helper.ApproverSids);
            var before=File.ReadAllBytes(servicePath);File.WriteAllText(Path.Combine(root,"HostAgent","preserved.db"),"preserved");
            InstallationBootstrap.Provision(root,sid,sid);
            Assert.Equal(before,File.ReadAllBytes(servicePath));Assert.Equal("preserved",File.ReadAllText(Path.Combine(root,"HostAgent","preserved.db")));
            HelperProtectedFiles.ValidateExisting(servicePath,sid);HelperProtectedFiles.ValidateExisting(helperPath,sid);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void Existing_unprotected_directory_is_refused_without_changing_permissions_or_files()
    {
        var root=Path.Combine(Path.GetTempPath(),"bootstrap-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"HostAgent"));
        try
        {
            var sid=WindowsIdentity.GetCurrent().User!;
            Assert.Throws<UnauthorizedAccessException>(()=>InstallationBootstrap.Provision(root,sid,sid));
            Assert.Empty(Directory.GetFiles(Path.Combine(root,"HostAgent")));
        }
        finally{Directory.Delete(root,true);}
    }
}
