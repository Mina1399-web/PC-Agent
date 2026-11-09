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
    private bool _darkTheme = true;
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
        // Keep all destinations visible on short desktop displays. The same approval
        // explanation remains in the status banner and at the point of every action.
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
        DashboardTabs.SelectedIndex = index;
        ((TabItem)DashboardTabs.Items[index]).Focus();
    }

    private void QuickNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string index } && int.TryParse(index, out var value)) NavigateTo(value);
        if (ReferenceEquals(sender, QuickIncidents))
        {
            var incident = IncidentsGrid.Items.OfType<ViewIncident>().FirstOrDefault(x => x.Id == _triageIncidentId);
            IncidentsGrid.SelectedItem = incident;
            if (incident is not null) IncidentsGrid.ScrollIntoView(incident);
        }
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
        ("CanvasBrush", "#101722", "#F4F7FB"),
        ("CardBrush", "#192331", "#FFFFFF"),
        ("InkBrush", "#F1F5FC", "#14243B"),
        ("MutedBrush", "#B3C1D5", "#52627A"),
        ("AccentBrush", "#9AC4FF", "#2259C5"),
        ("AccentInkBrush", "#101722", "#FFFFFF"),
        ("LineBrush", "#344358", "#DDE4EE"),
        ("InputBorderBrush", "#718198", "#718198"),
        ("RailBrush", "#0C121C", "#12243C"),
        ("RailTextBrush", "#D5DFEF", "#D5DFEF"),
        ("SelectedBrush", "#263E60", "#E8F0FF"),
        ("HoverBrush", "#28374B", "#E8F0FF"),
        ("AlternateBrush", "#1E2B3B", "#F8FAFD"),
        ("GreenBrush", "#83DCB8", "#176B50"),
        ("GreenSurfaceBrush", "#17372F", "#E8F5EF"),
        ("WarningBrush", "#F3CF8B", "#895514"),
        ("WarningSurfaceBrush", "#3C3020", "#FFF4DE"),
        ("ErrorBrush", "#FFB4B4", "#A03232"),
        ("ErrorSurfaceBrush", "#43282E", "#FDECEC"),
        ("RamBrush", "#C6AEFF", "#7854BA")
    ];
}
