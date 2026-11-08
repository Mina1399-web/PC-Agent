using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using HostAgent.Desktop;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class WindowsAdministrationTests
{
    [Fact]
    public void Privileged_consoles_use_the_system_mmc_with_a_fixed_local_snap_in()
    {
        var info = WindowsAdministration.CreateStartInfo(WindowsTool.Services);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "mmc.exe"), info.FileName);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "services.msc"), Assert.Single(info.ArgumentList));
        Assert.True(info.UseShellExecute);
        Assert.Equal("runas", info.Verb);
        Assert.Empty(info.Arguments);

        var eventViewer = WindowsAdministration.CreateStartInfo(WindowsTool.EventViewer);
        Assert.Empty(eventViewer.Verb);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "eventvwr.msc"), Assert.Single(eventViewer.ArgumentList));
    }

    [Fact]
    public void Settings_pages_do_not_request_elevation_and_unknown_tools_are_rejected()
    {
        var info = WindowsAdministration.CreateStartInfo(WindowsTool.UpdateHistory);
        Assert.Equal("ms-settings:windowsupdate-history", info.FileName);
        Assert.Empty(info.ArgumentList);
        Assert.Empty(info.Verb);
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowsAdministration.CreateStartInfo((WindowsTool)999));
    }

    [Fact]
    public Task Management_stays_available_when_monitor_is_offline_and_reports_launch_outcomes() => StaTest.RunAsync(() =>
    {
        var dashboard = new DashboardController((_, _) => ValueTask.FromResult(AdminProtocol.Reply(false, "unavailable")));
        var language = TestLocalization.Czech();
        var opened = new List<WindowsTool>();
        var window = new MainWindow(dashboard, language, null, tool =>
        {
            opened.Add(tool);
            if (tool == WindowsTool.Services) throw new Win32Exception(1223);
            if (tool == WindowsTool.DeviceManager) throw new Win32Exception(2);
        });
        try
        {
            window.Show();
            ((TabItem)window.FindName("WindowsAdministrationTab")).IsSelected = true;
            window.UpdateLayout();
            var panel = (StackPanel)window.FindName("WindowsToolsPanel");
            var buttons = panel.Children.OfType<Border>().SelectMany(card =>
                ((StackPanel)card.Child).Children.OfType<WrapPanel>().Single().Children.OfType<Button>()).ToArray();
            Assert.Equal(WindowsAdministration.Entries.Count, buttons.Length);
            Assert.Empty(opened);
            Assert.Null(dashboard.Snapshot);
            var status = (TextBlock)window.FindName("WindowsToolStatus");
            Button ButtonFor(WindowsTool tool) => buttons.Single(button => Equals(button.Tag, tool));
            void Click(WindowsTool tool) => ButtonFor(tool).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Správce úloh", ButtonFor(WindowsTool.TaskManager).Content);
            Click(WindowsTool.TaskManager);
            Assert.Contains("Předáno systému Windows", status.Text);
            Click(WindowsTool.Services);
            Assert.Contains("zrušeno", status.Text);
            Click(WindowsTool.DeviceManager);
            Assert.Contains("nepodařilo otevřít", status.Text);
            language.SelectLanguage("de");
            Assert.Equal("Task-Manager", ButtonFor(WindowsTool.TaskManager).Content);
            Assert.Contains("Geräte-Manager", status.Text);
            Assert.Equal(new[] { WindowsTool.TaskManager, WindowsTool.Services, WindowsTool.DeviceManager }, opened);
        }
        finally { window.Close(); }
    });
}
