using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HostAgent.Core.Dashboard;

namespace HostAgent.Desktop;

public partial class MainWindow
{
    private bool _processRankingRequested;
    private int _processRankingSelectionVersion;

    private async void ProcessRankingChanged(object sender, SelectionChangedEventArgs e)
    {
        _processRankingRequested = true;
        _processRankingSelectionVersion++;
        var selected = ProcessGrid.SelectedItem as ViewProcess;
        RenderProcessRanking(_renderedView, true);
        await RefreshVisibleProcessRanking(selected);
    }

    private async Task RefreshVisibleProcessRanking(ViewProcess? preferred = null)
    {
        if (!_processRankingRequested || !IsVisible ||
            DashboardTabs.SelectedItem is not TabItem { Tag: DesktopRoute.Situation } ||
            _renderedView is not { } view || !ViewFreshness.IsFresh(view.GeneratedAtUtc, DateTimeOffset.UtcNow)) return;
        var version = _processRankingSelectionVersion;
        try
        {
            if (await _controller.RefreshProcessLoadAsync(_closing.Token) && !_closing.IsCancellationRequested
                && version == _processRankingSelectionVersion && ReferenceEquals(view, _renderedView))
                RenderProcessRanking(view, true, preferred);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
    }

    private void RenderProcessRanking(DashboardSnapshot? view, bool replaceRows, ViewProcess? preferred = null)
    {
        var cpu = ProcessRankingChoice.SelectedIndex == 1;
        var data = view is null ? null : _controller.ProcessLoad;
        var state = cpu ? data?.CpuState : data?.RamState ?? view?.DataState?.Processes;
        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<ViewProcess> rows = cpu ? data?.TopCpu ?? [] : data?.TopRam ?? view?.Processes ?? [];
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
        var selected = ProcessGrid.SelectedItem as ViewProcess ?? preferred;
        ProcessGrid.ItemsSource = view is null ? null : rows;
        ProcessGrid.SelectedItem = selected?.StartTimeUtcTicks is > 0
            ? rows.FirstOrDefault(row => row.Pid == selected.Pid && row.StartTimeUtcTicks == selected.StartTimeUtcTicks)
            : null;
    }
}
