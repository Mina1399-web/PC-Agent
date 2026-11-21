using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HostAgent.Core.Dashboard;
using HostAgent.Desktop;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class SituationSubsystemViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly ViewDataState Available = new("collector", Now, "available", null);

    [Theory]
    [InlineData("cs-CZ", false)]
    [InlineData("cs-CZ", true)]
    [InlineData("en", false)]
    [InlineData("en", true)]
    [InlineData("de", false)]
    [InlineData("de", true)]
    public Task Shared_detail_keeps_selection_and_publishes_controls_in_both_layouts(string language, bool dark) =>
        StaTest.RunAsync(() =>
        {
            using var preferences = new DesktopLocalizationTests.PreferencesFixture();
            var loc = new DesktopLocalization(preferences.Preferences, CultureInfo.GetCultureInfo(language));
            var view = new SituationSubsystemView();
            var window = new Window { Content = view, Width = 800, Height = 650, ShowInTaskbar = false };
            loc.Apply(window);
            OrbitTheme.SetPalette(window, dark);
            try
            {
                view.Render(Snapshot(), loc, Now);
                window.Show();
                window.UpdateLayout();
                var detail = Find<Border>(view, "Detail");
                Assert.Equal(Visibility.Collapsed, detail.Visibility);
                var map = Find<Panel>(view, "Map");
                var list = Find<Panel>(view, "SubsystemList");
                var peer = new WindowAutomationPeer(window);
                Assert.Contains(Descendants(peer), item => item.GetAutomationControlType() == AutomationControlType.RadioButton &&
                    item.GetAutomationId() == "MapMemory" && item.GetName() == loc.Text("RAM"));

                var memoryMap = Assert.IsType<RadioButton>(map.Children[1]);
                var memoryList = Assert.IsType<RadioButton>(list.Children[1]);
                var cpuList = Assert.IsType<RadioButton>(list.Children[0]);
                var memoryPeer = new RadioButtonAutomationPeer(memoryMap);
                Assert.IsAssignableFrom<ISelectionItemProvider>(memoryPeer.GetPattern(PatternInterface.SelectionItem)).Select();
                Assert.True(memoryList.IsChecked == true);
                Assert.Equal(loc.Text("RAM"), Text(view, "DetailTitle"));
                Assert.Contains("0 %", Text(view, "DetailValue"));
                Assert.Equal(1, Grid.GetColumn(detail));

                window.Width = 500;
                window.UpdateLayout();
                Assert.Same(detail, Find<Border>(view, "Detail"));
                Assert.Equal(0, Grid.GetColumn(detail));
                Assert.Equal(1, Grid.GetRow(detail));
                view.Render(null, loc, Now);
                Assert.True(memoryMap.IsChecked == true && memoryList.IsChecked == true);
                Assert.Equal(loc.Text("DataTrustUnknown"), Text(view, "DetailStatus"));
                Assert.Equal(loc.Text("SubsystemNoCurrentValue"), Text(view, "DetailValue"));
                view.Render(Snapshot(), loc, Now);
                cpuList.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(Assert.IsType<RadioButton>(map.Children[0]).IsChecked == true);
                Assert.Equal(loc.Text("Processor"), Text(view, "DetailTitle"));
                var toggle = Find<ToggleButton>(view, "ShowDetail");
                toggle.IsChecked = false;
                Assert.Equal(Visibility.Collapsed, detail.Visibility);
                toggle.IsChecked = true;
                Assert.Equal(Visibility.Visible, detail.Visibility);
                Assert.Equal(loc.Text("Processor"), Text(view, "DetailTitle"));
            }
            finally { window.Close(); }
        });

    [Fact]
    public Task Source_status_controls_values_empty_lists_and_provenance() => StaTest.RunAsync(() =>
    {
        var loc = TestLocalization.CzechStandard();
        var view = new SituationSubsystemView();
        var snapshot = Snapshot();
        var data = snapshot.DataState!;
        var missing = loc.Text("SubsystemNoCurrentValue");
        var valueName = "DetailValue";
        var statusName = "DetailStatus";

        view.Render(snapshot with { DataState = null }, loc, Now);
        Assert.Equal(loc.Text("DataTrustUnknown"), Text(view, statusName));
        Assert.Equal(missing, Text(view, valueName));
        view.Render(snapshot, loc, Now);
        Assert.Contains("0 %", Text(view, valueName));
        view.Render(snapshot with { Cpu = new(Now.AddSeconds(-1), 98) }, loc, Now);
        Assert.Equal(missing, Text(view, valueName));
        Assert.Contains(loc.Text("DataTrustInvalid"), Text(view, statusName));

        var denied = new ViewDataState("cpu-memory", Now.AddMinutes(-4), "unavailable", "access_denied");
        view.Render(snapshot with { DataState = data with { Cpu = denied } }, loc, Now);
        Assert.Contains(loc.Text("DataTrustAccessDenied"), Text(view, statusName));
        Assert.Contains("cpu-memory", Text(view, statusName));
        Assert.Equal(missing, Text(view, valueName));
        view.Render(snapshot with { DataState = data with { Cpu = Available with { AtUtc = Now.AddMinutes(-4) } } }, loc, Now);
        Assert.Contains(loc.Text("DataTrustStale"), Text(view, statusName));
        Assert.Equal(missing, Text(view, valueName));
        view.Render(snapshot with { DataState = data with { Cpu = Available with { AtUtc = Now.AddSeconds(1) } } }, loc, Now);
        Assert.Contains(loc.Text("DataTrustUnknown"), Text(view, statusName));
        Assert.Equal(missing, Text(view, valueName));

        var list = Find<Panel>(view, "SubsystemList");
        ((RadioButton)list.Children[2]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        view.Render(snapshot, loc, Now);
        Assert.Equal(loc.Text("DataTrustEmpty"), Text(view, valueName));
        var partial = Available with { Status = "partial", Reason = "incomplete_sample" };
        view.Render(snapshot with { DataState = data with { Disks = partial } }, loc, Now);
        Assert.Contains(loc.Text("DataTrustPartial"), Text(view, statusName));
        Assert.Equal(missing, Text(view, valueName));
        view.Render(snapshot with { DataState = data with { Disks = partial }, Disks = [new("C:", 0, 1024, Now)] }, loc, Now);
        Assert.Contains(loc.Text("SubsystemExcerpt"), Text(view, valueName));
        Assert.Contains("C:", Text(view, valueName));
        Assert.Contains("0 B", Text(view, valueName));
        Assert.Contains("1 KiB", Text(view, valueName));
        ((RadioButton)list.Children[3]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(loc.Text("NetworkActiveAdaptersThisDoesNotVerifyInternetAccess", 0), Text(view, valueName));
        ((RadioButton)list.Children[4]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(loc.Text("DataTrustEmpty"), Text(view, valueName));
    });

    private static DashboardSnapshot Snapshot() => new(Now, "Shadow", true, false,
        new(Now, 0), new(Now, 0), null, [], [], [], 0, [], [], [], [])
    {
        DataState = new(Available, Available, Available, Available, Available, Available, Available)
    };

    private static T Find<T>(SituationSubsystemView view, string name) => Assert.IsAssignableFrom<T>(view.FindName(name));
    private static string Text(SituationSubsystemView view, string name) => Find<TextBlock>(view, name).Text;

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer) =>
        (peer.GetChildren() ?? []).SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
