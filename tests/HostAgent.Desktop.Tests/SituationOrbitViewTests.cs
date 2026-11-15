using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using HostAgent.Infrastructure.Dashboard;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class SituationOrbitViewTests
{
    private static SituationOrbitSnapshot Snapshot() => new(
        "Situation", "Current signals", "Available", "Measured now", "Selected subsystem",
        [
            new(SituationSubsystem.Cpu, "Processor", "Available", "12 %", []),
            new(SituationSubsystem.Memory, "Memory", "Available", "54 %", []),
            new(SituationSubsystem.Disks, "Storage", "Available", "2 drives", []),
            new(SituationSubsystem.Network, "Network", "Limited", "1 adapter", ["Gateway responded"]),
            new(SituationSubsystem.Services, "Services", "Unavailable", "", [])
        ]);

    [Fact]
    public async Task Map_and_text_list_selection_update_the_main_context_panel()
    {
        await StaTest.RunAsync(() =>
        {
            var window = CreateMainWindow(1000, 700);
            try
            {
                var view = (SituationOrbitView)window.FindName("SituationOrbit")!;
                view.Render(Snapshot());
                var mapNodes = (Canvas)view.FindName("OrbitNodes")!;
                window.UpdateLayout();
                var mapButtons = mapNodes.Children.OfType<Button>().ToArray();
                Assert.Equal(5, mapButtons.Length);
                Assert.All(mapButtons, button =>
                {
                    Assert.True(double.IsFinite(Canvas.GetLeft(button)));
                    Assert.True(double.IsFinite(Canvas.GetTop(button)));
                });
                Assert.Equal(5, mapButtons.Select(button => (Canvas.GetLeft(button), Canvas.GetTop(button))).Distinct().Count());
                Assert.Contains(mapButtons, button => Canvas.GetLeft(button) != 0 || Canvas.GetTop(button) != 0);
                var nodeList = FindVisualDescendant<Expander>(view);
                Assert.NotNull(nodeList);
                nodeList!.IsExpanded = true;
                view.UpdateLayout();
                var textNodes = (WrapPanel)view.FindName("TextNodes")!;
                var mapNetwork = mapNodes.Children.OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button).StartsWith("Network,", StringComparison.Ordinal));
                var textMemory = textNodes.Children.OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button).StartsWith("Memory,", StringComparison.Ordinal));

                mapNetwork.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("Network", ((TextBlock)window.FindName("SelectedSubsystemTitle")!).Text);
                Assert.Equal("Limited", ((TextBlock)window.FindName("SelectedSubsystemStatus")!).Text);
                Assert.Equal(Visibility.Visible, ((TextBlock)window.FindName("NetworkText")!).Visibility);
                Assert.Contains("Gateway responded", ((ItemsControl)window.FindName("SelectedSubsystemRows")!).Items.Cast<string>());

                textMemory.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("Memory", ((TextBlock)window.FindName("SelectedSubsystemTitle")!).Text);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("HistoryPanel")!).Visibility);
                Assert.Equal(Visibility.Collapsed, ((TextBlock)window.FindName("NetworkText")!).Visibility);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Escape_clears_selection_and_returns_focus_to_its_orbit_node()
    {
        await StaTest.RunAsync(() =>
        {
            var view = new SituationOrbitView();
            var notifications = new System.Collections.Generic.List<SituationOrbitNode?>();
            view.SelectedNodeChanged += notifications.Add;
            view.Render(Snapshot());
            var window = Show(view, 900, 700);
            try
            {
                var nodeList = FindVisualDescendant<Expander>(view);
                Assert.NotNull(nodeList);
                nodeList!.IsExpanded = true;
                view.UpdateLayout();
                var textNodes = (WrapPanel)view.FindName("TextNodes")!;
                var origin = textNodes.Children.OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button).StartsWith("Network,", StringComparison.Ordinal));
                Assert.True(origin.Focus());
                origin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("Network", notifications.Last()?.Title);

                var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(view), 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                view.RaiseEvent(escape);

                Assert.Null(notifications.Last());
                Assert.Same(origin, Keyboard.FocusedElement);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Orbit_node_geometry_tracks_text_size_and_snapshot_order()
    {
        await StaTest.RunAsync(() =>
        {
            var window = CreateMainWindow(1000, 700);
            try
            {
                var view = (SituationOrbitView)window.FindName("SituationOrbit")!;
                var map = (Grid)view.FindName("Map")!;
                var mapNodes = (Canvas)view.FindName("OrbitNodes")!;
                var snapshot = Snapshot();
                view.Render(snapshot);
                window.UpdateLayout();

                var cpu = mapNodes.Children.OfType<Button>().Single(button => button.Tag is SituationSubsystem.Cpu);
                var initialHeight = cpu.ActualHeight;
                var reordered = snapshot.Nodes
                    .Select(node => node.Id == SituationSubsystem.Cpu
                        ? node with { Status = string.Join(" ", Enumerable.Repeat("Warning", 12)) }
                        : node)
                    .Reverse()
                    .ToArray();
                view.Render(snapshot with { Nodes = reordered });
                window.UpdateLayout();

                Assert.True(cpu.ActualHeight > initialHeight);
                var angles = new Dictionary<SituationSubsystem, double>
                {
                    [SituationSubsystem.Cpu] = -90,
                    [SituationSubsystem.Memory] = -18,
                    [SituationSubsystem.Disks] = 54,
                    [SituationSubsystem.Network] = 126,
                    [SituationSubsystem.Services] = 198
                };
                foreach (var button in mapNodes.Children.OfType<Button>())
                {
                    var angle = angles[(SituationSubsystem)button.Tag] * Math.PI / 180;
                    var centerX = Canvas.GetLeft(button) + button.ActualWidth / 2;
                    var centerY = Canvas.GetTop(button) + button.ActualHeight / 2;
                    var expectedX = map.ActualWidth / 2 + Math.Cos(angle) * (map.ActualWidth / 2 - 62);
                    var expectedY = map.ActualHeight / 2 + Math.Sin(angle) * (map.ActualHeight / 2 - 41);
                    Assert.InRange(Math.Abs(centerX - expectedX), 0, 1);
                    Assert.InRange(Math.Abs(centerY - expectedY), 0, 1);
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Situation_layout_keeps_detail_at_right_only_above_the_desktop_breakpoint()
    {
        await StaTest.RunAsync(() =>
        {
            var window = CreateMainWindow(850, 620);
            try
            {
                var hero = (Grid)window.FindName("SituationHeroLayout")!;
                var detail = (FrameworkElement)window.FindName("SubsystemDetailPanel")!;

                // Control the layout input directly because the display can constrain the outer window.
                hero.Width = 940;
                window.UpdateLayout();
                Assert.Equal(940d, hero.ActualWidth);
                Assert.Equal(0, Grid.GetRow(detail));
                Assert.Equal(1, Grid.GetColumn(detail));

                hero.Width = 939;
                window.UpdateLayout();
                Assert.Equal(939d, hero.ActualWidth);
                Assert.Equal(1, Grid.GetRow(detail));
                Assert.Equal(0, Grid.GetColumn(detail));
                Assert.Equal(0d, hero.ColumnDefinitions[1].Width.Value);
            }
            finally { window.Close(); }
        });
    }

    private static MainWindow CreateMainWindow(double width, double height)
    {
        var controller = new DashboardController((_, _) => ValueTask.FromResult(Array.Empty<byte>()));
        var window = new MainWindow(controller, TestLocalization.Czech())
        { Width = width, Height = height, ShowInTaskbar = false };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static T? FindVisualDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            var nested = FindVisualDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static Window Show(SituationOrbitView view, double width, double height)
    {
        var window = new Window { Content = view, Width = width, Height = height, ShowInTaskbar = false };
        window.Show();
        window.UpdateLayout();
        return window;
    }
}
