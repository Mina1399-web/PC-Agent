using HostAgent.Core.Dashboard;
using HostAgent.Core.Models.Telemetry;
using System.Net.NetworkInformation;

namespace HostAgent.Service;

public sealed partial class DashboardDispatcher
{
    private const int NetworkAdapterLimit = 16;

    private async ValueTask<ViewNetworkDiagnostics> ReadNetworkDiagnostics(DateTimeOffset now, CancellationToken token)
    {
        var sample = await Latest<NetworkTelemetry>("network", now, token);
        var configuration = sample.Describe("network");
        var adapters = sample.Value?.Adapters;
        var rows = new List<ViewNetworkAdapter>(NetworkAdapterLimit);
        if (adapters is not null)
        {
            foreach (var adapter in adapters.Take(NetworkAdapterLimit))
            {
                if (adapter is null)
                {
                    configuration = configuration with { Status = "partial", Reason = "incomplete_sample" };
                    continue;
                }

                var status = Enum.TryParse<OperationalStatus>(adapter.Status, out var parsed) && Enum.IsDefined(parsed)
                    ? parsed : OperationalStatus.Unknown;
                if (status == OperationalStatus.Unknown || adapter.Gateways is null || adapter.DnsServers is null)
                    configuration = configuration with { Status = "partial", Reason = "incomplete_sample" };
                rows.Add(new(status.ToString(), adapter.Gateways?.Count, adapter.DnsServers?.Count));
            }
            if (adapters.Count > NetworkAdapterLimit)
                configuration = configuration with { Status = "partial", Reason = "incomplete_sample" };
            if (sample.HasWarning("some_network_adapter_details_access_denied"))
                configuration = configuration with { Status = "partial", Reason = "access_denied" };
        }
        else if (sample.Value is not null)
        {
            configuration = configuration with { Status = "unknown", Reason = "missing_sample" };
        }

        return new(configuration, rows, adapters?.Count > NetworkAdapterLimit,
            Enum.GetValues<NetworkProbeKind>().Select(kind => NetworkProbe(sample, kind)).ToArray());
    }

    private static ViewNetworkDiagnosticProbe NetworkProbe(SourceSample<NetworkTelemetry> sample, NetworkProbeKind kind)
    {
        var source = kind == NetworkProbeKind.DnsResolution ? "DNS" : "ICMP";
        var state = sample.Describe(source);
        ViewNetworkDiagnosticProbe Result(ViewDataState data, double? milliseconds = null, double? loss = null) =>
            new(kind.ToString(), data, milliseconds, loss);

        if (sample.Value is null) return Result(state);
        var matches = sample.Value.Probes?.Where(item => item?.Kind == kind).Take(2).ToArray();
        if (matches is not [var probe] || probe is null)
            return Result(state with { Status = "unknown", Reason = matches?.Length > 1 ? "invalid_sample" : "missing_sample" });

        if (probe.Status == NetworkProbeStatus.Succeeded)
        {
            var elapsed = probe.LatencyMilliseconds is >= 0 and < double.PositiveInfinity ? probe.LatencyMilliseconds : null;
            var loss = kind != NetworkProbeKind.DnsResolution && probe.PacketLossPercent is >= 0 and < 100
                ? probe.PacketLossPercent : null;
            if (elapsed is null || (kind != NetworkProbeKind.DnsResolution && loss is null))
                return Result(state with { Status = "partial", Reason = "invalid_sample" }, elapsed, loss);
            return Result(state with { Status = loss > 0 ? "partial" : "available", Reason = loss > 0 ? "response_loss" : null },
                elapsed, loss);
        }

        var reason = probe.FailureCode switch
        {
            "network_probes_disabled" => "probe_disabled",
            "gateway_not_configured" => "gateway_missing",
            "access_denied" or "network_adapters_access_denied" => "access_denied",
            "invalid_ip_address" => "invalid_sample",
            _ when probe.Status == NetworkProbeStatus.TimedOut => "read_timeout",
            _ => "collector_unavailable"
        };
        var measuredLoss = kind != NetworkProbeKind.DnsResolution
            && probe.Status is (NetworkProbeStatus.Failed or NetworkProbeStatus.TimedOut)
            && probe.PacketLossPercent == 100 ? probe.PacketLossPercent : null;
        return Result(state with { Status = Enum.IsDefined(probe.Status) ? "unavailable" : "unknown", Reason = reason },
            loss: measuredLoss);
    }
}
