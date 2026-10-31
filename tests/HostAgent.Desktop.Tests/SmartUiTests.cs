using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Desktop;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Collectors;
using HostAgent.Infrastructure.Dashboard;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class SmartUiTests
{
    [Theory]
    [InlineData("unavailable", "SmartPredictionUnavailable")]
    [InlineData("FailurePredicted", "SmartFailurePredicted")]
    [InlineData("NoFailurePredicted", "SmartNoFailurePredicted")]
    public Task Smart_result_is_visible_and_translates_without_repeating_the_probe(string state, string key) => StaTest.RunAsync(() =>
    {
        DiskHealthTelemetry[] values = state == "unavailable" ? new DiskHealthTelemetry[0] : [new DiskHealthTelemetry("PhysicalDrive0", state, null)];
        var reader = new Reader(values);
        var language = TestLocalization.Czech();
        var controller = new DashboardController((_, _) => ValueTask.FromResult(AdminProtocol.Reply(false, "view_unavailable")));
        var window = new MainWindow(controller, language, reader);
        try
        {
            window.Show();
            ((Button)window.FindName("SmartReadButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var text = (TextBlock)window.FindName("SmartResultText");
            Assert.Equal(language.Text(key, "PhysicalDrive0"), text.Text);
            language.SelectLanguage("de");
            Assert.Equal(language.Text(key, "PhysicalDrive0"), text.Text);
            Assert.Equal(1, reader.Calls);
        }
        finally { window.Close(); }
    });

    private sealed class Reader(IReadOnlyList<DiskHealthTelemetry> values) : IDiskHealthReader
    {
        public int Calls { get; private set; }
        public ValueTask<IReadOnlyList<DiskHealthTelemetry>> ReadAsync(CancellationToken cancellationToken)
        { Calls++; return ValueTask.FromResult(values); }
    }
}
