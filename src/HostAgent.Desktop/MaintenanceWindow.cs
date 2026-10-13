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
    internal MaintenancePreview? CurrentPreview => _preview;

    internal MaintenanceWindow(DashboardController controller) : this(controller, null) { }

    internal MaintenanceWindow(DashboardController controller, UserMaintenance? maintenance)
    {
        NameScope.SetNameScope(this, new NameScope());
        _controller = controller;
        _maintenance = maintenance ?? UserMaintenance.CreateForCurrentUser(ReadFreshStateAsync);
        Title = "Host Agent • Bezpečná údržba";
        Width = 960; Height = 800; MinWidth = 760; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        AddTextColumn(_previewGrid, "Název", nameof(MaintenanceItem.Name), 290);
        AddTextColumn(_previewGrid, "Velikost", nameof(MaintenanceItem.Bytes), 110);
        AddTextColumn(_previewGrid, "Změněno", nameof(MaintenanceItem.ModifiedAtUtc), 220);
        AddTextColumn(_previewGrid, "SHA-256", nameof(MaintenanceItem.Sha256), 270);
        AddTextColumn(_entriesGrid, "Název", nameof(QuarantineItem.Name), 260);
        AddTextColumn(_entriesGrid, "Velikost", nameof(QuarantineItem.Bytes), 110);
        AddTextColumn(_entriesGrid, "Stav", nameof(QuarantineItem.State), 150);
        AddTextColumn(_entriesGrid, "ID obnovení", nameof(QuarantineItem.Id), 310);
        RegisterName("MaintenancePreviewGrid", _previewGrid);
        RegisterName("MaintenanceEntriesGrid", _entriesGrid);
        RegisterName("MaintenanceApproval", _confirmation);
        RegisterName("MaintenanceRestoreApproval", _restoreConfirmation);
        RegisterName("MaintenanceStatus", _status);
        RegisterName("MaintenancePreviewInfo", _previewInfo);

        var content = new StackPanel { Margin = new Thickness(22) };
        content.Children.Add(new TextBlock { Text = "Dočasné soubory", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        content.Children.Add(new TextBlock
        {
            Text = "Náhled prohledá pouze soubory přímo v dočasné složce tohoto účtu. Zahrne běžné soubory starší 7 dnů do 16 MiB; maximálně 100 souborů. Přesun do karantény neuvolní místo. Soubory se nemažou automaticky a lze je ručně obnovit, pokud původní cesta zůstala volná.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });
        var previewButton = new Button { Name = "MaintenancePreviewButton", Content = "Vytvořit bezpečný náhled", HorizontalAlignment = HorizontalAlignment.Left };
        RegisterName("MaintenancePreviewButton", previewButton);
        previewButton.Click += async (_, _) => await PreviewAsync();
        content.Children.Add(previewButton);
        content.Children.Add(_previewInfo);
        content.Children.Add(_previewGrid);
        content.Children.Add(new TextBlock { Text = "Schválení přesně pro tento náhled", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        content.Children.Add(new TextBlock { Text = "Zkopírujte ID náhledu uvedené výše. Každá dávka vyžaduje nový náhled a přesné ID.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(_confirmation);
        var quarantineButton = new Button { Name = "MaintenanceQuarantineButton", Content = "Přesunout všechny soubory z náhledu do karantény", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 4) };
        RegisterName("MaintenanceQuarantineButton", quarantineButton);
        quarantineButton.Click += async (_, _) => await QuarantineAsync();
        content.Children.Add(quarantineButton);

        content.Children.Add(new TextBlock { Text = "Karanténa a obnovení", FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 8) });
        var listButton = new Button { Name = "MaintenanceListButton", Content = "Obnovit seznam", HorizontalAlignment = HorizontalAlignment.Left };
        RegisterName("MaintenanceListButton", listButton);
        listButton.Click += async (_, _) => await RefreshListAsync();
        content.Children.Add(listButton);
        content.Children.Add(_entriesGrid);
        content.Children.Add(new TextBlock { Text = "Zadejte přesné ID obnovení vybraného záznamu", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        content.Children.Add(_restoreConfirmation);
        var restoreButton = new Button { Name = "MaintenanceRestoreButton", Content = "Obnovit bez přepsání existujícího souboru", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 4) };
        RegisterName("MaintenanceRestoreButton", restoreButton);
        restoreButton.Click += async (_, _) => await RestoreAsync();
        content.Children.Add(restoreButton);
        content.Children.Add(_status);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content };
        Content = scroll;
        _entriesGrid.SelectionChanged += (_, _) => _restoreConfirmation.Clear();
        Closed += (_, _) => _closing.Cancel();
        Loaded += async (_, _) => await RefreshListAsync();
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
            _previewInfo.Text = $"Stav: {_preview.Code}. Vybráno {_preview.Items.Count}, přeskočeno {_preview.Skipped}{(_preview.Truncated ? ", dosažen limit náhledu" : "")}. ID náhledu: {_preview.Id}. Platnost do: {_preview.ExpiresAtUtc.ToLocalTime():g}.";
            _status.Text = _preview.Items.Count == 0 ? "Nebyly nalezeny vhodné soubory. Náhled nic nemění." : "Náhled nic nezměnil. Před přesunem zkontrolujte každý uvedený soubor.";
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch { _status.Text = "Náhled se nepodařilo vytvořit. Soubory nebyly změněny."; }
    }

    private async Task QuarantineAsync()
    {
        if (_preview is null) { _status.Text = "Nejprve vytvořte aktuální náhled."; return; }
        try
        {
            var result = await _maintenance.QuarantineAsync(_preview.Id, _confirmation.Text, _closing.Token);
            _status.Text = $"Výsledek: {result.Code}. Přesunuto {result.Moved}, přeskočeno {result.Skipped}, celkem {result.Bytes} bajtů. Záznamy vyžadující obnovu: {result.JournalWarnings}. Karanténa neuvolňuje místo.";
            _preview = null;
            _confirmation.Clear();
            await RefreshListAsync();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch { _status.Text = "Požadovaný přesun se nepodařilo dokončit. Znovu načtěte náhled a stav služby."; }
    }

    private async Task RefreshListAsync()
    {
        try { _entriesGrid.ItemsSource = await _maintenance.ListAsync(_closing.Token); }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch { _status.Text = "Seznam karantény není dostupný nebo jeho záznamy vyžadují kontrolu."; }
    }

    private async Task RestoreAsync()
    {
        if (_entriesGrid.SelectedItem is not QuarantineItem item) { _status.Text = "Vyberte položku z karantény."; return; }
        try
        {
            var result = await _maintenance.RestoreAsync(item.Id, _restoreConfirmation.Text, _closing.Token);
            _status.Text = $"Výsledek obnovení: {result.Code}. Obnoveno {result.Moved}, velikost {result.Bytes} bajtů. Existující soubor se nepřepisuje.";
            await RefreshListAsync();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch { _status.Text = "Obnovení se nepodařilo. Znovu načtěte seznam a ověřte cílovou cestu."; }
    }

    private static void AddTextColumn(DataGrid grid, string title, string property, double width)
        => grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = new DataGridLength(width) });

    private void RegisterName(string name, FrameworkElement element) => RegisterName(name, (object)element);
}
