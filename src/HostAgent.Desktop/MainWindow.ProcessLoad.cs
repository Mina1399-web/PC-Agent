using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using HostAgent.Core.Dashboard;

namespace HostAgent.Desktop;

public partial class MainWindow
{
    private void RenderProcessRanking(DashboardSnapshot? view, bool replaceRows)
    {
        var cpu = ProcessRankingChoice.SelectedIndex == 1;
        var data = view?.ProcessLoad;
        var state = cpu ? data?.CpuState : data?.RamState ?? view?.DataState?.Processes;
        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<ViewProcess> rows = cpu ? data?.TopCpu ?? [] : view?.Processes ?? [];
        if ((state is not null && !state.HasCurrentData(now)) || cpu && state is null)
            rows = [];
        var description = DescribeData(state, now);
        ProcessGrid.ToolTip = description;
        AutomationProperties.SetName(ProcessGrid, T(cpu ? "ProcessesByCpuUsage" : "ProcessesByMemoryUsage"));
        ProcessRankingCoverage.Text = data is not null
            ? T("ProcessRankingCoverage", cpu ? data.CpuMeasured : data.RamMeasured, data.Sampled)
            : "";
        ProcessRankingCoverage.Visibility = data is null ? Visibility.Collapsed : Visibility.Visible;
        SetListState(ProcessesEmptyText, rows.Count, state);
        // Keep the source and observation time visible even when coverage is complete.
        ProcessesEmptyText.Visibility = Visibility.Visible;
        if (!replaceRows) return;
        var selected = ProcessGrid.SelectedItem as ViewProcess;
        ProcessGrid.ItemsSource = view is null ? null : rows;
        ProcessGrid.SelectedItem = selected?.StartTimeUtcTicks is > 0
            ? rows.FirstOrDefault(row => row.Pid == selected.Pid && row.StartTimeUtcTicks == selected.StartTimeUtcTicks)
            : null;
    }
}
