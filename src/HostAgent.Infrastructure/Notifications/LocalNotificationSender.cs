using HostAgent.Core.Contracts;
namespace HostAgent.Infrastructure.Notifications;
public sealed class LocalNotificationSender:INotificationSender
{
    public ValueTask<DeliveryResult> SendAsync(Notification notification,CancellationToken cancellationToken)=>
        ValueTask.FromResult(new DeliveryResult(DeliveryState.Local,"local_only"));
    public ValueTask<DeliveryResult> PollAsync(string receipt,CancellationToken cancellationToken)=>
        ValueTask.FromResult(new DeliveryResult(DeliveryState.Disabled,"push_not_configured"));
}
