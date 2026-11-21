using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using HostAgent.Core.Dashboard;

namespace HostAgent.Desktop;

public partial class SituationSubsystemView : UserControl
{
    private enum Subsystem { Cpu, Memory, Disks, Network, Services }

    private static readonly string[] Labels = ["Processor", "RAM", "Disks", "Network", "WinToolServices"];
    private readonly RadioButton[] _mapButtons;
    private readonly RadioButton[] _listButtons;
    private Subsystem _selected;
    private FrameworkElement? _opener;
    private DashboardSnapshot? _snapshot;
    private DesktopLocalization? _localization;
    private DateTimeOffset _now;
    private bool _syncingChoices;
    private bool _settingDetailState;

    public SituationSubsystemView()
    {
        InitializeComponent();
        _mapButtons = CreateChoices(Map);
        _listButtons = CreateChoices(SubsystemList);
        SyncChoices();
    }

    private RadioButton[] CreateChoices(Panel panel) =>
        Enum.GetValues<Subsystem>().Select(id =>
        {
            var choice = new RadioButton();
            choice.Checked += (_, _) => { if (!_syncingChoices) Open(id, choice); };
            choice.Click += (_, _) => Open(id, choice);
            AutomationProperties.SetAutomationId(choice, panel.Name + id);
            panel.Children.Add(choice);
            return choice;
        }).ToArray();

    internal void Render(DashboardSnapshot? snapshot, DesktopLocalization localization, DateTimeOffset now)
    {
        _snapshot = snapshot;
        _localization = localization;
        _now = now;
        for (var i = 0; i < Labels.Length; i++)
        {
            var label = localization.Text(Labels[i]);
            _mapButtons[i].Content = label;
            _listButtons[i].Content = label;
            AutomationProperties.SetName(_mapButtons[i], label);
            AutomationProperties.SetName(_listButtons[i], label);
        }
        RenderSelection();
    }

    private void SyncChoices()
    {
        _syncingChoices = true;
        _mapButtons[(int)_selected].IsChecked = true;
        _listButtons[(int)_selected].IsChecked = true;
        _syncingChoices = false;
    }

    private void Open(Subsystem id, FrameworkElement opener)
    {
        _selected = id;
        _opener = opener;
        SyncChoices();
        RenderSelection();
        SetDetailToggle(true);
        Detail.Visibility = Visibility.Visible;
        ArrangeDetail(ActualWidth);
    }

    private void ToggleDetail(object sender, RoutedEventArgs e)
    {
        if (_settingDetailState) return;
        if (ShowDetail.IsChecked == true) Open(_selected, ShowDetail);
        else CollapseDetail();
    }

    private void SetDetailToggle(bool isOpen)
    {
        _settingDetailState = true;
        ShowDetail.IsChecked = isOpen;
        _settingDetailState = false;
    }

    private void CloseDetail(object sender, RoutedEventArgs e) => CollapseDetail();

    private void CollapseDetail(bool restoreFromComponent = false)
    {
        var restoreFocus = restoreFromComponent || Detail.IsKeyboardFocusWithin;
        SetDetailToggle(false);
        if (restoreFocus && !(_opener is { IsVisible: true, IsEnabled: true } && _opener.Focus())
            && !_mapButtons[(int)_selected].Focus())
            ShowDetail.Focus();
        Detail.Visibility = Visibility.Collapsed;
        ArrangeDetail(ActualWidth);
    }

    private void HandleKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.Escape && Detail.IsVisible && IsKeyboardFocusWithin)
        {
            CollapseDetail(restoreFromComponent: true);
            e.Handled = true;
            return;
        }

        if (e.OriginalSource is not RadioButton choice) return;
        var index = Array.IndexOf(_mapButtons, choice);
        if (index >= 0)
        {
            // The map fills two columns in reading order. Arrow exploration moves focus only.
            var destination = e.Key switch
            {
                Key.Left when index % 2 == 1 => index - 1,
                Key.Right when index % 2 == 0 => index + 1,
                Key.Up => index - 2,
                Key.Down => index + 2,
                _ => -1
            };
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
            {
                if (destination >= 0 && destination < _mapButtons.Length) _mapButtons[destination].Focus();
                e.Handled = true;
                return;
            }
        }
        else index = Array.IndexOf(_listButtons, choice);

        if (index >= 0 && e.Key is (Key.Enter or Key.Space))
        {
            Open((Subsystem)index, choice);
            e.Handled = true;
        }
    }

    private void UpdateLayoutForWidth(object sender, SizeChangedEventArgs e) => ArrangeDetail(e.NewSize.Width);

    private void ArrangeDetail(double width)
    {
        // Leave room for both sets of choices and a wrapped provenance line.
        // Change grid coordinates in place so the focused control stays mounted.
        var sideBySide = Detail.Visibility == Visibility.Visible && width >= 660;
        DetailColumn.Width = sideBySide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(Detail, sideBySide ? 1 : 0);
        Grid.SetRow(Detail, sideBySide ? 0 : 1);
        Detail.Margin = sideBySide ? new Thickness(16, 0, 0, 0) : new Thickness(0, 12, 0, 0);
    }

    private void RenderSelection()
    {
        if (_localization is not { } loc) return;
        var data = _snapshot?.DataState;
        var state = CurrentState(_selected switch
        {
            Subsystem.Cpu => data?.Cpu,
            Subsystem.Memory => data?.Memory,
            Subsystem.Disks => data?.Disks,
            Subsystem.Network => data?.Network,
            _ => data?.Services
        }, _now);
        string? value = null;
        if (state?.HasCurrentData(_now) == true && _snapshot is { } view)
        {
            value = CurrentValue(view, state, loc);
            if (value is null && state.Status == "available")
                state = state with { Status = "unavailable", Reason = "invalid_sample" };
        }
        DetailTitle.Text = loc.Text(Labels[(int)_selected]);
        DetailStatus.Text = DataTrustDisplay.Describe(loc, state);
        DetailValue.Text = value ?? loc.Text("SubsystemNoCurrentValue");
    }

    internal static ViewDataState? CurrentState(ViewDataState? state, DateTimeOffset now)
    {
        if (state is not { Status: "available" or "partial" }) return state;
        if (state.AtUtc is not { } at || at > now || string.IsNullOrWhiteSpace(state.Source))
            return state with { Status = "unknown", Reason = "invalid_sample" };
        return ViewFreshness.IsFresh(at, now) ? state : state with { Status = "stale", Reason = "sample_expired" };
    }

    private string? CurrentValue(DashboardSnapshot view, ViewDataState state, DesktopLocalization loc)
    {
        string? Percent(ViewMetric? point, string label) =>
            point is { Value: >= 0 and <= 100 } && point.AtUtc == state.AtUtc
                ? loc.Text(label) + ": " + point.Value.ToString("0.#", loc.Culture) + " %" : null;
        string? Excerpt(string[] entries) => entries.Length > 0
            ? loc.Text("SubsystemExcerpt") + "\n" + string.Join("\n", entries)
            : state.Status == "available" ? loc.Text("DataTrustEmpty") : null;

        return _selected switch
        {
            Subsystem.Cpu => Percent(view.Cpu, "TotalCPUUtilization"),
            Subsystem.Memory => Percent(view.Memory, "PhysicalMemoryUsage"),
            Subsystem.Network => view.ConnectedAdapters is >= 0
                ? loc.Text("NetworkActiveAdaptersThisDoesNotVerifyInternetAccess", view.ConnectedAdapters) : null,
            Subsystem.Disks => view.Disks.Count != 0 && view.Disks.Any(d => d.AtUtc != state.AtUtc ||
                string.IsNullOrWhiteSpace(d.Drive) || d.CapacityBytes <= 0 || d.FreeBytes < 0 || d.FreeBytes > d.CapacityBytes)
                ? null : Excerpt(view.Disks.Select(d => loc.Text("SubsystemDiskLine", d.Drive,
                    ByteSizeConverter.Format(d.FreeBytes, loc.Culture), ByteSizeConverter.Format(d.CapacityBytes, loc.Culture))).ToArray()),
            _ => view.Services.Count != 0 && view.Services.Any(s => string.IsNullOrWhiteSpace(s.Name))
                ? null : Excerpt(view.Services.Select(s => s.Name + " · " + TechnicalText.Format(s.State, loc.Culture)).ToArray())
        };
    }
}

internal static class DataTrustDisplay
{
    internal static string Describe(DesktopLocalization loc, ViewDataState? state)
    {
        var status = loc.Text(state?.Status switch
        {
            "available" => "DataTrustAvailable", "partial" => "DataTrustPartial",
            "unavailable" => "DataTrustUnavailable", "stale" => "DataTrustStale", _ => "DataTrustUnknown"
        });
        var reason = state?.Reason switch
        {
            "access_denied" => "DataTrustAccessDenied", "invalid_sample" => "DataTrustInvalid",
            "missing_sample" => "DataTrustMissing", "sample_expired" => "DataTrustExpired",
            "collector_unavailable" or "read_timeout" => "DataTrustFailed", "waiting_for_sample" => "DataTrustWaiting",
            "incomplete_sample" => "DataTrustIncomplete", "legacy_sample" => "DataTrustLegacy", _ => null
        };
        if (reason is not null) status += " · " + loc.Text(reason);
        if (state is null) return status;
        return status + "\n" + loc.Text("DataTrustMeta", state.Source,
            state.AtUtc?.ToLocalTime().ToString("G", loc.Culture) ?? loc.Text("DataTrustNoTime"));
    }
}
