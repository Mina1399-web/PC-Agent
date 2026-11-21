using HostAgent.Desktop;
using System.Windows.Input;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class DesktopModePolicyTests
{
    [Theory]
    [InlineData(Key.D1, DesktopRoute.Situation)]
    [InlineData(Key.D2, DesktopRoute.Events)]
    [InlineData(Key.D3, DesktopRoute.Recommendations)]
    [InlineData(Key.D4, DesktopRoute.System)]
    [InlineData(Key.D5, DesktopRoute.Maintenance)]
    [InlineData(Key.D6, DesktopRoute.WindowsTools)]
    public void Control_digit_routes_are_independent_of_tab_order(Key key, DesktopRoute route)
    {
        Assert.Equal<DesktopRoute?>(route, MainWindow.RouteForShortcut(key, ModifierKeys.Control));
        Assert.Null(MainWindow.RouteForShortcut(key, ModifierKeys.None));
    }

    [Theory]
    [InlineData(DesktopMode.Standard)]
    [InlineData(DesktopMode.Developer)]
    public void Routes_fail_closed_and_tab_keys_stay_with_Wpf(DesktopMode mode)
    {
        Assert.False(DesktopModePolicy.CanNavigateTo(null, mode));
        Assert.False(DesktopModePolicy.CanNavigateTo((DesktopRoute)999, mode));
        Assert.Null(MainWindow.RouteForShortcut(Key.Tab, ModifierKeys.None));
        Assert.Null(MainWindow.RouteForShortcut(Key.Tab, ModifierKeys.Shift));
        Assert.True(MainWindow.IsRefreshShortcut(Key.F5, ModifierKeys.None));
        Assert.False(MainWindow.IsRefreshShortcut(Key.F5, ModifierKeys.Control));
    }

    [Theory]
    [InlineData(true,false,false)]
    [InlineData(false,true,false)]
    [InlineData(false,false,true)]
    public void A_pending_cli_guided_action_or_maintenance_window_blocks_mode_change(
        bool cliPending,bool guidedAction,bool maintenanceOpen)
        =>Assert.False(DesktopModePolicy.CanChangeMode(cliPending,guidedAction,maintenanceOpen));

    [Fact]
    public void Mode_can_change_when_no_action_is_in_flight()
        =>Assert.True(DesktopModePolicy.CanChangeMode(false,false,false));

    [Fact]
    public void Maintenance_is_developer_only_but_windows_inventory_is_readable_in_standard_mode()
    {
        Assert.True(DesktopModePolicy.IsDeveloperTab(DesktopRoute.Maintenance));
        Assert.False(DesktopModePolicy.IsDeveloperTab(DesktopRoute.WindowsTools));
        Assert.False(DesktopModePolicy.CanNavigateTo(DesktopRoute.Maintenance,DesktopMode.Standard));
        Assert.True(DesktopModePolicy.CanNavigateTo(DesktopRoute.WindowsTools,DesktopMode.Standard));
    }

    [Theory]
    [InlineData(DesktopRoute.Situation)]
    [InlineData(DesktopRoute.Events)]
    [InlineData(DesktopRoute.Recommendations)]
    [InlineData(DesktopRoute.System)]
    public void Standard_read_only_dashboard_tabs_remain_available(DesktopRoute route)
        =>Assert.False(DesktopModePolicy.IsDeveloperTab(route));
}
