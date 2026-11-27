using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HostAgent.Core.Dashboard;
using HostAgent.Core.Models.Incidents;

namespace HostAgent.Desktop;

public partial class MainWindow
{
    private readonly ObservableCollection<ViewIncident> _incidentHistory = [];
    private IncidentHistoryCursor? _historyNext;
    private ViewIncident? _historyPinnedIncident;
    private bool _historySupported, _historyConnected, _historyStarted, _historyLoading, _historyLoadingOlder;
    private bool _applyingHistoryRows;
    private int _historyRequestId;
    private string? _historyStatus;
    private ViewIncident? SelectedEventIncident => IncidentsGrid.SelectedItem as ViewIncident ?? _historyPinnedIncident;

    private void RenderIncidentOverview(DashboardSnapshot? view, bool replaceRows)
    {
        var reconnecting = !_historyConnected && view?.SupportsIncidentHistory == true;
        if (view is { SupportsIncidentHistory: true } && !_historySupported)
        {
            _historySupported = true;
            ReplaceHistoryRows([]);
        }
        else if (view is { SupportsIncidentHistory: false } && _historySupported)
        {
            InvalidateHistoryRequest();
            _historySupported = _historyStarted = false;
            _historyNext = null;
            _historyStatus = null;
            _incidentHistory.Clear();
        }
        if (_historyConnected && view is null)
        {
            if (_historyLoading)
                _historyStatus = _historyLoadingOlder ? "IncidentHistoryOlderFailed" : "IncidentHistoryRefreshFailed";
            InvalidateHistoryRequest();
            ++_detailRequestVersion;
            _detailLoading = false;
        }
        _historyConnected = view is not null;

        if (!_historySupported)
        {
            if (replaceRows || view is null)
            {
                var selectedId = SelectedEventIncident?.Id;
                _historyPinnedIncident = null;
                IncidentsGrid.ItemsSource = view?.Incidents;
                if (selectedId is not null)
                    IncidentsGrid.SelectedItem = view?.Incidents.FirstOrDefault(row => row.Id == selectedId);
                RenderSelectedIncidentSummary();
            }
            IncidentsEmptyText.Text = view is null
                ? T("IncidentsAreUnavailableWithoutAConnectionToTheService")
                : T("IncidentHistoryLegacy") + (view.Incidents.Count == 0
                    ? "\n" + T("ThereAreNoIncidentsInTheLatestSavedRecords") : "");
        }
        else if (replaceRows) RenderSelectedIncidentSummary();
        UpdateIncidentHistoryControls();
        if (reconnecting && SelectedEventIncident is not null && _selectedDetail is null && !_detailLoading)
            _ = LoadSelectedIncidentAsync();
        if (replaceRows) _ = LoadInitialIncidentHistoryAsync();
    }

    private void InvalidateHistoryRequest()
    {
        ++_historyRequestId;
        _historyLoading = _historyLoadingOlder = false;
    }

    private Task LoadInitialIncidentHistoryAsync() => !_historyStarted &&
        DashboardTabs.SelectedItem is TabItem { Tag: DesktopRoute.Events }
        ? LoadIncidentPageAsync(older: false) : Task.CompletedTask;

    private async void RefreshIncidentHistoryClick(object sender, RoutedEventArgs e) => await LoadIncidentPageAsync(older: false);
    private async void LoadOlderIncidentsClick(object sender, RoutedEventArgs e) => await LoadIncidentPageAsync(older: true);

    private async Task LoadIncidentPageAsync(bool older)
    {
        if (!_historySupported || !_historyConnected || _closing.IsCancellationRequested) return;
        if (older ? _historyNext is null || _historyLoading : _historyLoading && !_historyLoadingOlder) return;
        var requestId = ++_historyRequestId;
        _historyStarted = _historyLoading = true;
        _historyLoadingOlder = older;
        _historyStatus = "IncidentHistoryLoading";
        UpdateIncidentHistoryControls();
        try
        {
            var page = await _controller.ReadIncidentPageAsync(older ? _historyNext : null, _closing.Token);
            if (_closing.IsCancellationRequested || requestId != _historyRequestId) return;
            if (page is null)
            {
                _historyStatus = older ? "IncidentHistoryOlderFailed" : "IncidentHistoryRefreshFailed";
                return;
            }

            if (older)
            {
                foreach (var row in page.Rows) _incidentHistory.Add(row);
                if (_historyPinnedIncident is { } pin &&
                    page.Rows.FirstOrDefault(row => row.Id == pin.Id) is { } nowListed)
                    IncidentsGrid.SelectedItem = nowListed;
            }
            else ReplaceHistoryRows(page.Rows);
            _historyNext = page.Next;
            _historyStatus = null;
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        finally
        {
            if (requestId == _historyRequestId && !_closing.IsCancellationRequested)
            {
                _historyLoading = _historyLoadingOlder = false;
                UpdateIncidentHistoryControls();
            }
        }
    }

    private void ReplaceHistoryRows(IEnumerable<ViewIncident> rows)
    {
        var selected = SelectedEventIncident;
        _applyingHistoryRows = true;
        try
        {
            _incidentHistory.Clear();
            foreach (var row in rows) _incidentHistory.Add(row);
            if (!ReferenceEquals(IncidentsGrid.ItemsSource, _incidentHistory)) IncidentsGrid.ItemsSource = _incidentHistory;
            var listed = _incidentHistory.FirstOrDefault(row => row.Id == selected?.Id);
            _historyPinnedIncident = listed is null ? selected : null;
            IncidentsGrid.SelectedItem = listed;
        }
        finally { _applyingHistoryRows = false; }
        RenderSelectedIncidentSummary();
        RenderDetail();
        if (SelectedEventIncident is { } current &&
            (current.UpdatedAtUtc != selected?.UpdatedAtUtc || _selectedDetail is null && !_detailLoading))
            _ = LoadSelectedIncidentAsync();
    }

    private void SelectIncidentFromTriage(string? id) => SelectIncidentForEvidence(id);

    private void SelectIncidentForEvidence(string? id)
    {
        var listed = IncidentsGrid.Items.OfType<ViewIncident>().FirstOrDefault(row => row.Id == id);
        _applyingHistoryRows = true;
        try
        {
            IncidentsGrid.SelectedItem = listed;
            _historyPinnedIncident = listed is null ? _renderedView?.Incidents.FirstOrDefault(row => row.Id == id) : null;
            if (listed is not null) IncidentsGrid.ScrollIntoView(listed);
        }
        finally { _applyingHistoryRows = false; }

        // Selecting the same row raises no SelectionChanged event.
        _ = LoadSelectedIncidentAsync();
    }

    private void UpdateIncidentHistoryControls()
    {
        if (RefreshHistoryButton is null) return;
        var restoreFocus = OlderIncidentsButton.IsKeyboardFocused || RefreshHistoryButton.IsKeyboardFocused && !_historyConnected;
        RefreshHistoryButton.Visibility = OlderIncidentsButton.Visibility =
            _historySupported ? Visibility.Visible : Visibility.Collapsed;
        IncidentCreatedColumn.Visibility = IncidentUpdatedColumn.Visibility = RefreshHistoryButton.Visibility;
        IncidentsGrid.CanUserSortColumns = !_historySupported;
        RefreshHistoryButton.IsEnabled = _historyConnected;
        OlderIncidentsButton.IsEnabled = _historyConnected && _historyNext is not null;
        if (restoreFocus && !OlderIncidentsButton.IsEnabled)
        {
            if (_historySupported && _historyConnected) RefreshHistoryButton.Focus();
            else IncidentsGrid.Focus();
        }
        if (!_historySupported) return;
        var key = !_historyConnected ? "IncidentHistoryDisconnected" :
            _historyStatus ?? (_incidentHistory.Count != 0 ? "IncidentHistoryCount" :
                _historyStarted ? "IncidentHistoryEmpty" : "WaitingForSavedIncidents");
        IncidentsEmptyText.Text = T(key, _incidentHistory.Count);
    }
}
