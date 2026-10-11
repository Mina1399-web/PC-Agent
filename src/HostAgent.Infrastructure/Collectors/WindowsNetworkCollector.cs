using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Infrastructure.Collectors;

public sealed class WindowsNetworkCollector : ITelemetryCollector<NetworkTelemetry>
{
    private const string InternetProbeAddress = "1.1.1.1";
    private const string DnsProbeHost = "example.com";
    private static readonly TimeSpan PingTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(2);
    private const int DiagnosticProbeCount = 3;

    public string CollectorId => "network";

    public async ValueTask<CollectorResult<NetworkTelemetry>> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var warnings = new List<string>();
        IReadOnlyList<NetworkAdapterTelemetry> adapters;
        try
        {
            adapters = ReadAdapters(warnings);
        }
        catch (NetworkInformationException)
        {
            return CollectorResult<NetworkTelemetry>.Unavailable(CollectorId, now, "network_adapters_unavailable");
        }

        var probes = new List<NetworkProbeResult>();
        var gateway = adapters.FirstOrDefault(static adapter =>
                string.Equals(adapter.Status, OperationalStatus.Up.ToString(), StringComparison.Ordinal)
                && adapter.Gateways.Count > 0)
            ?.Gateways.FirstOrDefault();

        probes.Add(gateway is null
            ? new NetworkProbeResult(
                NetworkProbeKind.Gateway,
                string.Empty,
                NetworkProbeStatus.Unavailable,
                TimeSpan.Zero,
                null,
                null,
                "gateway_not_configured")
            : await ProbeAddressAsync(
                NetworkProbeKind.Gateway,
                gateway,
                cancellationToken));
        probes.Add(await ProbeAddressAsync(
            NetworkProbeKind.InternetIp,
            InternetProbeAddress,
            cancellationToken));
        probes.Add(await ProbeDnsAsync(DnsProbeHost, cancellationToken));

        var telemetry = new NetworkTelemetry(adapters, probes.AsReadOnly());
        return warnings.Count == 0
            ? CollectorResult<NetworkTelemetry>.Success(CollectorId, now, telemetry)
            : CollectorResult<NetworkTelemetry>.Partial(CollectorId, now, telemetry, warnings);
    }

    private static IReadOnlyList<NetworkAdapterTelemetry> ReadAdapters(ICollection<string> warnings)
    {
        var adapters = new List<NetworkAdapterTelemetry>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            try
            {
                var properties = networkInterface.GetIPProperties();
                adapters.Add(new NetworkAdapterTelemetry(
                    networkInterface.Id,
                    networkInterface.Name,
                    networkInterface.Description,
                    networkInterface.OperationalStatus.ToString(),
                    properties.UnicastAddresses
                        .Select(static address => address.Address.ToString())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    properties.GatewayAddresses
                        .Select(static gateway => gateway.Address)
                        .Where(static address => !IPAddress.Any.Equals(address) && !IPAddress.IPv6Any.Equals(address))
                        .Select(static address => address.ToString())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    properties.DnsAddresses
                        .Select(static address => address.ToString())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()));
            }
            catch (NetworkInformationException)
            {
                warnings.Add("some_network_adapter_details_unavailable");
            }
        }

        if (adapters.Count == 0)
        {
            warnings.Add("no_network_adapters_available");
        }

        return adapters.AsReadOnly();
    }

    private static async ValueTask<NetworkProbeResult> ProbeAddressAsync(
        NetworkProbeKind kind,
        string target,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;
        var successes = 0;
        var timedOut = 0;
        var failures = 0;
        var latencyTotal = 0d;
        if (!IPAddress.TryParse(target, out var address))
        {
            return new NetworkProbeResult(
                kind,
                target,
                NetworkProbeStatus.Unavailable,
                stopwatch.Elapsed,
                null,
                null,
                "invalid_ip_address");
        }

        for (var index = 0; index < DiagnosticProbeCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            using var ping = new Ping();
            try
            {
                var reply = await ping.SendPingAsync(address, (int)PingTimeout.TotalMilliseconds)
                    .WaitAsync(PingTimeout, cancellationToken);
                if (reply.Status == IPStatus.Success)
                {
                    successes++;
                    latencyTotal += reply.RoundtripTime;
                }
                else if (reply.Status == IPStatus.TimedOut)
                {
                    timedOut++;
                }
            }
            catch (TimeoutException)
            {
                timedOut++;
            }
            catch (PingException)
            {
                failures++;
            }
            catch (SocketException)
            {
                failures++;
            }
        }

        stopwatch.Stop();
        var loss = 100d * (attempts - successes) / attempts;
        var status = successes > 0
            ? NetworkProbeStatus.Succeeded
            : timedOut == attempts
                ? NetworkProbeStatus.TimedOut
                : NetworkProbeStatus.Failed;
        return new NetworkProbeResult(
            kind,
            target,
            status,
            stopwatch.Elapsed,
            successes == 0 ? null : latencyTotal / successes,
            loss,
            successes > 0 ? null : status == NetworkProbeStatus.TimedOut
                ? "probe_timed_out"
                : failures > 0 ? "probe_failed" : "probe_unavailable");
    }

    private static async ValueTask<NetworkProbeResult> ProbeDnsAsync(
        string hostName,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            _ = await Dns.GetHostAddressesAsync(hostName, cancellationToken)
                .WaitAsync(DnsTimeout, cancellationToken);
            stopwatch.Stop();
            return new NetworkProbeResult(
                NetworkProbeKind.DnsResolution,
                hostName,
                NetworkProbeStatus.Succeeded,
                stopwatch.Elapsed,
                stopwatch.Elapsed.TotalMilliseconds,
                null,
                null);
        }
        catch (TimeoutException)
        {
            stopwatch.Stop();
            return new NetworkProbeResult(
                NetworkProbeKind.DnsResolution,
                hostName,
                NetworkProbeStatus.TimedOut,
                stopwatch.Elapsed,
                null,
                null,
                "dns_timed_out");
        }
        catch (SocketException)
        {
            stopwatch.Stop();
            return new NetworkProbeResult(
                NetworkProbeKind.DnsResolution,
                hostName,
                NetworkProbeStatus.Failed,
                stopwatch.Elapsed,
                null,
                null,
                "dns_resolution_failed");
        }
    }
}
