using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using HostAgent.Core.Dashboard;

namespace HostAgent.Desktop;

public partial class MainWindow
{
    private int _diskHistoryVersion;
    private bool _diskHistoryLoading;
    private ViewDiskHistory? _diskHistory;

    private string? SelectedDiskHistory => (HistoryGroup.SelectedItem as ComboBoxItem)?.Tag as string;

    private async void HistoryGroupChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiskHistoryChart is null) return;
        await LoadSelectedDiskHistory();
    }

    private async Task LoadSelectedDiskHistory()
    {
        var version = ++_diskHistoryVersion;
        var group = SelectedDiskHistory;
        _diskHistory = null;
        _diskHistoryLoading = group is not null;
        RenderDiskHistory();
        if (group is null) return;

        ViewDiskHistory? response = null;
        try
        {
            response = await _controller.ReadAsync<ViewDiskHistory>("disk-history", new { group }, _closing.Token);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        if (_closing.IsCancellationRequested || version != _diskHistoryVersion) return;
        _diskHistory = ValidDiskHistory(response, group) ? response : null;
        _diskHistoryLoading = false;
        RenderDiskHistory();
    }

    private static bool ValidDiskHistory(ViewDiskHistory? history, string group)
    {
        if (history is not { BucketMinutes: 15, Series: { } series } ||
            history.ThroughUtc - history.FromUtc != TimeSpan.FromHours(24)) return false;
        if (group == "throughput" ? series.Count != 2 : series.Count != 1) return false;
        return series.All(row => row is { Values.Count: 96, CurrentState: not null }
                && row.Unit == (group == "throughput" ? "B/s" : "ms"))
            && series.Select(row => row.Metric).SequenceEqual(group == "throughput" ? new[] { "read", "write" } : new[] { "latency" });
    }

    private void RenderDiskHistory()
    {
        if (DiskHistoryChart is null) return;
        var group = SelectedDiskHistory;
        CpuHistoryPanel.Visibility = group is null ? Visibility.Visible : Visibility.Collapsed;
        DiskHistoryPanel.Visibility = group is null ? Visibility.Collapsed : Visibility.Visible;
        if (group is null) return;

        DiskHistoryChart.Children.Clear();
        var title = T(group == "latency" ? "DiskHistoryLatency" : "DiskHistoryThroughput");
        if (_diskHistory is not { } history)
        {
            DiskHistoryStatus.Text = T(_diskHistoryLoading ? "DiskHistoryLoading" : "DiskHistoryUnavailable");
            DiskHistoryWindow.Text = "";
            AutomationProperties.SetName(DiskHistorySummary, title + ". " + DiskHistoryStatus.Text);
            return;
        }

        var width = Math.Max(1, DiskHistoryChart.ActualWidth - 6);
        var height = Math.Max(1, DiskHistoryChart.ActualHeight - 6);
        var maximum = history.Series.SelectMany(row => row.Values).Where(IsDiskValue)
            .Select(value => value.GetValueOrDefault()).DefaultIfEmpty(0).Max();
        foreach (var fraction in new[] { 0d, .5, 1d })
            DiskHistoryChart.Children.Add(new Line { X1 = 3, X2 = width + 3, Y1 = 3 + height * fraction,
                Y2 = 3 + height * fraction, Stroke = (Brush)FindResource("LineBrush"), StrokeDashArray = new DoubleCollection { 4, 4 } });

        var descriptions = new List<string>(history.Series.Count);
        DiskHistoryStatus.Inlines.Clear();
        var now = DateTimeOffset.UtcNow;
        foreach (var row in history.Series)
        {
            var color = (Brush)FindResource(row.Metric == "write" ? "RamBrush" : "AccentBrush");
            var label = T(row.Metric switch { "read" => "DiskHistoryRead", "write" => "DiskHistoryWrite", _ => "DiskHistoryLatency" });
            Polyline? line = null;
            for (var i = 0; i < row.Values.Count; i++)
            {
                var value = row.Values[i];
                if (!IsDiskValue(value)) { line = null; continue; }
                var fraction = maximum > 0 ? value!.Value / maximum : 0d;
                var point = new Point(3 + width * (i + .5) / row.Values.Count, 3 + height * (1 - fraction));
                if (line is null)
                {
                    line = new Polyline { Stroke = color, StrokeThickness = 2 };
                    if (row.Metric == "write") line.StrokeDashArray = new DoubleCollection { 3, 2 };
                    DiskHistoryChart.Children.Add(line);
                }
                line.Points.Add(point);
                var dot = new Ellipse { Width = 4, Height = 4, Fill = color };
                Canvas.SetLeft(dot, point.X - 2);
                Canvas.SetTop(dot, point.Y - 2);
                DiskHistoryChart.Children.Add(dot);
            }

            var current = row.CurrentState.HasCurrentData(now) && row.Current is { } pointNow
                && pointNow.AtUtc == row.CurrentState.AtUtc && IsDiskValue(pointNow.Value)
                ? pointNow.Value.ToString("G4", Localization.Culture) + " " + row.Unit : "—";
            var coverage = row.CoverageReason == "history_capacity" ? T("DiskHistoryCapacity") :
                DataTrustDisplay.Describe(Localization, new ViewDataState(row.Source, null, row.Coverage, row.CoverageReason));
            var currentState = row.CurrentState.Reason?.StartsWith("disk_io_", StringComparison.Ordinal) == true
                || row.CurrentState.Reason == "some_disk_io_counters_unavailable"
                ? row.CurrentState with { Reason = "collector_unavailable" }
                : row.CurrentState;
            var description = T("DiskHistoryCoverage", label, row.Values.Count(IsDiskValue), row.Values.Count,
                coverage.Replace("\n", " · "), current, DescribeData(currentState, now).Replace("\n", " · "));
            descriptions.Add(description);
            if (DiskHistoryStatus.Inlines.Count > 0) DiskHistoryStatus.Inlines.Add(new LineBreak());
            DiskHistoryStatus.Inlines.Add(new Run(row.Metric == "write" ? "– –  " : "━  ") { Foreground = color });
            DiskHistoryStatus.Inlines.Add(new Run(description));
        }
        var windowKey = descriptions.Count > 0 && history.Series.Any(row => row.Values.Any(IsDiskValue))
            ? "DiskHistoryWindow" : "DiskHistoryWindowEmpty";
        DiskHistoryWindow.Text = T(windowKey, history.FromUtc.ToLocalTime(), history.ThroughUtc.ToLocalTime(),
            maximum > 0 ? maximum : 1, history.Series[0].Unit);
        AutomationProperties.SetName(DiskHistorySummary, title + ". " + string.Join(" ", descriptions) + " " + DiskHistoryWindow.Text);
    }

    private static bool IsDiskValue(double? value) => value is >= 0 and < double.PositiveInfinity;

    private void DiskHistoryChartSizeChanged(object sender, SizeChangedEventArgs e) => RenderDiskHistory();
}
