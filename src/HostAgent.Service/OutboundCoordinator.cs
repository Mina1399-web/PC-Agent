using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Storage;
using System.Text.Json;
namespace HostAgent.Service;
public sealed class OutboundCoordinator(INotificationStore store, INotificationSender sender, IHeartbeatSender heartbeat,IHostAgentStore? monitoringStore=null)
{
    private volatile bool _notificationFailed;
    private volatile bool _heartbeatFailed;
    public bool Degraded=>_notificationFailed||_heartbeatFailed;
    public async ValueTask RunCycleAsync(DateTimeOffset now, bool monitoringDegraded, CancellationToken cancellationToken)
    {
        await RunNotificationsAsync(now,cancellationToken);
        await RunHeartbeatAsync(monitoringDegraded,cancellationToken);
    }
    public async ValueTask RunNotificationsAsync(DateTimeOffset now,CancellationToken cancellationToken)
    {
        try
        {
            _notificationFailed=await store.HasFailuresAsync(cancellationToken);
            foreach (var record in await store.ReadDueAsync(now, cancellationToken))
            {
                var polling = record.Result.Receipt is not null;
                if (!polling && !await store.ClaimAsync(record.Notification.Id, cancellationToken)) continue;
                var result = polling ? await sender.PollAsync(record.Result.Receipt!, cancellationToken) :
                    await sender.SendAsync(record.Notification, cancellationToken);
                // A receipt lookup failure must not turn into a duplicate emergency send.
                if (polling && result.State is DeliveryState.Failed or DeliveryState.Disabled)
                    result = new(DeliveryState.PendingAcknowledgement, result.Code, record.Result.Receipt);
                if (polling && now > record.CreatedAtUtc.AddDays(1) && result.State != DeliveryState.Acknowledged)
                    result = new(DeliveryState.Expired, "receipt_review_required", record.Result.Receipt);
                await store.CompleteAsync(record.Notification.Id, result, now, cancellationToken);
                _notificationFailed=await store.HasFailuresAsync(cancellationToken);
            }
            _notificationFailed=await store.HasFailuresAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { _notificationFailed = true; } // Fixed health state; never exception messages or HTTP payloads.
    }
    public async ValueTask RunHeartbeatAsync(bool monitoringDegraded,CancellationToken cancellationToken)
    {
        try
        {
            var result = await heartbeat.SendAsync(new(monitoringDegraded || _notificationFailed), cancellationToken);
            _heartbeatFailed=result.State == DeliveryState.Failed;
            if(monitoringStore is not null)
                await monitoringStore.SaveServiceHealthAsync(new("outbound-heartbeat",DateTimeOffset.UtcNow,
                    result.State.ToString(),JsonSerializer.Serialize(new{result.State,result.Code})),cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { _heartbeatFailed = true; }
    }
}
