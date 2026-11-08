using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HostAgent.Desktop;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class WindowsAdministrationTests
{
    [Fact]
    public Task Inventory_is_visible_and_filterable_inside_the_dashboard_when_monitor_is_offline() => StaTest.RunAsync(() =>
    {
        var dashboard = new DashboardController((_, _) => ValueTask.FromResult(AdminProtocol.Reply(false, "unavailable")));
        var language = TestLocalization.Czech();
        var window = new MainWindow(dashboard, language, null, area => area switch
        {
            WindowsArea.Processes => new([new("Example", "42", "", "30 MiB"), new("Other", "57", "", "50 MiB")]),
            WindowsArea.Services => new([new("HostAgent", "Monitor", "Running", "")]),
            _ => new([])
        });
        try
        {
            window.Show();
            ((TabItem)window.FindName("WindowsAdministrationTab")).IsSelected = true;
            var grid = (DataGrid)window.FindName("WindowsInventoryGrid");
            PumpUntil(() => grid.Items.Count == 2);
            Assert.Null(dashboard.Snapshot);
            Assert.Contains("Monitor", ((TextBlock)window.FindName("WindowsDiagnosis")).Text);
            var search = (TextBox)window.FindName("WindowsInventorySearch");
            search.Text = "57";
            Assert.Equal("Other", Assert.IsType<WindowsInventoryRow>(Assert.Single(grid.Items)).Name);
            search.Clear();
            ((ComboBox)window.FindName("WindowsInventoryArea")).SelectedIndex = 1;
            PumpUntil(() => grid.Items.Count == 1 && grid.Items[0] is WindowsInventoryRow { Name: "HostAgent" });
            language.SelectLanguage("de");
            Assert.Equal("Gestartet", Assert.IsType<WindowsInventoryRow>(Assert.Single(grid.Items)).State);
        }
        finally { window.Close(); }
    });

    private static void PumpUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (!condition() && DateTime.UtcNow < until)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(20);
        }
        Assert.True(condition());
    }
}
