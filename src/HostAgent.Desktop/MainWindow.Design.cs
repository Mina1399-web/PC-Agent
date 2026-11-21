using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HostAgent.Core.Dashboard;

namespace HostAgent.Desktop;

public partial class MainWindow
{
    private bool _darkTheme;
    private string? _triageIncidentId;
    private static readonly DesktopRoute[] ShortcutRoutes =
    [DesktopRoute.Situation, DesktopRoute.Events, DesktopRoute.Recommendations, DesktopRoute.System, DesktopRoute.Maintenance, DesktopRoute.WindowsTools];

    private void InitializeDesign()
    {
        PreviewKeyDown += DesignKeyDown;
        SizeChanged += (_, _) => UpdateDesignLayout();
        Loaded += (_, _) => UpdateDesignLayout();
        OperationChoice.SelectionChanged += (_, _) => UpdateProcessFields();
        UpdateProcessFields();
        UpdateThemeLabel();
        SubsystemView.Render(null, Localization, DateTimeOffset.UtcNow);
    }

    private void UpdateDesignLayout()
    {
        // Keep all six named destinations visible on short desktop displays. The
        // same approval explanation remains in the status banner and at every action.
        if (DashboardTabs.Template.FindName("NavigationFooter", DashboardTabs) is Border footer)
            footer.Visibility = ActualHeight < 700 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void DesignKeyDown(object sender, KeyEventArgs e)
    {
        if (IsRefreshShortcut(e.Key, Keyboard.Modifiers))
        {
            RefreshClick(sender, e);
            e.Handled = true;
        }
        else if (RouteForShortcut(e.Key, Keyboard.Modifiers) is {} route)
        {
            NavigateTo(route);
            e.Handled = true;
        }
    }

    internal static bool IsRefreshShortcut(Key key, ModifierKeys modifiers) =>
        key == Key.F5 && modifiers == ModifierKeys.None;

    internal static DesktopRoute? RouteForShortcut(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Control && key >= Key.D1 && key <= Key.D6
            ? ShortcutRoutes[(int)key - (int)Key.D1] : null;

    private TabItem? FindTab(DesktopRoute route) =>
        DashboardTabs.Items.OfType<TabItem>().FirstOrDefault(tab => Equals(tab.Tag, route));

    private void NavigateTo(DesktopRoute route, bool focus = true)
    {
        if (!DesktopModePolicy.CanNavigateTo(route, _desktopMode) || FindTab(route) is not {} tab) return;
        DashboardTabs.SelectedItem = tab;
        if (focus) tab.Focus();
    }

    private void TriageActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.Tag is "refresh")
        {
            RefreshClick(sender, e);
            return;
        }
        if (button.Tag is not DesktopRoute route) return;
        NavigateTo(route);
        if (route != DesktopRoute.Events) return;
        var incident = IncidentsGrid.Items.OfType<ViewIncident>().FirstOrDefault(x => x.Id == _triageIncidentId);
        IncidentsGrid.SelectedItem = incident;
        if (incident is not null) IncidentsGrid.ScrollIntoView(incident);
    }

    private void ThemeClick(object sender, RoutedEventArgs e)
    {
        _darkTheme = !_darkTheme;
        OrbitTheme.SetPalette(this, _darkTheme);
        UpdateThemeLabel();
        if (_hasRefreshed) RenderSnapshot(_renderedView, false);
        DrawChart();
    }

    private void UpdateThemeLabel() => ThemeButton.Content = T(_darkTheme ? "DesignLightTheme" : "DesignDarkTheme");

    private void RenderDesignMetrics(DashboardSnapshot? view)
    {
        var now=DateTimeOffset.UtcNow;
        SetMeter(CpuMeter, CurrentDisplayMetric(view?.Cpu,view?.DataState?.Cpu,now));
        SetMeter(MemoryMeter, CurrentDisplayMetric(view?.Memory,view?.DataState?.Memory,now,allowLegacyZero:false));
        SubsystemView.Render(view, Localization, now);
        RenderTriage(view);
    }

    private void UpdateProcessFields() => ProcessIdentityPanel.Visibility =
        OperationChoice.SelectedItem is ComboBoxItem { Tag: "TerminateProcess" } ? Visibility.Visible : Visibility.Collapsed;

    private void RenderTriage(DashboardSnapshot? view)
    {
        var now = DateTimeOffset.UtcNow;
        if (view is null || !ViewFreshness.IsFresh(view.GeneratedAtUtc, now))
        {
            _triageIncidentId = null;
            TriageIncidentText.Text = T("DesignEvidenceUnavailable");
            TriageActionText.Text = T("DesignRefreshFirst");
            TriageEvidenceMetaText.Text = T("DesignEvidenceSourceUnavailable");
            TriageActionButton.Content = T("DesignRefreshMeasurementsButton");
            TriageActionButton.Tag = "refresh";
            System.Windows.Automation.AutomationProperties.SetName(TriageActionButton, (string)TriageActionButton.Content);
            TriageDiskText.Text = "";
            TriageDiskText.Visibility = Visibility.Collapsed;
            return;
        }
        // This is a ranking of retained records, never proof that a historical fault is still occurring.
        static int Rank(string severity) => severity switch { "Critical" => 4, "High" => 3, "Warning" => 2, "Information" => 1, _ => 0 };
        var incident = view.Incidents.Where(x => x.State is not ("Resolved" or "Failed") && x.AtUtc <= now)
            .OrderByDescending(x => Rank(x.Severity)).ThenByDescending(x => x.AtUtc).FirstOrDefault();
        _triageIncidentId = incident?.Id;
        TriageIncidentText.Text = incident is null ? T("DesignNoOpenEvidence") : T("DesignRecordedIncident",
            TechnicalText.Format(incident.Code, Localization.Culture), TechnicalText.Format(incident.Severity, Localization.Culture),
            incident.AtUtc.ToUniversalTime(), Math.Max(0, (int)(now - incident.AtUtc).TotalMinutes));
        TriageActionText.Text = incident is null ? T("DesignReviewRecommendations") : T("DesignReviewRecordedEvidence");
        TriageEvidenceMetaText.Text = T("DesignEvidenceSourceAndTime", view.GeneratedAtUtc.ToLocalTime());
        TriageActionButton.Content = T(incident is null ? "DesignReviewRecommendationsButton" : "DesignReviewEvidenceButton");
        TriageActionButton.Tag = incident is null ? DesktopRoute.Recommendations : DesktopRoute.Events;
        System.Windows.Automation.AutomationProperties.SetName(TriageActionButton, (string)TriageActionButton.Content);
        var disk = view.Disks.Where(x => ViewFreshness.IsFresh(x.AtUtc, now) && x.CapacityBytes > 0 && x.FreeBytes >= 0 && x.FreeBytes <= x.CapacityBytes / 10)
            .OrderBy(x => (double)x.FreeBytes / x.CapacityBytes).FirstOrDefault();
        TriageDiskText.Text = disk is null ? "" : T("DesignLowDisk", disk.Drive, 100d * disk.FreeBytes / disk.CapacityBytes);
        TriageDiskText.Visibility = disk is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void SetMeter(ProgressBar meter, ViewMetric? metric)
    {
        var available = metric is { Value: >= 0 and <= 100 } && ViewFreshness.IsFresh(metric.AtUtc,DateTimeOffset.UtcNow);
        meter.Visibility = available ? Visibility.Visible : Visibility.Hidden;
        meter.Value = available ? metric!.Value : 0;
    }
}

