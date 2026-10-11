using HostAgent.Core.Contracts;
namespace HostAgent.Infrastructure.Heartbeat;
public sealed class DisabledHeartbeat:IHeartbeatSender
{
    public ValueTask<DeliveryResult> SendAsync(AgentHealth health,CancellationToken cancellationToken)=>
        ValueTask.FromResult(new DeliveryResult(DeliveryState.Disabled,"heartbeat_not_configured"));
}
