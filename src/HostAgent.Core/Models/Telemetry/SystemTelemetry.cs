namespace HostAgent.Core.Models.Telemetry;

public sealed record SystemTelemetry(
    string ProductName,
    string Version,
    string Build,
    DateTimeOffset? BootTimeUtc,
    TimeSpan Uptime,
    SystemLifecycleEvent? LastLifecycleEvent);

public sealed record SystemLifecycleEvent(
    int EventId,
    string Provider,
    DateTimeOffset? OccurredAtUtc);
