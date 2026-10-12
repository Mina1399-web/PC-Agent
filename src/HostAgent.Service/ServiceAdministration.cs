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
        services.AddSingleton(sp=>new DashboardDispatcher(sp.GetRequiredService<IHostAgentStore>(),
            sp.GetRequiredService<ServiceConfiguration>(),sp.GetRequiredService<AgentRuntimeHealth>(),TimeProvider.System));
        services.AddHostedService(sp=>new DashboardWorker(new AdminPipeServer("HostAgent.View.v1",serviceSid,
            new(WellKnownSidType.InteractiveSid,null),sp.GetRequiredService<DashboardDispatcher>().DispatchAsync,TimeSpan.FromSeconds(10)).ServeOnceAsync));
        services.AddSingleton(sp=>new AdminDispatcher(sp.GetRequiredService<IHostAgentStore>(),sp.GetRequiredService<RemediationCoordinator>(),
            sp.GetRequiredService<ServiceConfiguration>(),sp.GetRequiredService<AgentRuntimeHealth>(),sp.GetRequiredService<ProtectedSecretStore>().WriteAsync));
        services.AddHostedService(sp=>new AdminWorker(new AdminPipeServer(pipeName,serviceSid,
            operatorRole??new(WellKnownSidType.BuiltinAdministratorsSid,null),sp.GetRequiredService<AdminDispatcher>().DispatchAsync,
            TimeSpan.FromSeconds(30),code=>sp.GetRequiredService<ILogger<AdminWorker>>().LogWarning("Administrative connection rejected: {Code}.",code)).ServeOnceAsync,sp.GetRequiredService<AgentRuntimeHealth>()));
    }
}
