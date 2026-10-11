using HostAgent.Core.Contracts;
using HostAgent.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace HostAgent.Service;
public static class ServiceRuntime
{
    public static void AddWorkers(IServiceCollection services,ServiceConfiguration configuration)
    {
        services.AddSingleton(configuration);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<AgentRuntimeHealth>();
        services.AddSingleton<IncidentPublisher>();
        services.AddSingleton<RemediationCoordinator>();
        services.AddSingleton<OutboundCoordinator>();
        services.Configure<HostOptions>(options=>options.ShutdownTimeout=TimeSpan.FromSeconds(15));
        services.AddHostedService<CollectionCoordinator>();
        services.AddHostedService<OutboundWorker>();
    }
}
internal sealed class OutboundWorker(IHostAgentStore store,IncidentPublisher publisher,OutboundCoordinator outbound,
    AgentRuntimeHealth health,ServiceConfiguration configuration,ILogger<OutboundWorker> logger,IHeartbeatSender heartbeatSender):BackgroundService
{
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try{await heartbeatSender.SendAsync(new(health.Degraded,true),deadline.Token).AsTask().WaitAsync(deadline.Token);}
        catch{logger.LogWarning("Shutdown heartbeat unavailable.");}
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var notifications=new ResilientWorker(async token=>
        {
            try
            {
                await publisher.PublishAsync(await store.ReadIncidentHeadsAsync(token),DateTimeOffset.UtcNow,token);
                health.Set("publication",false);
            }
            catch (OperationCanceledException) when(token.IsCancellationRequested){throw;}
            catch {health.Set("publication",true);}
            await outbound.RunNotificationsAsync(DateTimeOffset.UtcNow,token);
            logger.LogInformation("Outbound cycle completed; degraded {Degraded}.",outbound.Degraded);
        },TimeSpan.FromSeconds(configuration.HeartbeatIntervalSeconds),TimeSpan.FromMinutes(18),
            TimeProvider.System,code=>logger.LogWarning("Outbound worker {Code}.",code));
        using var heartbeat=new ResilientWorker(token=>outbound.RunHeartbeatAsync(health.Degraded,token).AsTask(),
            TimeSpan.FromSeconds(configuration.HeartbeatIntervalSeconds),TimeSpan.FromSeconds(35),TimeProvider.System,
            code=>logger.LogWarning("Heartbeat worker {Code}.",code));
        await notifications.StartAsync(stoppingToken);
        await heartbeat.StartAsync(stoppingToken);
        await Task.WhenAll(notifications.ExecuteTask!,heartbeat.ExecuteTask!);
    }
}
