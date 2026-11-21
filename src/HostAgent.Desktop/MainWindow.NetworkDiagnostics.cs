using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using HostAgent.Core.Dashboard;

namespace HostAgent.Desktop;

public partial class MainWindow
{
    private ViewNetworkDiagnostics? _networkDiagnostic;
    private int _networkDiagnosticVersion;
    private bool _networkDiagnosticRequested, _networkDiagnosticLoading;

    private async void NetworkDiagnosticsClick(object sender, RoutedEventArgs e) => await LoadNetworkDiagnostics();

    private async Task LoadNetworkDiagnostics()
    {
        var version = ++_networkDiagnosticVersion;
        _networkDiagnosticRequested = _networkDiagnosticLoading = true;
        _networkDiagnostic = null;
        RenderNetworkDiagnostics();
        ViewNetworkDiagnostics? response = null;
        try
        {
            response = await _controller.ReadAsync<ViewNetworkDiagnostics>("network-diagnostics", new { }, _closing.Token);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { return; }
        if (_closing.IsCancellationRequested || version != _networkDiagnosticVersion) return;
        if (response is { Configuration: not null, Adapters.Count: <= 16, Probes.Count: 3 }
            && response.Adapters.All(row => row is not null)
            && response.Probes.All(probe => probe is { State: not null }))
            _networkDiagnostic = response;
        _networkDiagnosticLoading = false;
        RenderNetworkDiagnostics();
    }

    private void ClearNetworkDiagnostics()
    {
        _networkDiagnosticVersion++;
        _networkDiagnostic = null;
        _networkDiagnosticLoading = false;
        _networkDiagnosticRequested = false;
    }

    private void RenderNetworkDiagnostics()
    {
        if (_networkDiagnostic is not { } report)
        {
            NetworkDiagnosticsText.Text = !_networkDiagnosticRequested ? "" :
                T(_networkDiagnosticLoading ? "NetDetailLoading" : "NetDetailUnavailable");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var state = SituationSubsystemView.CurrentState(report.Configuration, now);
        var lines = new List<string> { T("NetDetailPassive"), DataTrustDisplay.Describe(Localization, state) };
        if (state?.Status is "available" or "partial")
        {
            if (report.Adapters.Count == 0 && state.Status == "available") lines.Add(T("NetDetailEmpty"));
            for (var i = 0; i < report.Adapters.Count; i++)
            {
                var row = report.Adapters[i];
                var status = row.Status switch
                {
                    "Up" => "NetDetailUp", "Down" or "LowerLayerDown" => "NetDetailDown",
                    "Dormant" => "NetDetailDormant", "Testing" => "NetDetailTesting",
                    "NotPresent" => "NetDetailAbsent", _ => "DataTrustUnknown"
                };
                lines.Add(T("NetDetailAdapter", i + 1, T(status), Count(row.GatewayCount), Count(row.DnsCount)));
            }
            if (report.AdapterLimitReached) lines.Add(T("NetDetailLimited"));
        }

        lines.Add("");
        lines.Add(T("NetDetailProbes"));
        lines.Add(T("NetDetailGatewayScope"));
        foreach (var probe in report.Probes)
        {
            var label = probe.Kind switch
            {
                "Gateway" => "NetDetailGateway", "InternetIp" => "NetDetailInternet", "DnsResolution" => "NetDetailDns", _ => null
            };
            if (label is null) continue;
            var current = SituationSubsystemView.CurrentState(probe.State, now);
            lines.Add(T(label));
            lines.Add(DataTrustDisplay.Describe(Localization, current));
            var reason = current?.Reason switch
            {
                "probe_disabled" => "NetDetailDisabled", "gateway_missing" => "NetDetailNoGateway",
                "response_loss" => "NetDetailPartialResponse", "read_timeout" => "NetDetailTimeout", _ => null
            };
            if (reason is not null) lines.Add(T(reason));
            if (current?.Status is ("available" or "partial") && probe.Milliseconds is { } milliseconds)
                lines.Add(T(probe.Kind == "DnsResolution" ? "NetDetailElapsed" : "NetDetailRtt", milliseconds));
            if (current?.AtUtc is { } at && ViewFreshness.IsFresh(at, now) &&
                probe.Kind != "DnsResolution" && probe.PacketLossPercent is { } loss)
                lines.Add(T("NetDetailLoss", loss));
        }

        NetworkDiagnosticsText.Text = string.Join("\n", lines);
        string Count(int? count) => count?.ToString(Localization.Culture) ?? T("DataTrustUnknown");
    }
}
