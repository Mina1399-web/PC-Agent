using HostAgent.Core.Contracts;
using HostAgent.Infrastructure.Notifications;
namespace HostAgent.Infrastructure.Heartbeat;
public sealed class HealthchecksHeartbeat(HttpClient http, ISecretStore secrets, TimeSpan timeout) : IHeartbeatSender
{
    public async ValueTask<DeliveryResult> SendAsync(AgentHealth health, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = OutboundHttp.Deadline(timeout, cancellationToken);
        try
        {
            var endpoint = await secrets.ReadAsync(SecretName.HeartbeatUrl, deadline.Token);
            if (endpoint?.Length > 2048 || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                return new(DeliveryState.Disabled, "heartbeat_not_configured");
            using var request = new HttpRequestMessage(HttpMethod.Post,
                uri.AbsoluteUri.TrimEnd('/') + (health.Degraded || health.Stopping ? "/fail" : "")) { Content = new StringContent("") };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            return response.IsSuccessStatusCode ? new(DeliveryState.Sent, "heartbeat_sent") : new(DeliveryState.Failed, "heartbeat_rejected");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
        { return new(DeliveryState.Failed, "heartbeat_unavailable"); }
    }
}
