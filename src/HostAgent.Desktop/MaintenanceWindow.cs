using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Threading;
using System.Threading.Tasks;
using HostAgent.Core.Dashboard;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Maintenance;

namespace HostAgent.Desktop;

internal sealed class MaintenanceWindow : Window
{
    private readonly DashboardController _controller;
    private readonly UserMaintenance _maintenance;
    private readonly CancellationTokenSource _closing = new();
    private readonly DataGrid _previewGrid = new() { IsReadOnly = true, AutoGenerateColumns = false, Height = 210 };
    private readonly DataGrid _entriesGrid = new() { IsReadOnly = true, AutoGenerateColumns = false, Height = 180 };
    private readonly TextBox _confirmation = new() { Name = "MaintenanceApproval", MinWidth = 320 };
    private readonly TextBox _restoreConfirmation = new() { Name = "MaintenanceRestoreApproval", MinWidth = 320 };
    private readonly TextBlock _status = new() { Name = "MaintenanceStatus", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 10) };
    private readonly TextBlock _previewInfo = new() { Name = "MaintenancePreviewInfo", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private MaintenancePreview? _preview;
    private readonly DesktopLocalization _language;
    private readonly System.Collections.Generic.List<(DataGridColumn Column,string Source)> _columns=[];
    private Func<string>? _statusText, _previewText;
    private string T(string source,params object?[] args)=>_language.Text(source,args);
    private void SetStatus(Func<string> text){_statusText=text;_status.Text=text();}
    internal MaintenancePreview? CurrentPreview => _preview;

    internal MaintenanceWindow(DashboardController controller) : this(controller, null) { }

    internal MaintenanceWindow(DashboardController controller, UserMaintenance? maintenance, DesktopLocalization? language=null)
    {
        _language=language??DesktopLocalization.ForCurrentUser();
        NameScope.SetNameScope(this, new NameScope());
        _controller = controller;
        _maintenance = maintenance ?? UserMaintenance.CreateForCurrentUser(ReadFreshStateAsync);
        Title = "HostAgentSafeMaintenance";
        Width = 960; Height = 800; MinWidth = 760; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        AddTextColumn(_previewGrid, "Name", nameof(MaintenanceItem.Name), 290);
        AddTextColumn(_previewGrid, "Size", nameof(MaintenanceItem.Bytes), 110);
        AddTextColumn(_previewGrid, "Modified", nameof(MaintenanceItem.ModifiedAtUtc), 220);
        AddTextColumn(_previewGrid, "SHA-256", nameof(MaintenanceItem.Sha256), 270);
        AddTextColumn(_entriesGrid, "Name", nameof(QuarantineItem.Name), 260);
        AddTextColumn(_entriesGrid, "Size", nameof(QuarantineItem.Bytes), 110);
        AddTextColumn(_entriesGrid, "Status", nameof(QuarantineItem.State), 150);
        AddTextColumn(_entriesGrid, "RestoreID", nameof(QuarantineItem.Id), 310);
        RegisterName("MaintenancePreviewGrid", _previewGrid);
        RegisterName("MaintenanceEntriesGrid", _entriesGrid);
        RegisterName("MaintenanceApproval", _confirmation);
        RegisterName("MaintenanceRestoreApproval", _restoreConfirmation);
        RegisterName("MaintenanceStatus", _status);
        RegisterName("MaintenancePreviewInfo", _previewInfo);

        var content = new StackPanel { Margin = new Thickness(22) };
        content.Children.Add(new TextBlock { Text = "TemporaryFiles", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        content.Children.Add(new TextBlock
        {
            Text = "ThePreviewScansOnlyFilesDirectlyInThisAccountSTemporaryFolderItInclu",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });
        var previewButton = new Button { Name = "MaintenancePreviewButton", Content = "CreateSafePreview", HorizontalAlignment = HorizontalAlignment.Left };
        RegisterName("MaintenancePreviewButton", previewButton);
        previewButton.Click += async (_, _) => await PreviewAsync();
        content.Children.Add(previewButton);
        content.Children.Add(_previewInfo);
        content.Children.Add(_previewGrid);
        content.Children.Add(new TextBlock { Text = "ApprovalForThisExactPreview", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        content.Children.Add(new TextBlock { Text = "CopyThePreviewIDShownAboveEachBatchRequiresANewPreviewAndItsExactID", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(_confirmation);
        var quarantineButton = new Button { Name = "MaintenanceQuarantineButton", Content = "MoveAllPreviewedFilesToQuarantine", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 4) };
        RegisterName("MaintenanceQuarantineButton", quarantineButton);
        quarantineButton.Click += async (_, _) => await QuarantineAsync();
        content.Children.Add(quarantineButton);

        content.Children.Add(new TextBlock { Text = "QuarantineAndRestore", FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 8) });
        var listButton = new Button { Name = "MaintenanceListButton", Content = "RefreshList", HorizontalAlignment = HorizontalAlignment.Left };
        RegisterName("MaintenanceListButton", listButton);
        listButton.Click += async (_, _) => await RefreshListAsync();
        content.Children.Add(listButton);
        content.Children.Add(_entriesGrid);
        content.Children.Add(new TextBlock { Text = "EnterTheExactRestoreIDOfTheSelectedRecord", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        content.Children.Add(_restoreConfirmation);
        var restoreButton = new Button { Name = "MaintenanceRestoreButton", Content = "RestoreWithoutOverwritingAnExistingFile", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 4) };
        RegisterName("MaintenanceRestoreButton", restoreButton);
        restoreButton.Click += async (_, _) => await RestoreAsync();
        content.Children.Add(restoreButton);
        content.Children.Add(_status);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content };
        Content = scroll;
        _entriesGrid.SelectionChanged += (_, _) => _restoreConfirmation.Clear();
        _language.Apply(this);_language.BindStaticText(this);
        System.Windows.Automation.AutomationProperties.SetName(_confirmation,T("ApprovalForThisExactPreview"));
        System.Windows.Automation.AutomationProperties.SetName(_restoreConfirmation,T("EnterTheExactRestoreIDOfTheSelectedRecord"));
        _language.Changed+=LanguageChanged;
        Closed += (_, _) => {_language.Changed-=LanguageChanged;_closing.Cancel();};
        Loaded += async (_, _) => await RefreshListAsync();
    }
    private void LanguageChanged(object? sender,EventArgs e)
    {
        _language.Apply(this);
        foreach(var (column,source) in _columns)column.Header=source=="SHA-256"?source:T(source);
        if(_statusText is not null)_status.Text=_statusText();
        if(_previewText is not null)_previewInfo.Text=_previewText();
        System.Windows.Automation.AutomationProperties.SetName(_confirmation,T("ApprovalForThisExactPreview"));
        System.Windows.Automation.AutomationProperties.SetName(_restoreConfirmation,T("EnterTheExactRestoreIDOfTheSelectedRecord"));
        _previewGrid.Items.Refresh();_entriesGrid.Items.Refresh();
    }

    private async ValueTask<DashboardSnapshot?> ReadFreshStateAsync(CancellationToken token)
    {
        await _controller.RefreshAsync(token);
        return _controller.Snapshot;
    }

    private async Task PreviewAsync()
    {
        try
        {
            _preview = await _maintenance.PreviewAsync(_closing.Token);
            _previewGrid.ItemsSource = _preview.Items;
            _confirmation.Clear();
            var preview=_preview;
            _previewText=()=>T("StatusSelectedSkippedPreviewIDValidUntil",TechnicalText.Format(preview.Code,_language.Culture,true),preview.Items.Count,preview.Skipped,(preview.Truncated ? T("PreviewLimitReached") : ""),preview.Id,preview.ExpiresAtUtc.ToLocalTime());
            _previewInfo.Text=_previewText();
            SetStatus(() => preview.Items.Count == 0 ? T("NoEligibleFilesWereFoundThePreviewMakesNoChanges") : T("ThePreviewMadeNoChangesReviewEveryListedFileBeforeMovingIt"));
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch { SetStatus(() => T("CouldNotCreateThePreviewFilesWereNotChanged")); }
    }

    private async Task QuarantineAsync()
    {
        if (_preview is null) { SetStatus(() => T("FirstCreateACurrentPreview")); return; }
        try
        {
            var result = await _maintenance.QuarantineAsync(_preview.Id, _confirmation.Text, _closing.Token);
            SetStatus(() => T("ResultMovedSkippedTotalBytesRecordsRequiringRecoveryQuarantineDoesNo",TechnicalText.Format(result.Code,_language.Culture,true),result.Moved,result.Skipped,result.Bytes,result.JournalWarnings));
            _preview = null;
            _confirmation.Clear();
            await RefreshListAsync();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch { SetStatus(() => T("CouldNotCompleteTheRequestedMoveReloadThePreviewAndServiceStatus")); }
    }

    private async Task RefreshListAsync()
    {
        try { _entriesGrid.ItemsSource = await _maintenance.ListAsync(_closing.Token); }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch { SetStatus(() => T("TheQuarantineListIsUnavailableOrItsRecordsRequireReview")); }
    }

    private async Task RestoreAsync()
    {
        if (_entriesGrid.SelectedItem is not QuarantineItem item) { SetStatus(() => T("SelectAnItemInQuarantine")); return; }
        try
        {
            var result = await _maintenance.RestoreAsync(item.Id, _restoreConfirmation.Text, _closing.Token);
            SetStatus(() => T("RestoreResultRestoredSizeBytesExistingFilesAreNotOverwritten",TechnicalText.Format(result.Code,_language.Culture,true),result.Moved,result.Bytes));
            await RefreshListAsync();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch { SetStatus(() => T("CouldNotRestoreTheFileReloadTheListAndCheckTheDestinationPath")); }
    }

    private void AddTextColumn(DataGrid grid, string title, string property, double width)
    {
        var binding=new Binding(property);
        if(property==nameof(MaintenanceItem.ModifiedAtUtc))binding.StringFormat="g";
        if(property==nameof(MaintenanceItem.Bytes))binding.Converter=new ByteSizeConverter();
        if(property==nameof(QuarantineItem.State))binding.Converter=new TechnicalTextConverter();
        if(grid==_entriesGrid&&property==nameof(QuarantineItem.Name)){binding=new Binding{Converter=new MaintenanceNameConverter()};}
        var column=new DataGridTextColumn{Header=title=="SHA-256"?title:T(title),Binding=binding,Width=new DataGridLength(width)};
        _columns.Add((column,title));grid.Columns.Add(column);
    }

    private void RegisterName(string name, FrameworkElement element) => RegisterName(name, (object)element);
}
