using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HostAgent.Desktop;
using HostAgent.Core.Dashboard;
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
            Assert.Equal("Other", Assert.Single(grid.Items.OfType<WindowsInventoryRow>()).Name);
            search.Clear();
            ((ComboBox)window.FindName("WindowsInventoryArea")).SelectedIndex = 1;
            PumpUntil(() => grid.Items.Count == 1 && grid.Items[0] is WindowsInventoryRow { Name: "HostAgent" });
            language.SelectLanguage("de");
            Assert.Equal("Gestartet", Assert.Single(grid.Items.OfType<WindowsInventoryRow>()).State);
            var area=(ComboBox)window.FindName("WindowsInventoryArea");
            area.SelectedIndex=5;
            PumpUntil(()=>((TextBlock)window.FindName("WindowsAreaCapabilityText")).Text.Contains("Installation",StringComparison.OrdinalIgnoreCase));
            Assert.Contains("Installation",((TextBlock)window.FindName("WindowsAreaCapabilityText")).Text);
            area.SelectedIndex=6;
            PumpUntil(()=>((TextBlock)window.FindName("WindowsAreaCapabilityText")).Text.Contains("Autostarteinträge",StringComparison.OrdinalIgnoreCase));
            Assert.Contains("Autostarteinträge",((TextBlock)window.FindName("WindowsAreaCapabilityText")).Text);
            area.SelectedIndex=1;
            PumpUntil(()=>grid.Items.Count==1&&grid.Items[0] is WindowsInventoryRow {Name:"HostAgent"});
            grid.SelectedIndex=0;
            var prepare=(Button)window.FindName("PrepareServiceActionButton");
            Assert.Equal(Visibility.Visible,prepare.Visibility);
            prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(4,((TabControl)window.FindName("DashboardTabs")).SelectedIndex);
            Assert.Equal("HostAgent",((TextBox)window.FindName("AliasInput")).Text);
            Assert.Equal(-1,((ComboBox)window.FindName("OperationChoice")).SelectedIndex);
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

    [Fact]
    public Task Findings_offer_cleanup_for_the_temp_volume_and_explain_dns_failure() => StaTest.RunAsync(() =>
    {
        var now=DateTimeOffset.UtcNow;
        var drive=Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))!.TrimEnd('\\');
        var view=new DashboardSnapshot(now,"Confirmation",false,false,null,null,null,
            [new(drive,5,100,now)],[],[],1,
            [new(Guid.NewGuid().ToString("N"),"Active","service_stopped",88,"High",now,1){AffectedService="Spooler"}],[],[],[])
        {
            NetworkChecks=new(now,true,[new("Gateway","Succeeded"),new("InternetIp","Succeeded"),new("DnsResolution","TimedOut")])
        };
        var dashboard=new DashboardController((_,_)=>ValueTask.FromResult(AdminProtocol.Reply(true,"overview",view)));
        var window=new MainWindow(dashboard,TestLocalization.Czech(),null,_=>new([]));
        try
        {
            window.Show();
            var finding=(TextBlock)window.FindName("WindowsDiagnosis");
            PumpUntil(()=>finding.Text.Contains("překlad názvů selhal",StringComparison.Ordinal));
            Assert.Contains("Samotné zastavení neprokazuje pád",finding.Text);
            Assert.Equal(Visibility.Visible,((Button)window.FindName("DiskCleanupAction")).Visibility);
            Assert.Equal(Visibility.Collapsed,((Button)window.FindName("ServiceRecoveryAction")).Visibility);
            Assert.Equal("Vypnout kontroly sítě",((Button)window.FindName("NetworkProbesAction")).Content);
        }
        finally{window.Close();}
    });
}
