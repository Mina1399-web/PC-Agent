using HostAgent.Core.Models.Diagnosis;
namespace HostAgent.Core.Contracts;

public sealed record Notification(string Id, string IncidentId, DiagnosisSeverity Severity, string Message);
public enum DeliveryState { Local, Sent, PendingAcknowledgement, Acknowledged, Expired, Failed, Uncertain, Disabled, Queued }
public sealed record DeliveryResult(DeliveryState State, string Code, string? Receipt = null);
public sealed record AgentHealth(bool Degraded, bool Stopping = false);
public interface INotificationSender
{
    ValueTask<DeliveryResult> SendAsync(Notification notification, CancellationToken cancellationToken);
    ValueTask<DeliveryResult> PollAsync(string receipt, CancellationToken cancellationToken);
}
public interface IHeartbeatSender
{
    ValueTask<DeliveryResult> SendAsync(AgentHealth health, CancellationToken cancellationToken);
}
public enum SecretName { PushoverToken, PushoverUser, HeartbeatUrl }
public interface ISecretStore
{
    ValueTask<string?> ReadAsync(SecretName name, CancellationToken cancellationToken);
}
public sealed record NotificationRecord(Notification Notification, DeliveryResult Result, int Attempts,
    DateTimeOffset CreatedAtUtc, DateTimeOffset NextAttemptAtUtc);
public interface INotificationStore
{
    ValueTask<bool> HasFailuresAsync(CancellationToken cancellationToken);
    ValueTask EnqueueAsync(Notification notification, DateTimeOffset now, CancellationToken cancellationToken);
    ValueTask<NotificationRecord?> ReadAsync(string id, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<NotificationRecord>> ReadDueAsync(DateTimeOffset now, CancellationToken cancellationToken);
    ValueTask<bool> ClaimAsync(string id, CancellationToken cancellationToken);
    ValueTask CompleteAsync(string id, DeliveryResult result, DateTimeOffset now, CancellationToken cancellationToken);
}
