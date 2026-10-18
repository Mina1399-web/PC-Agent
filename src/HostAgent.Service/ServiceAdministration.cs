using System.Security.Principal;
using Microsoft.Extensions.DependencyInjection;
using HostAgent.Core.Contracts;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using HostAgent.Infrastructure.Dashboard;
namespace HostAgent.Service;
public static class ServiceAdministration
{
    public static void Add(IServiceCollection services,SecurityIdentifier serviceSid,string pipeName="HostAgent.Admin.v1",SecurityIdentifier? operatorRole=null)
    {
        services.AddSingleton(sp=>
        {
            var logger=sp.GetRequiredService<ILogger<DashboardDispatcher>>();
            return new DashboardDispatcher(sp.GetRequiredService<IHostAgentStore>(),
                sp.GetRequiredService<ServiceConfiguration>(),sp.GetRequiredService<AgentRuntimeHealth>(),TimeProvider.System,
                value=>
                {
                    if(value.Kind!="completed")
                        logger.LogWarning("View dispatch stage {Stage} failed ({Kind}) after {ElapsedMilliseconds} ms.",
                            value.Stage,value.Kind,value.ElapsedMilliseconds);
                    else if(value.ElapsedMilliseconds>=500)
                        logger.LogWarning("View dispatch stage {Stage} took {ElapsedMilliseconds} ms.",
                            value.Stage,value.ElapsedMilliseconds);
                });
        });
        services.AddHostedService(sp=>
        {
            var logger=sp.GetRequiredService<ILogger<DashboardWorker>>();
            var server=new AdminPipeServer("HostAgent.View.v1",serviceSid,
                new(WellKnownSidType.InteractiveSid,null),sp.GetRequiredService<DashboardDispatcher>().DispatchAsync,
                TimeSpan.FromSeconds(10),reportDiagnostic:value=>logger.LogWarning(
                    "View pipe failed at {Stage} ({Kind}) after {ElapsedMilliseconds} ms.",
                    value.Stage,value.Kind,value.ElapsedMilliseconds));
            return new DashboardWorker(server.ServeOnceAsync,value=>logger.LogWarning(
                "View worker failed at {Stage} ({Kind}); retrying after {RetryDelayMilliseconds} ms.",
                value.Stage,value.Kind,value.RetryDelayMilliseconds));
        });
        services.AddSingleton(sp=>new AdminDispatcher(sp.GetRequiredService<IHostAgentStore>(),sp.GetRequiredService<RemediationCoordinator>(),
            sp.GetRequiredService<ServiceConfiguration>(),sp.GetRequiredService<AgentRuntimeHealth>(),sp.GetRequiredService<ProtectedSecretStore>().WriteAsync));
        services.AddHostedService(sp=>new AdminWorker(new AdminPipeServer(pipeName,serviceSid,
            operatorRole??new(WellKnownSidType.BuiltinAdministratorsSid,null),sp.GetRequiredService<AdminDispatcher>().DispatchAsync,
            TimeSpan.FromSeconds(30),code=>sp.GetRequiredService<ILogger<AdminWorker>>().LogWarning("Administrative connection rejected: {Code}.",code)).ServeOnceAsync,sp.GetRequiredService<AgentRuntimeHealth>()));
    }
}
