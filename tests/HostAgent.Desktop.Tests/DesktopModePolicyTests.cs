using HostAgent.Desktop;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class DesktopModePolicyTests
{
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
        Assert.True(DesktopModePolicy.IsDeveloperTab(4));
        Assert.False(DesktopModePolicy.IsDeveloperTab(5));
        Assert.False(DesktopModePolicy.CanNavigateTo(4,DesktopMode.Standard));
        Assert.True(DesktopModePolicy.CanNavigateTo(5,DesktopMode.Standard));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Standard_read_only_dashboard_tabs_remain_available(int index)
        =>Assert.False(DesktopModePolicy.IsDeveloperTab(index));
}
