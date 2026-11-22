using System;
using System.Linq;
using System.Windows;
using HostAgent.Core.Models.Inventory;

namespace HostAgent.Desktop;

public partial class MainWindow
{
    private DeviceHealthReport? _deviceHealth;
    private int _deviceHealthVersion;

    private async void ReadDeviceHealthClick(object sender, RoutedEventArgs e)
    {
        var version = ++_deviceHealthVersion;
        DeviceHealthRead.IsEnabled = false;
        try
        {
            var result = await _controller.ReadAsync<DeviceHealthReport>("device-health", new { }, _closing.Token);
            if (_closing.IsCancellationRequested || version != _deviceHealthVersion) return;
            _deviceHealth = result is { Coverage: not null, Findings.Count: <= 32 } ? result : null;
            RenderDeviceHealth();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        finally { if (!_closing.IsCancellationRequested) DeviceHealthRead.IsEnabled = true; }
    }

    private void RenderDeviceHealth()
    {
        if (DeviceHealthSummary is null) return;
        var report = _deviceHealth?.At(DateTimeOffset.UtcNow);
        var outcome = T(report?.Outcome switch
        {
            "healthy" => "DeviceHealthNoPnPProblem",
            "unhealthy" => "DeviceHealthPnPProblem",
            "access_denied" => "DeviceHealthDenied",
            "unavailable" => "DeviceHealthUnavailable",
            _ => "DeviceHealthUnknown"
        });
        DeviceHealthSummary.Text = report is null ? outcome :
            outcome + "\n" + T("DeviceHealthChecked", report.Checked, report.Enumerated) + "\n" +
            DataTrustDisplay.Describe(Localization, report.Coverage);
        if (report?.Coverage.Reason is "enumeration_limit" or "finding_limit")
            DeviceHealthSummary.Text += "\n" + T("DeviceHealthLimit");
        DeviceHealthFindings.ItemsSource = report?.Findings.Select(finding =>
            T("DeviceHealthFinding", finding.DeviceClass?.ToString("D") ?? "—",
                finding.ProblemCode?.ToString(Localization.Culture) ?? "—", T(finding.Description switch
                {
                    "disabled" => "DeviceHealthDisabled",
                    "unhealthy" => "DeviceHealthPnPProblem",
                    "access_denied" => "DeviceHealthDenied",
                    "unavailable" => "DeviceHealthUnavailable",
                    _ => "DeviceHealthUnknown"
                }))).ToArray();
    }
}
