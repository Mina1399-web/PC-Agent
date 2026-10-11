using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using HostAgent.Core.Contracts;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Security;
using HostAgent.Service;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class OperatorHostingTests
{
    // Defect: shipping entry point cannot show help before service identity/data initialization.
    [Fact]
    public async Task Actual_cli_process_shows_help_without_installation_or_elevation()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var runtime=Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!,"dotnet.exe");
        var info=new ProcessStartInfo(runtime){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
        info.ArgumentList.Add(Path.Combine(root,"src/HostAgent.Cli/bin/Release/net10.0-windows/HostAgent.Cli.dll"));info.ArgumentList.Add("--help");
        using var process=Process.Start(info)!;
        var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0,process.ExitCode);Assert.Contains("secret-set",await output);Assert.Equal("",await errors);
    }
    // Defect: service startup never registers the authenticated operator listener or stop leaves it running.
    [Fact]
    public async Task Hosted_registration_serves_status_and_stops_real_pending_accept()
    {
        await using var fixture=await OperatingModeTests.Fixture.Create();
        var sid=WindowsIdentity.GetCurrent().User!;
        var parent=Path.Combine(Path.GetTempPath(),"operator-host-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(parent);
        try
        {
            var builder=Host.CreateApplicationBuilder();
            builder.Services.AddSingleton<IHostAgentStore>(fixture.Store);
            builder.Services.AddSingleton(new ServiceConfiguration());builder.Services.AddSingleton<AgentRuntimeHealth>();
            builder.Services.AddSingleton(new RemediationCoordinator(fixture.Store,fixture.Helper,new(),TimeProvider.System));
            builder.Services.AddSingleton(new ProtectedSecretStore(Path.Combine(parent,"secrets"),sid));
            var name="HostAgent-admin-test-"+Guid.NewGuid().ToString("N");ServiceAdministration.Add(builder.Services,sid,name,sid);
            using var host=builder.Build();await host.StartAsync();
            using var json=JsonDocument.Parse(await new AdminPipeClient(name,sid,TimeSpan.FromSeconds(1)).SendAsync(AdminProtocol.Request("status",new{}),default));
            Assert.True(json.RootElement.GetProperty("ok").GetBoolean());Assert.Equal("ready",json.RootElement.GetProperty("code").GetString());
            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally{Directory.Delete(parent,true);}
    }
}
