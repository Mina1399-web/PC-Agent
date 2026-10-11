using System.Security.Principal;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Security;
using HostAgent.Core.Models.Remediation;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class OperatorConfigurationTests
{
    [Fact]
    public void Process_registration_pins_file_hash_and_explicit_removal_revokes_only_selected_alias()
    {
        WithRoot((root,sid)=>
        {
            var path=Path.Combine(root,"synthetic.exe");File.WriteAllText(path,"not an executable; never launched");
            OperatorConfiguration.Apply(root,sid,sid,sid.Value,["allow-process","demo",path]);
            var helperPath=Path.Combine(root,"HostAgent.Helper","helper.json");
            var process=Assert.Single(HelperConfiguration.LoadSafe(helperPath).Processes);
            Assert.Equal(path,process.ExecutablePath);Assert.True(process.Enabled);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),process.Sha256);
            OperatorConfiguration.Apply(root,sid,sid,sid.Value,["allow-service","print","Spooler","RestartService"]);
            OperatorConfiguration.Apply(root,sid,sid,sid.Value,["remove-process","demo"]);
            Assert.Empty(HelperConfiguration.LoadSafe(helperPath).Processes);Assert.Single(HelperConfiguration.LoadSafe(helperPath).Services);
            OperatorConfiguration.Apply(root,sid,sid,sid.Value,["remove-service","print"]);
            Assert.Empty(HelperConfiguration.LoadSafe(helperPath).Services);
        });
    }
    [Fact]
    public void Explicit_configuration_enables_confirmation_without_broadening_registrations_and_can_return_to_shadow()
    {
        WithRoot((root,sid)=>
        {
            OperatorConfiguration.Apply(root,sid,sid,sid.Value,["allow-service","print","Spooler","RestartService"]);
            var helperPath=Path.Combine(root,"HostAgent.Helper","helper.json");
            var helper=HelperConfiguration.LoadSafe(helperPath);
            Assert.True(helper.Shadow);Assert.True(helper.ReadOnly);Assert.Single(helper.Services);
            Assert.Equal("Spooler",helper.Services[0].ServiceName);Assert.Equal(new[]{HelperOperation.RestartService},helper.Services[0].EnabledOperations);
            Assert.Equal(new[]{sid.Value},helper.ApproverSids);
            OperatorConfiguration.Apply(root,sid,sid,sid.Value,["configure","Confirmation","on","off"]);
            var service=ServiceConfiguration.LoadSafe(Path.Combine(root,"HostAgent","service.json"),sid);
            helper=HelperConfiguration.LoadSafe(helperPath);
            Assert.Equal(OperatingMode.Confirmation,service.Mode);Assert.False(service.ReadOnly);Assert.True(service.EnablePushover);Assert.False(service.EnableHeartbeat);
            Assert.False(helper.Shadow);Assert.False(helper.ReadOnly);Assert.Single(helper.Services);Assert.Empty(helper.Processes);
            OperatorConfiguration.Apply(root,sid,sid,sid.Value,["configure","Shadow","off","off"]);
            Assert.True(HelperConfiguration.LoadSafe(helperPath).Shadow);Assert.True(ServiceConfiguration.LoadSafe(Path.Combine(root,"HostAgent","service.json"),sid).ReadOnly);
        });
    }
    [Fact]
    public void Invalid_operation_or_mode_cannot_modify_either_configuration()
    {
        WithRoot((root,sid)=>
        {
            var paths=new[]{Path.Combine(root,"HostAgent","service.json"),Path.Combine(root,"HostAgent.Helper","helper.json")};
            var before=paths.Select(File.ReadAllBytes).ToArray();
            foreach(var args in new[]{new[]{"configure","Autonomous","on","on"},new[]{"allow-service","print","Spooler","RunShell"},new[]{"allow-service","print","Spooler","TerminateProcess"}})
                Assert.Throws<InvalidDataException>(()=>OperatorConfiguration.Apply(root,sid,sid,sid.Value,args));
            for(var i=0;i<paths.Length;i++)Assert.Equal(before[i],File.ReadAllBytes(paths[i]));
        });
    }
    private static void WithRoot(Action<string,SecurityIdentifier> test)
    {
        var root=Path.Combine(Path.GetTempPath(),"operator-config-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{var sid=WindowsIdentity.GetCurrent().User!;InstallationBootstrap.Provision(root,sid,sid);test(root,sid);}
        finally{Directory.Delete(root,true);}
    }
}
