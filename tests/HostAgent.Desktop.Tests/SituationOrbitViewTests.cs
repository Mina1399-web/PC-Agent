using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HostAgent.Core.Dashboard;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class SituationOrbitViewTests
{
    [Fact]
    public Task Compact_detail_can_be_collapsed_and_reopened_without_a_viewport_change() => WithOrbit((window, orbit) =>
    {
        window.Width = 850;
        window.Height = 620;
        window.UpdateLayout();

        var detail = (Border)orbit.FindName("Detail");
        var content = (StackPanel)orbit.FindName("DetailContent");
        var toggle = (Button)orbit.FindName("CloseDetail");
        Assert.Equal(1, Grid.GetRow(detail));
        Assert.Equal(Visibility.Visible, detail.Visibility);
        Assert.Equal(Visibility.Visible, content.Visibility);

        Click(toggle);
        window.UpdateLayout();
        Assert.Equal(Visibility.Visible, detail.Visibility);
        Assert.Equal(Visibility.Collapsed, content.Visibility);
        Assert.Equal("Rozbalit detail", AutomationProperties.GetName(toggle));
        Assert.True(toggle.IsKeyboardFocused);

        Click(toggle);
        window.UpdateLayout();
        Assert.Equal(Visibility.Visible, detail.Visibility);
        Assert.Equal(Visibility.Visible, content.Visibility);
        Assert.Equal("Sbalit detail", AutomationProperties.GetName(toggle));
        Assert.True(toggle.IsKeyboardFocused);
    });

    [Fact]
    public Task Wide_detail_hides_and_reopens_when_the_selected_orbit_node_is_clicked() => WithOrbit((window, orbit) =>
    {
        window.Width = 1280;
        window.Height = 800;
        window.UpdateLayout();

        // The DEV VM's 200% scaling clamps top-level windows below this
        // control breakpoint. Give the control an explicit wide test size so
        // this test exercises the wide branch independently of screen size.
        var wideControlSize = new Size(1280, 800);
        orbit.Width = wideControlSize.Width;
        orbit.Height = wideControlSize.Height;
        window.UpdateLayout();
        var body = (Grid)orbit.FindName("Body");
        Console.WriteLine($"WIDE_LAYOUT window={window.ActualWidth}x{window.ActualHeight}; body={body.ActualWidth}x{body.ActualHeight}; WPF work area={SystemParameters.WorkArea.Width}x{SystemParameters.WorkArea.Height}; requested control={wideControlSize.Width}x{wideControlSize.Height}.");
        Assert.True(body.ActualWidth >= 940, $"Wide layout precondition failed: window={window.ActualWidth}x{window.ActualHeight}; body={body.ActualWidth}x{body.ActualHeight}; WPF work area={SystemParameters.WorkArea.Width}x{SystemParameters.WorkArea.Height}.");

        var detail = (Border)orbit.FindName("Detail");
        var close = (Button)orbit.FindName("CloseDetail");
        var cpu = GetNode(orbit, "Cpu");
        var detailColumn = (ColumnDefinition)orbit.FindName("DetailColumn");
        Assert.Equal(1, Grid.GetColumn(detail));
        Assert.Equal(Visibility.Visible, detail.Visibility);
        Assert.True(detailColumn.ActualWidth > 0);

        Click(close);
        window.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, detail.Visibility);
        Assert.Equal(0, detailColumn.ActualWidth);
        Assert.True(cpu.IsKeyboardFocused);

        Click(cpu);
        window.UpdateLayout();
        Assert.Equal(Visibility.Visible, detail.Visibility);
        Assert.True(detailColumn.ActualWidth > 0);
    });

    [Fact]
    public Task Escape_collapses_compact_detail_and_keeps_focus_on_the_visible_toggle() => WithOrbit((window, orbit) =>
    {
        window.Width = 850;
        window.Height = 620;
        window.UpdateLayout();
        var content = (StackPanel)orbit.FindName("DetailContent");
        var toggle = (Button)orbit.FindName("CloseDetail");
        Assert.Equal(Visibility.Visible, content.Visibility);

        var source = PresentationSource.FromVisual(orbit);
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        orbit.RaiseEvent(key);
        window.UpdateLayout();

        Assert.Equal(Visibility.Collapsed, content.Visibility);
        Assert.True(toggle.IsKeyboardFocused);
        Assert.True(toggle.IsVisible);
        Assert.True(key.Handled);
    });

    [Fact]
    public Task Viewport_transition_from_a_collapsed_compact_drawer_moves_focus_to_a_visible_orbit_node() => WithOrbit((window, orbit) =>
    {
        window.Width = 850;
        window.Height = 620;
        window.UpdateLayout();
        var toggle = (Button)orbit.FindName("CloseDetail");
        var detail = (Border)orbit.FindName("Detail");
        Click(toggle);
        Assert.Equal(Visibility.Collapsed, ((StackPanel)orbit.FindName("DetailContent")).Visibility);
        Assert.True(toggle.IsKeyboardFocused);

        window.Width = 1280;
        var wideControlSize = new Size(1280, 800);
        orbit.Width = wideControlSize.Width;
        orbit.Height = wideControlSize.Height;
        window.UpdateLayout();
        var body = (Grid)orbit.FindName("Body");
        Console.WriteLine($"VIEWPORT_TRANSITION window={window.ActualWidth}x{window.ActualHeight}; body={body.ActualWidth}x{body.ActualHeight}; WPF work area={SystemParameters.WorkArea.Width}x{SystemParameters.WorkArea.Height}; requested control={wideControlSize.Width}x{wideControlSize.Height}.");
        Assert.True(body.ActualWidth >= 940, $"Wide layout precondition failed: window={window.ActualWidth}x{window.ActualHeight}; body={body.ActualWidth}x{body.ActualHeight}; WPF work area={SystemParameters.WorkArea.Width}x{SystemParameters.WorkArea.Height}.");
        var cpu = GetNode(orbit, "Cpu");
        Assert.Equal(Visibility.Collapsed, detail.Visibility);
        Assert.True(cpu.IsVisible);
        Assert.True(cpu.IsKeyboardFocused);
    });

    private static Task WithOrbit(Action<Window, SituationOrbitView> action) => StaTest.RunAsync(() =>
    {
        Window? window = null;
        try
        {
            var localization = TestLocalization.Czech();
            var now = DateTimeOffset.UtcNow;
            var overview = new DashboardSnapshot(now, "Shadow", true, false,
                new(now, 21), new(now, 58), null, [], [], [], null, [], [], [], []);
            var controller = new DashboardController((payload, _) =>
            {
                var request = ViewProtocol.Parse(payload);
                return ValueTask.FromResult(request.Command switch
                {
                    "overview" => AdminProtocol.Reply(true, "overview", overview),
                    "history" => AdminProtocol.Reply(true, "history", Array.Empty<ViewMetric>()),
                    _ => AdminProtocol.Reply(false, "view_unavailable")
                });
            });
            window = new MainWindow(controller, localization);
            window.Width = 1280;
            window.Height = 800;
            window.Left = 0;
            window.Top = 0;
            window.Show();
            PumpUntil(() => ((TextBlock)window.FindName("CpuText")).Text.Contains("21", StringComparison.Ordinal));
            var orbit = (SituationOrbitView)window.FindName("SituationOrbit");
            orbit.Render(SituationOrbitSnapshot.Create(overview, "connected", true, localization, now));
            window.UpdateLayout();
            action(window, orbit);
        }
        finally { window?.Close(); }
    });

    private static Button GetNode(SituationOrbitView orbit, string name)
    {
        var id = Enum.Parse<SituationSubsystem>(name);
        return ((Grid)orbit.FindName("Map")).Children.OfType<Button>()
            .Single(button => button.Tag is SituationSubsystem node && node == id);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
        Assert.True(condition());
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
