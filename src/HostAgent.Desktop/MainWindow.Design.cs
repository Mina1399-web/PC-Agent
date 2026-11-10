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

    private void InitializeDesign()
    {
        PreviewKeyDown += DesignKeyDown;
        SizeChanged += (_, _) => UpdateDesignLayout();
        Loaded += (_, _) => UpdateDesignLayout();
        OperationChoice.SelectionChanged += (_, _) => UpdateProcessFields();
        UpdateProcessFields();
        UpdateThemeLabel();
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
        if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            RefreshClick(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D6)
        {
            NavigateTo((int)e.Key - (int)Key.D1);
            e.Handled = true;
        }
    }

    private void NavigateTo(int index)
    {
        if (index < 0 || index >= DashboardTabs.Items.Count) return;
        if (!DesktopModePolicy.CanNavigateTo(index, _desktopMode)) return;
        DashboardTabs.SelectedIndex = index;
        ((TabItem)DashboardTabs.Items[index]).Focus();
    }

    private void TriageActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target }) return;
        if (target == "refresh")
        {
            RefreshClick(sender, e);
            return;
        }
        if (!int.TryParse(target, out var index) || index is < 1 or > 2) return;
        NavigateTo(index);
        if (index != 1) return;
        var incident = IncidentsGrid.Items.OfType<ViewIncident>().FirstOrDefault(x => x.Id == _triageIncidentId);
        IncidentsGrid.SelectedItem = incident;
        if (incident is not null) IncidentsGrid.ScrollIntoView(incident);
    }

    private void ThemeClick(object sender, RoutedEventArgs e)
    {
        _darkTheme = !_darkTheme;
        // Only window-local presentation resources change. No service settings or data writes.
        foreach (var (key, dark, light) in ThemeColors)
            Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_darkTheme ? dark : light));
        UpdateThemeLabel();
        if (_hasRefreshed) RenderSnapshot(_renderedView, false);
        DrawChart();
    }

    private void UpdateThemeLabel() => ThemeButton.Content = T(_darkTheme ? "DesignLightTheme" : "DesignDarkTheme");

    private void RenderDesignMetrics(DashboardSnapshot? view)
    {
        SetMeter(CpuMeter, view?.Cpu);
        SetMeter(MemoryMeter, view?.Memory);
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
        TriageActionButton.Tag = incident is null ? "2" : "1";
        System.Windows.Automation.AutomationProperties.SetName(TriageActionButton, (string)TriageActionButton.Content);
        var disk = view.Disks.Where(x => ViewFreshness.IsFresh(x.AtUtc, now) && x.CapacityBytes > 0 && x.FreeBytes >= 0 && x.FreeBytes <= x.CapacityBytes / 10)
            .OrderBy(x => (double)x.FreeBytes / x.CapacityBytes).FirstOrDefault();
        TriageDiskText.Text = disk is null ? "" : T("DesignLowDisk", disk.Drive, 100d * disk.FreeBytes / disk.CapacityBytes);
        TriageDiskText.Visibility = disk is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void SetMeter(ProgressBar meter, ViewMetric? metric)
    {
        var available = metric is not null && double.IsFinite(metric.Value);
        meter.Visibility = available ? Visibility.Visible : Visibility.Hidden;
        meter.Value = available ? Math.Clamp(metric!.Value, 0, 100) : 0;
    }

    private static readonly (string Key, string Dark, string Light)[] ThemeColors =
    [
        ("CanvasBrush", "#101722", "#EEECE5"),
        ("CardBrush", "#192331", "#FAF9F4"),
        ("InkBrush", "#F1F5FC", "#172722"),
        ("MutedBrush", "#B3C1D5", "#53635B"),
        ("AccentBrush", "#9AC4FF", "#17674E"),
        ("AccentInkBrush", "#101722", "#FAF9F4"),
        ("LineBrush", "#344358", "#B9BDB3"),
        ("InputBorderBrush", "#718198", "#7E897F"),
        ("RailBrush", "#0C121C", "#183A32"),
        ("RailTextBrush", "#D5DFEF", "#E3EBE4"),
        ("SelectedBrush", "#263E60", "#31564B"),
        ("HoverBrush", "#28374B", "#24493E"),
        ("AlternateBrush", "#1E2B3B", "#F0EFE8"),
        ("GreenBrush", "#83DCB8", "#17674E"),
        ("GreenSurfaceBrush", "#17372F", "#DFEEE7"),
        ("WarningBrush", "#F3CF8B", "#9A591C"),
        ("WarningSurfaceBrush", "#3C3020", "#F5E8D2"),
        ("ErrorBrush", "#FFB4B4", "#A7432A"),
        ("ErrorSurfaceBrush", "#43282E", "#F2DFD9"),
        ("RamBrush", "#C6AEFF", "#58639A")
    ];
}
