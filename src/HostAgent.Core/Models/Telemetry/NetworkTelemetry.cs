namespace HostAgent.Core.Models.Telemetry;

public sealed record NetworkTelemetry(
    IReadOnlyList<NetworkAdapterTelemetry> Adapters,
    IReadOnlyList<NetworkProbeResult> Probes);

public sealed record NetworkAdapterTelemetry(
    string AdapterId,
    string Name,
    string Description,
    string Status,
    IReadOnlyList<string> IpAddresses,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string> DnsServers);

public enum NetworkProbeKind
{
    Gateway,
    InternetIp,
    DnsResolution
}

public enum NetworkProbeStatus
{
    Succeeded,
    Failed,
    TimedOut,
    Unavailable
}

public sealed record NetworkProbeResult(
    NetworkProbeKind Kind,
    string Target,
    NetworkProbeStatus Status,
    TimeSpan Duration,
    double? LatencyMilliseconds,
    double? PacketLossPercent,
    string? FailureCode);
