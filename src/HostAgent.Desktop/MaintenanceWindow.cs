using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using HostAgent.Core.Dashboard;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Maintenance;

namespace HostAgent.Desktop;

internal sealed class MaintenanceWindow : Window
{
    private readonly DashboardController _controller;
    private UserMaintenance _maintenance;
    private readonly TextBlock _selectedFolder = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _defaultScope = new() { Text = "ThePreviewScansOnlyFilesDirectlyInThisAccountSTemporaryFolderItInclu", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock _scopeInfo = new() { Name = "MaintenanceScopeInfo", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _locationDetails = new() { Name = "MaintenanceExactLocationDetails", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8), Visibility = Visibility.Collapsed };
    private readonly TextBlock _restoreInfo = new() { Name = "MaintenanceRestoreTarget", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
    private readonly TextBlock _status = new() { Name = "MaintenanceStatus", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 10) };
    private readonly TextBlock _previewInfo = new() { Name = "MaintenancePreviewInfo", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _diskInfo = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock _noSpaceWarning = new() { Text = "QuarantineDoesNotFreeDiskSpace", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _quarantineBlockReason = new() { Name = "MaintenanceQuarantineBlockReason", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private readonly CancellationTokenSource _closing = new();
    private CancellationTokenSource? _activeOperation;
    private readonly DataGrid _previewGrid = new() { IsReadOnly = true, AutoGenerateColumns = false, Height = 210 };
    private readonly DataGrid _skippedGrid = new() { IsReadOnly = true, AutoGenerateColumns = false, Height = 150 };
    private readonly DataGrid _entriesGrid = new() { IsReadOnly = true, AutoGenerateColumns = false, Height = 180 };
    private readonly DataGrid _resultGrid = new() { IsReadOnly = true, AutoGenerateColumns = false, Height = 180 };
    private readonly Button _chooseFolderButton = new() { Content = "ChooseMaintenanceFolder", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _previewButton = new() { Name = "MaintenancePreviewButton", Content = "CreateSafePreview", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _quarantineButton = new() { Name = "MaintenanceQuarantineButton", Content = "MovePreviewedFilesToQuarantine", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 4) };
    private readonly Button _listButton = new() { Name = "MaintenanceListButton", Content = "RefreshList", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _restoreButton = new() { Name = "MaintenanceRestoreButton", Content = "SelectAQuarantineEntryToRestore", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 4) };
    private readonly Button _exactLocationButton = new() { Name = "MaintenanceExactLocationButton", Content = "ShowExactLocation", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4), Visibility = Visibility.Collapsed };
    private readonly Button _cancelButton = new() { Name = "MaintenanceCancelButton", Content = "CancelMaintenanceAction", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
    private readonly DispatcherTimer _previewExpiryTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string? _targetDrive;
    private long? _initialFreeBytes;
    private DateTimeOffset? _initialFreeMeasuredAtUtc;
    private MaintenancePreview? _preview;
    private readonly DesktopLocalization _language;
    private readonly Func<bool> _developerMode;
    private readonly System.Collections.Generic.List<(DataGridColumn Column, string Source)> _columns = [];
    private readonly System.Collections.Generic.List<DataGridColumn> _developerColumns = [];
    private Func<string>? _statusText;
    private bool _actionInProgress;
    private bool _exactLocationsShown;
    private bool? _lastDeveloperMode;
    private ScrollViewer? _workArea;
    private DataGridTextColumn? _previewHashColumn;
    private string T(string source, params object?[] args) => _language.Text(source, args);
    private CancellationToken CurrentToken => _activeOperation?.Token ?? _closing.Token;
    private void SetStatus(Func<string> text) { _statusText = text; _status.Text = text(); }
    internal MaintenancePreview? CurrentPreview => _preview;
    internal bool ActionInProgress => _actionInProgress;

    internal MaintenanceWindow(DashboardController controller) : this(controller, null) { }

    internal MaintenanceWindow(DashboardController controller, UserMaintenance? maintenance, DesktopLocalization? language = null,
        string? targetDrive = null, Func<bool>? developerMode = null)
    {
        _language = language ?? DesktopLocalization.ForCurrentUser();
        _developerMode = developerMode ?? (() => false);
        NameScope.SetNameScope(this, new NameScope());
        _controller = controller;
        _targetDrive = targetDrive;
        _maintenance = maintenance ?? UserMaintenance.CreateForCurrentUser(ReadFreshStateAsync);
        Title = "HostAgentSafeMaintenance";
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1320, workArea.Width * .92); Height = Math.Min(920, workArea.Height * .90);
        MinWidth = Math.Min(760, Width); MinHeight = Math.Min(620, Height);
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 16;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        AddTextColumn(_previewGrid, "Name", nameof(MaintenanceItem.Name), 250);
        AddTextColumn(_previewGrid, "Size", nameof(MaintenanceItem.Bytes), 100);
        AddTextColumn(_previewGrid, "Modified", nameof(MaintenanceItem.ModifiedAtUtc), 170);
        _previewHashColumn = AddTextColumn(_previewGrid, "SHA-256", nameof(MaintenanceItem.Sha256), 230);
        AddTextColumn(_skippedGrid, "Name", nameof(MaintenanceSkippedItem.Name), 250);
        AddTextColumn(_skippedGrid, "Reason", nameof(MaintenanceSkippedItem.ReasonCode), 300);
        AddTextColumn(_entriesGrid, "Name", nameof(QuarantineItem.Name), 230);
        AddTextColumn(_entriesGrid, "Size", nameof(QuarantineItem.Bytes), 90);
        AddTextColumn(_entriesGrid, "Status", nameof(QuarantineItem.State), 130);
        _developerColumns.Add(AddTextColumn(_entriesGrid, "RestoreID", nameof(QuarantineItem.Id), 280));
        AddTextColumn(_resultGrid, "Name", nameof(MaintenanceActionItem.Name), 170);
        AddTextColumn(_resultGrid, "Status", nameof(MaintenanceActionItem.State), 120);
        AddTextColumn(_resultGrid, "Reason", nameof(MaintenanceActionItem.ReasonCode), 160);
        _developerColumns.Add(AddTextColumn(_resultGrid, "Source", nameof(MaintenanceActionItem.SourcePath), 260));
        _developerColumns.Add(AddTextColumn(_resultGrid, "Destination", nameof(MaintenanceActionItem.DestinationPath), 260));
        _developerColumns.Add(AddTextColumn(_resultGrid, "RestoreID", nameof(MaintenanceActionItem.Id), 280));

        RegisterName("MaintenanceFolderButton", _chooseFolderButton);
        RegisterName("MaintenancePreviewGrid", _previewGrid);
        RegisterName("MaintenanceSkippedGrid", _skippedGrid);
        RegisterName("MaintenanceEntriesGrid", _entriesGrid);
        RegisterName("MaintenanceResultGrid", _resultGrid);
        RegisterName("MaintenancePreviewButton", _previewButton);
        RegisterName("MaintenanceQuarantineButton", _quarantineButton);
        RegisterName("MaintenanceQuarantineBlockReason", _quarantineBlockReason);
        RegisterName("MaintenanceListButton", _listButton);
        RegisterName("MaintenanceRestoreButton", _restoreButton);
        RegisterName("MaintenanceCancelButton", _cancelButton);
        RegisterName("MaintenanceStatus", _status);
        RegisterName("MaintenancePreviewInfo", _previewInfo);
        RegisterName("MaintenanceScopeInfo", _scopeInfo);
        RegisterName("MaintenanceExactLocationDetails", _locationDetails);
        RegisterName("MaintenanceExactLocationButton", _exactLocationButton);
        RegisterName("MaintenanceRestoreTarget", _restoreInfo);
        RegisterName("MaintenanceNoSpaceWarning", _noSpaceWarning);

        var content = new StackPanel { Margin = new Thickness(22) };
        content.Children.Add(new TextBlock { Text = "MaintenanceWindowIntro", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        _chooseFolderButton.Click += async (_, _) => await RunActionAsync(ChooseFolderAsync);
        content.Children.Add(_chooseFolderButton);
        _selectedFolder.Margin = new Thickness(0, 4, 0, 10);
        content.Children.Add(_selectedFolder);
        content.Children.Add(new TextBlock { Text = "TemporaryFiles", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        content.Children.Add(_defaultScope);
        if (_targetDrive is not null) content.Children.Add(_diskInfo);
        _previewButton.Click += async (_, _) => await RunActionAsync(PreviewAsync);
        content.Children.Add(_previewButton);
        content.Children.Add(_previewInfo);
        content.Children.Add(_scopeInfo);
        content.Children.Add(_exactLocationButton);
        content.Children.Add(_locationDetails);
        content.Children.Add(_previewGrid);
        content.Children.Add(new TextBlock { Text = "SkippedItemsAndReasons", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
        content.Children.Add(_skippedGrid);
        content.Children.Add(_noSpaceWarning);
        content.Children.Add(new TextBlock { Text = "ReviewExactPreviewBeforeQuarantine", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) });
        content.Children.Add(_quarantineBlockReason);
        _quarantineButton.Click += async (_, _) => await RunActionAsync(QuarantineAsync);
        content.Children.Add(_quarantineButton);

        content.Children.Add(new TextBlock { Text = "QuarantineAndRestore", FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 8) });
        _listButton.Click += async (_, _) => await RunActionAsync(RefreshListAsync);
        content.Children.Add(_listButton);
        content.Children.Add(_entriesGrid);
        content.Children.Add(_restoreInfo);
        _restoreButton.Click += async (_, _) => await RunActionAsync(RestoreAsync);
        content.Children.Add(_restoreButton);
        content.Children.Add(new TextBlock { Text = "ActionReceipts", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        content.Children.Add(_resultGrid);
        content.Children.Add(_status);

        var root = new DockPanel();
        DockPanel.SetDock(_cancelButton, Dock.Bottom);
        root.Children.Add(_cancelButton);
        _workArea = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content };
        root.Children.Add(_workArea);
        Content = root;
        _cancelButton.Click += (_, _) => RequestCancellation();
        _exactLocationButton.Click += (_, _) => { _exactLocationsShown = !_exactLocationsShown; UpdateExactLocationSurface(); };
        _previewExpiryTimer.Tick += (_, _) => UpdateActionLabels();
        _previewExpiryTimer.Start();
        _entriesGrid.SelectionChanged += (_, _) => UpdateRestoreTarget();
        RefreshModeSurface();
        _language.Apply(this); _language.BindStaticText(this);
        _language.Changed += LanguageChanged;
        Closed += (_, _) => { _previewExpiryTimer.Stop(); _language.Changed -= LanguageChanged; _closing.Cancel(); _activeOperation?.Cancel(); };
        Loaded += async (_, _) => await RunActionAsync(async () =>
        {
            if (_targetDrive is not null)
            {
                _initialFreeMeasuredAtUtc = DateTimeOffset.UtcNow;
                _initialFreeBytes = ReadFreeBytes(_targetDrive);
                RenderDiskMeasurement();
                await PreviewAsync();
            }
            await RefreshListAsync();
        });
    }

    private async Task ChooseFolderAsync()
    {
        if (!EnsureDeveloperMode()) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        if (!EnsureDeveloperMode()) return;
        try
        {
            _maintenance = UserMaintenance.CreateForSelectedDirectory(dialog.FolderName, ReadFreshStateAsync);
            _selectedFolder.Text = dialog.FolderName;
            _defaultScope.Visibility = Visibility.Collapsed;
            ClearPreviewAndResults();
            SetStatus(() => T("SelectedFolderMaintenanceScope"));
            RefreshModeSurface();
            await RefreshListAsync();
        }
        catch { SetStatus(() => T("SelectedFolderUnavailable")); }
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        if (_actionInProgress) return;
        if (!EnsureScopeAllowed()) return;
        _actionInProgress = true;
        _activeOperation = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        if (_workArea is not null) _workArea.IsEnabled = false;
        _cancelButton.IsEnabled = true;
        try { await action(); }
        catch (OperationCanceledException) when (CurrentToken.IsCancellationRequested)
        {
            if (!_closing.IsCancellationRequested) SetStatus(() => T("MaintenanceOperationCancelled"));
        }
        finally
        {
            _activeOperation.Dispose();
            _activeOperation = null;
            _actionInProgress = false;
            if (_workArea is not null) _workArea.IsEnabled = true;
            _cancelButton.IsEnabled = false;
            UpdateRestoreTarget();
        }
    }

    private void RequestCancellation()
    {
        if (_activeOperation is null) return;
        _activeOperation.Cancel();
        _cancelButton.IsEnabled = false;
        SetStatus(() => T("MaintenanceCancellationRequested"));
    }

    private bool EnsureDeveloperMode()
    {
        if (_developerMode()) return true;
        SetStatus(() => T("DeveloperModeRequired"));
        return false;
    }

    private bool EnsureScopeAllowed()
    {
        if (!_maintenance.IsSelectedDirectory || _developerMode()) return true;
        _maintenance = UserMaintenance.CreateForCurrentUser(ReadFreshStateAsync);
        _selectedFolder.Text = string.Empty;
        _defaultScope.Visibility = Visibility.Visible;
        ClearPreviewAndResults();
        RefreshModeSurface();
        SetStatus(() => T("MaintenanceScopeResetToDefault"));
        return false;
    }

    private void RefreshModeSurface()
    {
        var developer = _developerMode();
        if (_lastDeveloperMode == true && !developer) _exactLocationsShown = false;
        _lastDeveloperMode = developer;
        _chooseFolderButton.Visibility = developer ? Visibility.Visible : Visibility.Collapsed;
        _selectedFolder.Visibility = developer && _maintenance.IsSelectedDirectory ? Visibility.Visible : Visibility.Collapsed;
        if (_previewHashColumn is not null) _previewHashColumn.Visibility = developer ? Visibility.Visible : Visibility.Collapsed;
        foreach (var column in _developerColumns) column.Visibility = developer ? Visibility.Visible : Visibility.Collapsed;
        UpdateExactLocationSurface();
        UpdateActionLabels();
    }

    private void ClearPreviewAndResults()
    {
        _preview = null;
        _exactLocationsShown = false;
        _previewGrid.ItemsSource = null;
        _skippedGrid.ItemsSource = null;
        _entriesGrid.ItemsSource = null;
        _resultGrid.ItemsSource = null;
        _previewInfo.Text = string.Empty;
        _scopeInfo.Text = string.Empty;
        UpdateExactLocationSurface();
        UpdateActionLabels();
    }

    private void LanguageChanged(object? sender, EventArgs e)
    {
        _language.Apply(this);
        foreach (var (column, source) in _columns) column.Header = source == "SHA-256" ? source : T(source);
        RefreshModeSurface();
        RenderPreviewDetails();
        UpdateRestoreTarget();
        if (_statusText is not null) _status.Text = _statusText();
        if (_targetDrive is not null) RenderDiskMeasurement();
        _previewGrid.Items.Refresh(); _skippedGrid.Items.Refresh(); _entriesGrid.Items.Refresh(); _resultGrid.Items.Refresh();
    }

    private async ValueTask<DashboardSnapshot?> ReadFreshStateAsync(CancellationToken token)
    {
        await _controller.RefreshAsync(token);
        return _controller.Snapshot;
    }

    private async Task PreviewAsync()
    {
        if (!EnsureScopeAllowed()) return;
        try
        {
            _preview = await _maintenance.PreviewAsync(CurrentToken);
            _previewGrid.ItemsSource = _preview.Items;
            _skippedGrid.ItemsSource = _preview.SkippedItems;
            RenderPreviewDetails();
            var preview = _preview;
            SetStatus(() => preview.Items.Count == 0 ? T("NoEligibleFilesWereFoundThePreviewMakesNoChanges") : T("ThePreviewMadeNoChangesReviewEveryListedFileBeforeMovingIt"));
            UpdateActionLabels();
        }
        catch (OperationCanceledException) when (CurrentToken.IsCancellationRequested) { SetStatus(() => T("MaintenanceOperationCancelled")); }
        catch { SetStatus(() => T("CouldNotCreateThePreviewFilesWereNotChanged")); }
    }

    private void RenderPreviewDetails()
    {
        if (_preview is not { } preview)
        {
            _previewInfo.Text = string.Empty;
            _scopeInfo.Text = string.Empty;
            UpdateExactLocationSurface();
            return;
        }
        var developer = _developerMode();
        var status = TechnicalText.Format(preview.Code, _language.Culture, developer);
        var truncated = preview.Truncated ? " · " + T("PreviewLimitReached") : string.Empty;
        var technical = developer
            ? T("PreviewDeveloperTechnicalDetails", preview.Id, preview.ScopeVolumeId?.ToString("X8") ?? T("Unknown"))
            : string.Empty;
        _previewInfo.Text = T("MaintenancePreviewStatus", status, preview.Items.Count, preview.Skipped,
            preview.ExpiresAtUtc.ToLocalTime(), truncated, technical);
        var limits = preview.EligibilityLimits;
        var ageRule = limits.MinimumAgeDays == 0 ? T("NoMinimumAge") : T("MinimumAgeDays", limits.MinimumAgeDays);
        _scopeInfo.Text = T("MaintenancePreviewDetails", preview.InspectedFiles, preview.Items.Count, preview.Skipped,
            preview.Truncated ? T("PreviewIncomplete") : T("PreviewComplete"),
            limits.TopLevelOnly ? T("TopLevelFilesOnly") : T("RecursiveScope"), ageRule,
            FormatBytes(limits.MaximumFileBytes), FormatBytes(limits.MaximumTotalBytes), limits.MaximumQuarantineEntries,
            limits.MaximumInspectedFiles, limits.MaximumCandidates, limits.PreviewTimeoutSeconds);
        UpdateExactLocationSurface();
    }

    private async Task QuarantineAsync()
    {
        if (!EnsureScopeAllowed()) return;
        if (_preview is null) { SetStatus(() => T("FirstCreateACurrentPreview")); return; }
        try
        {
            var preview = _preview;
            var result = await _maintenance.QuarantineAsync(preview.Id, preview.Id, CurrentToken);
            RenderResult(result);
            _preview = null;
            _previewInfo.Text = string.Empty;
            _scopeInfo.Text = string.Empty;
            ClearPreviewTablesAfterQuarantine();
            await RefreshListAsync();
        }
        catch (OperationCanceledException) when (CurrentToken.IsCancellationRequested) { SetStatus(() => T("MaintenanceOperationCancelled")); }
        catch { SetStatus(() => T("CouldNotCompleteTheRequestedMoveReloadThePreviewAndServiceStatus")); }
    }

    private void ClearPreviewTablesAfterQuarantine()
    {
        _previewGrid.ItemsSource = null;
        _skippedGrid.ItemsSource = null;
        UpdateExactLocationSurface();
        UpdateActionLabels();
    }

    private async Task RefreshListAsync()
    {
        if (!EnsureScopeAllowed()) return;
        try
        {
            var selected = (_entriesGrid.SelectedItem as QuarantineItem)?.Id;
            var entries = await _maintenance.ListAsync(CurrentToken);
            _entriesGrid.ItemsSource = entries;
            if (selected is not null) _entriesGrid.SelectedItem = entries.FirstOrDefault(item => item.Id == selected);
            UpdateRestoreTarget();
        }
        catch (OperationCanceledException) when (CurrentToken.IsCancellationRequested) { }
        catch { SetStatus(() => T("TheQuarantineListIsUnavailableOrItsRecordsRequireReview")); }
    }

    private async Task RestoreAsync()
    {
        if (!EnsureScopeAllowed()) return;
        if (_entriesGrid.SelectedItem is not QuarantineItem item) { SetStatus(() => T("SelectAnItemInQuarantine")); return; }
        if (item.State is not ("Quarantined" or "Intent" or "RestoreIntent"))
        { SetStatus(() => T("SelectedQuarantineEntryNeedsReview")); return; }
        try
        {
            // Selection plus the path-specific button is the user's exact, single-item restore confirmation.
            var result = await _maintenance.RestoreAsync(item.Id, item.Id, CurrentToken);
            RenderResult(result);
            await RefreshListAsync();
        }
        catch (OperationCanceledException) when (CurrentToken.IsCancellationRequested) { SetStatus(() => T("MaintenanceOperationCancelled")); }
        catch { SetStatus(() => T("CouldNotRestoreTheFileReloadTheListAndCheckTheDestinationPath")); }
    }

    private void RenderResult(MaintenanceResult result)
    {
        _resultGrid.ItemsSource = result.Items;
        SetStatus(() => T("MaintenanceActionResultSummary", result.Moved, result.Restored, result.Skipped,
            result.Blocked, result.Failed, result.Cancelled, result.Unknown, result.JournalWarnings,
            _developerMode() ? T("MaintenanceActionResultDeveloperDetails", result.Code) : string.Empty));
    }

    private void UpdateActionLabels()
    {
        if (_preview is not { } preview)
        {
            _quarantineButton.Content = T("MovePreviewToQuarantine");
            _quarantineButton.IsEnabled = false;
            _quarantineBlockReason.Text = T("CreatePreviewBeforeQuarantine");
        }
        else if (preview.Truncated)
        {
            _quarantineButton.Content = T("MovePreviewedFilesToQuarantine", preview.Items.Count);
            _quarantineButton.IsEnabled = false;
            _quarantineBlockReason.Text = T("IncompletePreviewBlocksQuarantine");
        }
        else if (DateTimeOffset.UtcNow >= preview.ExpiresAtUtc)
        {
            _quarantineButton.Content = T("MovePreviewedFilesToQuarantine", preview.Items.Count);
            _quarantineButton.IsEnabled = false;
            _quarantineBlockReason.Text = T("ExpiredPreviewBlocksQuarantine");
        }
        else if (preview.Items.Count == 0)
        {
            _quarantineButton.Content = T("MovePreviewedFilesToQuarantine", 0);
            _quarantineButton.IsEnabled = false;
            _quarantineBlockReason.Text = T("NoEligibleFilesToQuarantine");
        }
        else
        {
            _quarantineButton.Content = T("MovePreviewedFilesToQuarantine", preview.Items.Count);
            _quarantineButton.IsEnabled = CanQuarantinePreview(preview, DateTimeOffset.UtcNow);
            _quarantineBlockReason.Text = string.Empty;
        }
        UpdateRestoreTarget();
    }

    internal static bool CanQuarantinePreview(MaintenancePreview? preview, DateTimeOffset now)
        => preview is { Truncated: false, Items.Count: > 0 } && now < preview.ExpiresAtUtc;

    private void UpdateRestoreTarget()
    {
        if (_entriesGrid.SelectedItem is not QuarantineItem item)
        {
            _restoreInfo.Text = T("SelectAnItemInQuarantine");
            _restoreButton.Content = T("SelectAQuarantineEntryToRestore");
            _restoreButton.IsEnabled = false;
            UpdateExactLocationSurface();
            return;
        }
        var destination = Path.Combine(_maintenance.ScopePath, item.Name);
        _restoreInfo.Text = _developerMode() ? T("RestoreDestinationPath", destination) : T("RestoreToOriginalLocation");
        _restoreButton.Content = T("RestoreFileToOriginalPath", item.Name, _developerMode() ? destination : T("OriginalLocation"));
        _restoreButton.IsEnabled = item.State is "Quarantined" or "Intent" or "RestoreIntent";
        UpdateExactLocationSurface();
    }

    private void UpdateExactLocationSurface()
    {
        var details = new System.Collections.Generic.List<string>();
        if (_preview is { } preview)
            details.Add(T("MaintenancePreviewLocationDetails", preview.ScopePath, preview.QuarantinePath));
        if (_entriesGrid.SelectedItem is QuarantineItem item)
            details.Add(T("RestoreDestinationPath", Path.Combine(_maintenance.ScopePath, item.Name)));
        var hasDetails = details.Count > 0;
        var developer = _developerMode();
        _locationDetails.Text = string.Join(Environment.NewLine, details);
        _locationDetails.Visibility = hasDetails && (developer || _exactLocationsShown) ? Visibility.Visible : Visibility.Collapsed;
        _exactLocationButton.Visibility = hasDetails && !developer ? Visibility.Visible : Visibility.Collapsed;
        _exactLocationButton.Content = T(_exactLocationsShown ? "HideExactLocation" : "ShowExactLocation");
    }

    private DataGridTextColumn AddTextColumn(DataGrid grid, string title, string property, double width)
    {
        var binding = new Binding(property);
        if (property == nameof(MaintenanceItem.ModifiedAtUtc)) binding.StringFormat = "g";
        if (property == nameof(MaintenanceItem.Bytes) || property == nameof(QuarantineItem.Bytes) || property == nameof(MaintenanceActionItem.Bytes))
            binding.Converter = new ByteSizeConverter();
        if (property == nameof(MaintenanceSkippedItem.ReasonCode) || property == nameof(MaintenanceActionItem.ReasonCode))
            binding.Converter = new MaintenanceReasonConverter(code => TechnicalText.FormatMaintenanceReason(code, _language.Culture, _developerMode()));
        if (property == nameof(MaintenanceActionItem.State))
            binding.Converter = new MaintenanceActionStateConverter(state => TechnicalText.FormatMaintenanceActionState(state, _language.Culture, _developerMode()));
        if (property == nameof(QuarantineItem.State))
            binding.Converter = new TechnicalTextConverter();
        if (grid == _entriesGrid && property == nameof(QuarantineItem.Name)) binding = new Binding { Converter = new MaintenanceNameConverter() };
        var column = new DataGridTextColumn { Header = title == "SHA-256" ? title : T(title), Binding = binding, Width = new DataGridLength(width, DataGridLengthUnitType.Star) };
        _columns.Add((column, title)); grid.Columns.Add(column);
        return column;
    }

    private string FormatBytes(long bytes) => (bytes / (1024d * 1024d)).ToString("N0", _language.Culture) + " MiB";

    private long? ReadFreeBytes(string drive)
    {
        try { return new DriveInfo(drive).TotalFreeSpace; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void RenderDiskMeasurement()
    {
        string Format(long? bytes) => bytes is long value ? value.ToString("N0", _language.Culture) + " B" : T("Unknown");
        _diskInfo.Text = T("DiskMaintenanceBefore", _targetDrive, Format(_initialFreeBytes), _initialFreeMeasuredAtUtc?.ToLocalTime());
    }

    internal static bool MatchesDefaultVolume(string drive)
    {
        if (drive.Length != 2 || !char.IsAsciiLetter(drive[0]) || drive[1] != ':') return false;
        var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        return string.Equals(root?.TrimEnd('\\'), drive, StringComparison.OrdinalIgnoreCase);
    }

    private void RegisterName(string name, FrameworkElement element) => RegisterName(name, (object)element);

    private sealed class MaintenanceReasonConverter(Func<string, string> format) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => format(value?.ToString() ?? string.Empty);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    private sealed class MaintenanceActionStateConverter(Func<string, string> format) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => format(value?.ToString() ?? string.Empty);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
