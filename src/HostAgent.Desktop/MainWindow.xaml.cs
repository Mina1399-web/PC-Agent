using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using HostAgent.Core.Dashboard;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Collectors;

namespace HostAgent.Desktop;

public partial class MainWindow:Window
{
    private readonly DashboardController _controller;
    private readonly IDiskHealthReader _diskHealthReader;
    private readonly Func<WindowsArea, WindowsInventory> _readWindowsInventory;
    private readonly SemaphoreSlim _windowsInventoryGate=new(1,1);
    private int _windowsInventoryRequest;
    private WindowsInventory? _windowsInventory;
    private string? _cleanupDrive;
    private ViewIncident? _recoveryIncident;
    private bool _guidedActionRunning;
    private Func<string>? _windowsActionText;
    private Func<string>? _smartText;
    private readonly CancellationTokenSource _closing=new();
    private readonly SemaphoreSlim _reading=new(1,1);
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromSeconds(15)};
    private readonly System.Windows.Forms.NotifyIcon _tray=new(){Text="Host Agent",Icon=System.Drawing.SystemIcons.Information};
    private readonly Func<bool>? _confirmDeveloperMode;
    private ViewMetric[] _cpuHistory=[],_memoryHistory=[];
    private ViewIncidentDetail? _selectedDetail;
    private string? _selectedOperationIncidentId;
    private int _detailRequestVersion;
    private readonly OperationObservation _observation=new();
    public DesktopLocalization Localization { get; }
    private bool _hasRefreshed, _refreshFailed, _detailLoading;
    private DesktopMode _desktopMode=DesktopMode.Standard;
    private bool _modePreferenceUnavailable, _childWorkflowPending, _maintenanceWindowOpen;
    private DesktopRoute _lastStandardTab = DesktopRoute.Situation;
    private Func<string>? _comparisonText;
    private DashboardSnapshot? _renderedView;
    private string _renderedState="disconnected";
    private string T(string source, params object?[] args) => Localization.Text(source, args);
    private LocalizedMessages Messages => new(Localization);

    public MainWindow(DashboardController controller) : this(controller, DesktopLocalization.ForCurrentUser()) { }
    public MainWindow(DashboardController controller, DesktopLocalization localization) : this(controller, localization, null) { }
    public MainWindow(DashboardController controller, DesktopLocalization localization, IDiskHealthReader? diskHealthReader)
        : this(controller, localization, diskHealthReader, WindowsAdministration.Read) { }
    internal MainWindow(DashboardController controller, DesktopLocalization localization, IDiskHealthReader? diskHealthReader,
        Func<WindowsArea, WindowsInventory> readWindowsInventory, Func<bool>? confirmDeveloperMode=null)
    {
        Localization=localization;
        _confirmDeveloperMode=confirmDeveloperMode;
        try{_desktopMode=Localization.ReadDesktopMode();}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {_desktopMode=DesktopMode.Standard;_modePreferenceUnavailable=true;}
        _diskHealthReader = diskHealthReader ?? new WindowsDiskHealthReader();
        _readWindowsInventory=readWindowsInventory;
        InitializeComponent();_controller=controller;
        Localization.Apply(this);
        InitializeDesign();
        ApplyDesktopMode();
        OperationChoice.SelectionChanged += OperationChoiceChanged;
        AliasInput.TextChanged += OperationFieldsChanged;
        PidInput.TextChanged += OperationFieldsChanged;
        TicksInput.TextChanged += OperationFieldsChanged;
        WindowsInventoryGrid.SelectionChanged += WindowsInventorySelectionChanged;
        LanguageChoice.ItemsSource=DesktopLocalization.Languages;
        LanguageChoice.SelectedValue=Localization.LanguageCode;
        LanguageChoice.SelectionChanged+=LanguageSelected;
        SizeChanged+=(_,_)=>MetricsGrid.Columns=ActualWidth<1050?2:4;
        var menu=new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(T("OpenDashboard"),null,(_,_)=>Dispatcher.Invoke(ShowDashboard));
        menu.Items.Add(T("ExitDashboard"),null,(_,_)=>Dispatcher.Invoke(Close));
        _tray.ContextMenuStrip=menu;
        _tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(ShowDashboard);
        _timer.Tick+=async(_,_)=>await Refresh();
        Loaded+=async(_,_)=>{_timer.Start();await Refresh();};
        Localization.Changed+=LanguageChanged;
        Closed+=(_,_)=>{Localization.Changed-=LanguageChanged;_timer.Stop();_closing.Cancel();_tray.Dispose();};
        ProcessGrid.SelectionChanged+=(_,_)=>
        {
            var process=ProcessGrid.SelectedItem as ViewProcess;
            PidInput.Text=process?.Pid.ToString(CultureInfo.InvariantCulture)??"";
            TicksInput.Text=process?.StartTimeUtcTicks?.ToString(CultureInfo.InvariantCulture)??"";
        };
    }
    private void LanguageSelected(object sender, SelectionChangedEventArgs e)
    {
        if(LanguageChoice.SelectedValue is not string code)return;
        try {Localization.SelectLanguage(code);}
        catch
        {
            LanguageChoice.SelectedValue=Localization.LanguageCode;
            Messages.Show(this,T("CouldNotSaveTheLanguageThePreviousSelectionRemainsInEffect"));
        }
    }
    private void LanguageChanged(object? sender, EventArgs e)
    {
        Localization.Apply(this);
        UpdateDesktopModeControls();
        UpdateThemeLabel();
        LanguageChoice.SelectedValue=Localization.LanguageCode;
        _tray.ContextMenuStrip!.Items[0].Text=T("OpenDashboard");
        _tray.ContextMenuStrip.Items[1].Text=T("ExitDashboard");
        if(_hasRefreshed)RenderSnapshot(_renderedView,false);
        else SubsystemView.Render(null, Localization, DateTimeOffset.UtcNow);
        if(_comparisonText is not null)ComparisonText.Text=_comparisonText();
        if(_smartText is not null)SmartResultText.Text=_smartText();
        RenderWindowsInventory();
        if(_windowsActionText is not null)WindowsActionStatus.Text=_windowsActionText();
        RenderDetail();
        RenderSelectedIncidentSummary();
        foreach(var grid in new[]{DisksGrid,ProcessGrid,HealthGrid,ServicesGrid,IncidentsGrid})grid.Items.Refresh();
    }

    internal DesktopMode CurrentDesktopMode=>_desktopMode;
    internal bool DeveloperFeaturesEnabled=>DesktopModePolicy.CanOpenDeveloperSurface(_desktopMode);

    private void ApplyDesktopMode()
    {
        var developer=DeveloperFeaturesEnabled;
        if(DashboardTabs.SelectedItem is TabItem active &&
            !DesktopModePolicy.CanNavigateTo(active.Tag as DesktopRoute?, _desktopMode))
            NavigateTo(_lastStandardTab,focus:false);
        foreach(var tab in DashboardTabs.Items.OfType<TabItem>())
        {
            var route=tab.Tag as DesktopRoute?;
            tab.IsEnabled=DesktopModePolicy.CanNavigateTo(route,_desktopMode);
            if(route is {} named && DesktopModePolicy.IsDeveloperTab(named))
                tab.ToolTip=tab.IsEnabled?null:T("DeveloperModeRequired");
        }
        if(developer)_lastStandardTab=DesktopRoute.Situation;
        UpdateDesktopModeControls();
        UpdateOperationReadiness();
    }

    private void UpdateDesktopModeControls()
    {
        var modeText=T(DeveloperFeaturesEnabled?"DesignModeDeveloperActive":"DesignModeStandardActive");
        DesktopModeButton.Content=modeText;
        System.Windows.Automation.AutomationProperties.SetName(DesktopModeButton,modeText);
        DesktopModeButton.ToolTip=_modePreferenceUnavailable?T("ModePreferenceSaveFailed"):
            DeveloperFeaturesEnabled?T("ReturnToStandardMode"):T("DeveloperModeConsent");
    }

    private void DesktopModeClick(object sender,RoutedEventArgs e)
        =>RequestDesktopMode(DeveloperFeaturesEnabled?DesktopMode.Standard:DesktopMode.Developer);

    internal bool RequestDesktopMode(DesktopMode mode)
    {
        if(mode==_desktopMode)return true;
        if(!DesktopModePolicy.CanChangeMode(_childWorkflowPending,_guidedActionRunning,_maintenanceWindowOpen))
        {Messages.Show(this,T("ModeChangeBlockedWhileActionRuns"));return false;}
        if(mode==DesktopMode.Developer)
        {
            var accepted=_confirmDeveloperMode?.Invoke()??Messages.Show(this,T("DeveloperModeConsent"),T("EnableDeveloperMode"),
                MessageBoxButton.OKCancel,MessageBoxImage.Information)==MessageBoxResult.OK;
            if(!accepted)return false;
        }
        try{Localization.SaveDesktopMode(mode);}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {Messages.Show(this,T("ModePreferenceSaveFailed"));return false;}
        _desktopMode=mode;_modePreferenceUnavailable=false;
        if(mode==DesktopMode.Standard)
        {
            WindowsDiagnosis.Text="";WindowsActionStatus.Text="";
            DashboardTabs.Focus();
        }
        ApplyDesktopMode();
        RenderTriage(_renderedView);
        RenderDetail();
        foreach(var grid in new[]{HealthGrid,ServicesGrid,IncidentsGrid})grid.Items.Refresh();
        return true;
    }

    private bool RequireDeveloperMode()
    {
        if(DeveloperFeaturesEnabled)return true;
        Messages.Show(this,T("DeveloperModeRequired"));
        return false;
    }
    private async Task Refresh()
    {
        if(!await _reading.WaitAsync(0))return;
        RefreshButton.IsEnabled=false;
        try
        {
            await _controller.RefreshAsync(_closing.Token);
            var view=_controller.Snapshot;
            _renderedView=view;_renderedState=_controller.State;
            _hasRefreshed=true;_refreshFailed=false;
            RenderSnapshot(view,true);
            if(view is not null)
            {
                _cpuHistory=await _controller.ReadAsync<ViewMetric[]>("history",new{metric="cpu"},_closing.Token)??[];
                _memoryHistory=await _controller.ReadAsync<ViewMetric[]>("history",new{metric="memory"},_closing.Token)??[];
                DrawChart();
            }
        }
        catch(OperationCanceledException) when(_closing.IsCancellationRequested){}
        catch {_hasRefreshed=true;_refreshFailed=true;_renderedView=null;_renderedState=_controller.State;RenderSnapshot(null,false);}
        finally {RefreshButton.IsEnabled=true;_reading.Release();}
    }
    private void RenderSnapshot(DashboardSnapshot? view,bool replaceRows)
    {
            var now=DateTimeOffset.UtcNow;
            if(view is not null && !ViewFreshness.IsFresh(view.GeneratedAtUtc,now))
            {
                view=null;
                _renderedState="stale";
            }
            SetDataTooltips(view,now);
            RenderDesignMetrics(view);
            RenderWindowsDiagnosis(view);
            HealthSummaryText.Text=HealthSummary.Describe(view,Localization.Culture);
            if(view is null)
            {
                StatusText.Text=_renderedState=="stale"?T("ServiceDataIsOutOfDateTheCurrentStatusIsUnavailableTryRefresh"):T("TheLocalServiceIsUnavailableMakeSureAVersionOfHostAgentThatSupportsT");
                StatusBanner.Background=(Brush)FindResource("ErrorSurfaceBrush");
                StatusText.Foreground=(Brush)FindResource("ErrorBrush");
                CpuText.Text=MemoryText.Text=UptimeText.Text=IncidentCountText.Text="—";NetworkText.Text=T("NetworkDataUnavailable");
                SystemCpuText.Text=SystemMemoryText.Text=SystemUptimeText.Text=SystemIncidentCountText.Text="—";
                SystemEvidenceMetaText.Text=T("DesignEvidenceSourceUnavailable");
                DisksGrid.ItemsSource=ProcessGrid.ItemsSource=HealthGrid.ItemsSource=IncidentsGrid.ItemsSource=ServicesGrid.ItemsSource=null;
                SetEmptyStates(null);
                _cpuHistory=[];_memoryHistory=[];DrawChart();RecommendationsPanel.Children.Clear();OperationButton.IsEnabled=false;
                RecommendationsPanel.Children.Add(Notice(T("RecommendationsAreUnavailableWithoutCurrentMeasurementsCheckTheLocal")));
                OperationHintText.Text=T("ActionsAreBlockedTheCurrentServiceStatusIsUnavailable");
                _selectedDetail=null;DetailText.Text=T("DetailsAreUnavailableAPreviousStatusIsNotCurrentEvidence");return;
            }
            var limited=view.Degraded || (view.DataState is {} data &&
                new[]{data.Cpu,data.Memory,data.Uptime,data.Disks,data.Processes,data.Services,data.Network}
                    .Any(state=>!state.HasCurrentData(now)||state.Status=="partial"));
            if(view.DataState is null && (CurrentDisplayMetric(view.Cpu,null,now) is null
                || CurrentDisplayMetric(view.Memory,null,now,allowLegacyZero:false) is null))limited=true;
            StatusText.Text=T("KillSwitch",(limited?T("PartialTelemetryLimitations"):T("MonitoringIsRunning")),TechnicalText.Format(view.Mode,Localization.Culture),(view.ReadOnly?T("ReadOnly"):T("ActionsRequireApproval")),(view.KillSwitch?T("On"):T("Off")),view.GeneratedAtUtc.ToLocalTime());
            StatusBanner.Background=(Brush)FindResource(limited?"WarningSurfaceBrush":"GreenSurfaceBrush");
            StatusText.Foreground=(Brush)FindResource(limited?"WarningBrush":"GreenBrush");
            CpuText.Text=Percent(CurrentDisplayMetric(view.Cpu,view.DataState?.Cpu,now));
            MemoryText.Text=Percent(CurrentDisplayMetric(view.Memory,view.DataState?.Memory,now,allowLegacyZero:false));
            UptimeText.Text=(view.DataState?.Uptime.HasCurrentData(now)??true) && view.Uptime is {} up && ViewFreshness.IsFresh(up.AtUtc,now) && up.Value is >=0 and <= int.MaxValue*86400d?up.Value>=86400?T("UptimeDays",(int)(Math.Max(0,up.Value)/86400),(int)(Math.Max(0,up.Value)/3600)%24,(int)(Math.Max(0,up.Value)/60)%60):T("UptimeHours",(int)(Math.Max(0,up.Value)/3600),(int)(Math.Max(0,up.Value)/60)%60):"—";
            IncidentCountText.Text=view.Incidents.Count(x=>x.State!="Resolved").ToString(Localization.Culture);
            SystemCpuText.Text=CpuText.Text;SystemMemoryText.Text=MemoryText.Text;SystemUptimeText.Text=UptimeText.Text;
            SystemIncidentCountText.Text=IncidentCountText.Text;
            SystemEvidenceMetaText.Text=T("DesignEvidenceSourceAndTime",view.GeneratedAtUtc.ToLocalTime());
            NetworkText.Text=view.ConnectedAdapters is >=0 and {} count && (view.DataState?.Network.HasCurrentData(now)??true)
                ?T("NetworkActiveAdaptersThisDoesNotVerifyInternetAccess",count):T("NetworkDataUnavailable");
            if(view.DataState?.Network is {Status:not "available"} networkState)
                NetworkText.Text+="\n"+DescribeData(networkState,now);
            if(replaceRows)
            {
            var selectedProcess=ProcessGrid.SelectedItem as ViewProcess;
            DisksGrid.ItemsSource=view.Disks;ProcessGrid.ItemsSource=view.Processes;HealthGrid.ItemsSource=view.Health;ServicesGrid.ItemsSource=view.Services;
            ProcessGrid.SelectedItem=selectedProcess?.StartTimeUtcTicks is >0
                ?view.Processes.FirstOrDefault(x=>x.Pid==selectedProcess.Pid&&x.StartTimeUtcTicks==selectedProcess.StartTimeUtcTicks)
                :null;
            SetEmptyStates(view);
            var selected=(IncidentsGrid.SelectedItem as ViewIncident)?.Id;
            IncidentsGrid.ItemsSource=view.Incidents;
            if(selected is not null)IncidentsGrid.SelectedItem=view.Incidents.FirstOrDefault(x=>x.Id==selected);
            RenderSelectedIncidentSummary();
            }
            SetEmptyStates(view);
            UpdateOperationReadiness();
            RenderRecommendations(view);
            DrawChart();
            if(_refreshFailed){StatusText.Text=T("CouldNotRefreshTheDataAnotherAttemptWillRunAutomatically");UpdateOperationReadiness();}
    }
    private async void SmartClick(object sender, RoutedEventArgs e)
    {
        if (!SmartReadButton.IsEnabled) return;
        SmartReadButton.IsEnabled = false;
        try
        {
            var result = await _diskHealthReader.ReadAsync(_closing.Token);
            _smartText = () => result.Count == 0 ? T("SmartPredictionUnavailable") : string.Join(Environment.NewLine,
                result.Select(item => T(item.Status == "FailurePredicted" ? "SmartFailurePredicted" :
                    item.Status == "NoFailurePredicted" ? "SmartNoFailurePredicted" : "SmartDeviceUnavailable", item.DeviceId)));
            SmartResultText.Text = _smartText();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch { _smartText = () => T("SmartPredictionUnavailable"); SmartResultText.Text = _smartText(); }
        finally { SmartReadButton.IsEnabled = true; }
    }

    private void MaintenanceClick(object sender,RoutedEventArgs e)
    {
        _maintenanceWindowOpen=true;
        try
        {
            var window=new MaintenanceWindow(_controller,null,Localization,developerMode:()=>DeveloperFeaturesEnabled){Owner=this};
            OrbitTheme.ApplyToDialog(window, this);
            window.ShowDialog();
        }
        finally{_maintenanceWindowOpen=false;}
    }
    private static ViewMetric? CurrentDisplayMetric(ViewMetric? metric,ViewDataState? state,DateTimeOffset now,bool allowLegacyZero=true)
        => metric is not null && ViewFreshness.IsFresh(metric.AtUtc,now) && metric.Value is >=0 and <=100
            && (state?.HasCurrentData(now) ?? (allowLegacyZero || metric.Value!=0)) ? metric : null;

    private string Percent(ViewMetric? metric)=>metric is {} point && ViewFreshness.IsFresh(point.AtUtc,DateTimeOffset.UtcNow)
        && point.Value is >=0 and <=100 ? point.Value.ToString("0.#",Localization.Culture)+" %":"—";

    private void SetDataTooltips(DashboardSnapshot? view,DateTimeOffset now)
    {
        var data=view?.DataState;
        ViewDataState? Legacy(ViewMetric? point,string source)=>point is null?null:new(source,point.AtUtc,"partial","legacy_sample");
        CpuText.ToolTip=SystemCpuText.ToolTip=DescribeData(data?.Cpu??Legacy(view?.Cpu,"GetSystemTimes"),now);
        MemoryText.ToolTip=SystemMemoryText.ToolTip=DescribeData(data?.Memory??Legacy(view?.Memory,"GlobalMemoryStatusEx"),now);
        UptimeText.ToolTip=SystemUptimeText.ToolTip=DescribeData(data?.Uptime??Legacy(view?.Uptime,"system"),now);
        DisksGrid.ToolTip=DescribeData(data?.Disks,now);
        ProcessGrid.ToolTip=DescribeData(data?.Processes,now);
        ServicesGrid.ToolTip=DescribeData(data?.Services,now);
        NetworkText.ToolTip=DescribeData(data?.Network,now);
    }

    private string DescribeData(ViewDataState? state,DateTimeOffset now)
    {
        if (state?.AtUtc is { } at && !ViewFreshness.IsFresh(at, now))
            state = state with { Status = "stale", Reason = "sample_expired" };
        return DataTrustDisplay.Describe(Localization, state);
    }

    private void SetListState(TextBlock label,int count,ViewDataState? state)
    {
        var currentEmpty=count==0 && state is {Status:"available"} && state.HasCurrentData(DateTimeOffset.UtcNow);
        label.Text=(currentEmpty?T("DataTrustEmpty")+"\n":"")+DescribeData(state,DateTimeOffset.UtcNow);
        label.Visibility=count==0||state is {Status:not "available"}?Visibility.Visible:Visibility.Collapsed;
    }
    private void SetEmptyStates(DashboardSnapshot? view)
    {
        SetListState(DisksEmptyText,view?.Disks.Count??0,view?.DataState?.Disks);
        SetListState(ProcessesEmptyText,view?.Processes.Count??0,view?.DataState?.Processes);
        HealthEmptyText.Visibility=view?.Health.Count>0?Visibility.Collapsed:Visibility.Visible;
        SetListState(ServicesEmptyText,view?.Services.Count??0,view?.DataState?.Services);
        IncidentsEmptyText.Text=view is null?T("IncidentsAreUnavailableWithoutAConnectionToTheService"):view.Incidents.Count==0?T("ThereAreNoIncidentsInTheLatestSavedRecords"):T("ShowingTheLatestRecordsDiagnosticConfidenceDoesNotGuaranteeASuccessf",view.Incidents.Count);
    }
    private Border Notice(string text)=>new()
    {
        Style=(Style)FindResource("Card"),
        Child=new TextBlock{Text=text,Style=(Style)FindResource("Caption")}
    };
    private void RenderRecommendations(DashboardSnapshot view)
    {
        RecommendationsPanel.Children.Clear();
        var cards=new RecommendationEngine().Evaluate(view,DateTimeOffset.UtcNow);
        if(cards.Count==0)
        {
            var content=new StackPanel();
            content.Children.Add(new TextBlock{Text=T("TheCurrentEvidenceDoesNotCallForANewRecommendationMissingMeasurement"),Style=(Style)FindResource("Caption"),TextWrapping=TextWrapping.Wrap});
            var review=new Button{Content=T("ReviewSavedEvidence"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,0)};
            review.Click+=async(_,_)=>await Investigate();
            content.Children.Add(review);
            RecommendationsPanel.Children.Add(new Border{Child=content,Style=(Style)FindResource("Card"),Margin=new Thickness(0,0,0,16)});
        }
        foreach(var card in cards)
        {
            var textCard=RecommendationText.Localize(card,view,Localization);
            var panel=new StackPanel();
            panel.Children.Add(new TextBlock{Text=textCard.Title,FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
            foreach(var text in new[]{T("Evidence")+textCard.Evidence,T("Benefit")+textCard.Benefit,T("Impact")+textCard.Impact,T("Recovery")+textCard.Rollback})
                panel.Children.Add(new TextBlock{Text=text,Style=(Style)FindResource("Caption"),Margin=new Thickness(0,0,0,8)});
            var disk=card.Code.StartsWith("disk_space:",StringComparison.Ordinal)?card.Code["disk_space:".Length..]:null;
            var button=new Button{Content=disk is not null?T("DiskMaintenancePreview"):card.Action==GuidedAction.Investigate?T("ReviewSavedEvidence"):T("OpenWindowsManagement"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,10,0,0)};
            if(disk is not null)button.IsEnabled=MaintenanceWindow.MatchesDefaultVolume(disk);
            button.Click+=async(_,_)=>{if(disk is not null)await OpenDiskCleanup(disk);else if(card.Action==GuidedAction.Investigate)await Investigate();else OpenManagementArea(card.Action);};
            panel.Children.Add(button);RecommendationsPanel.Children.Add(new Border{Child=panel,Style=(Style)FindResource("Card"),Margin=new Thickness(0,0,0,16)});
        }
    }
    private async void IncidentSelected(object sender,SelectionChangedEventArgs e)
    {
        var requestVersion=++_detailRequestVersion;
        _selectedDetail=null;
        _detailLoading=false;
        if(IncidentsGrid.SelectedItem is not ViewIncident incident)
        {
            RenderSelectedIncidentSummary();
            DetailText.Text=T("SelectAnIncidentToViewTheAvailableCodesTimesAndNumericEvidence");
            TechnicalDetailsExpander.Visibility=Visibility.Collapsed;
            return;
        }
        RenderSelectedIncidentSummary();
        _detailLoading=true;RenderDetail();
        try
        {
            var detail=await _controller.ReadAsync<ViewIncidentDetail>("incident",new{incidentId=incident.Id},_closing.Token);
            if(_closing.IsCancellationRequested||requestVersion!=_detailRequestVersion
                ||(IncidentsGrid.SelectedItem as ViewIncident)?.Id!=incident.Id)return;
            _selectedDetail=detail;
            _detailLoading=false;RenderDetail();
        }
        catch(OperationCanceledException) when(_closing.IsCancellationRequested){}
    }
    private void RenderDetail()
    {
            if(_hasRefreshed&&_renderedView is null){TechnicalDetailsExpander.Visibility=Visibility.Collapsed;DetailText.Text=T("DetailsAreUnavailableAPreviousStatusIsNotCurrentEvidence");return;}
            if(_detailLoading){TechnicalDetailsExpander.Visibility=Visibility.Collapsed;DetailText.Text=T("LoadingLimitedEvidence");return;}
            if(IncidentsGrid.SelectedItem is not ViewIncident){TechnicalDetailsExpander.Visibility=Visibility.Collapsed;DetailText.Text=T("SelectAnIncidentToViewTheAvailableCodesTimesAndNumericEvidence");return;}
            var detail=_selectedDetail;
            if(detail is null){TechnicalDetailsExpander.Visibility=Visibility.Collapsed;DetailText.Text=T("DetailsAreUnavailable");return;}
            var scope=detail.ConfidenceScope=="stop_code_classification_only"?T("ConfidenceRefersToTheStopCodeClassificationItDoesNotIdentifyAFaultyD"):T("ConfidenceIsAnEstimateOfTheLikelyCauseNotAGuaranteeOfTheCorrectRepai");
            DetailText.Text=T("IncidentCodeStatusConfidence",T("IncidentSummary",TechnicalText.Format(detail.Incident.Code,Localization.Culture),detail.Incident.AtUtc.ToLocalTime().ToString("g",Localization.Culture)),TechnicalText.Format(detail.Incident.Code,Localization.Culture),TechnicalText.Format(detail.Incident.State,Localization.Culture),detail.Incident.Confidence)+
                (detail.Incident.AffectedService is{} service?T("AffectedService",service):string.Empty)+
                T("LimitedEvidenceSourceValueUTC",scope)+
                string.Join("\n",detail.Evidence.Select(x=>T("EvidenceValues",x.Source,x.Value?.ToString(Localization.Culture)??"—",x.AtUtc)))+
                (detail.Truncated?T("AdditionalEvidenceWasOmittedToLimitTheScope"):"")+T("ViewingThisInformationDoesNotApproveAnAutomaticRepair");
            TechnicalDetailsExpander.Visibility=DeveloperFeaturesEnabled?Visibility.Visible:Visibility.Collapsed;
            TechnicalIncidentCodeText.Text=T("TechnicalIncidentIdentifiers",detail.Incident.Id,detail.Incident.Code);
    }

    private void RenderSelectedIncidentSummary()
    {
        if(IncidentsGrid.SelectedItem is not ViewIncident incident)
        {
            _selectedOperationIncidentId=null;
            OperationIncidentSummary.Text=T("NoIncidentSelected");
        }
        else
        {
            _selectedOperationIncidentId=incident.Id;
            OperationIncidentSummary.Text=T("IncidentSummary",TechnicalText.Format(incident.Code,Localization.Culture),
                incident.AtUtc.ToLocalTime().ToString("g",Localization.Culture));
        }
        UpdateOperationReadiness();
    }
    private async Task Investigate()
    {
        try
        {
            var result=await _controller.ReadAsync<ViewInvestigation>("investigate",new{},_closing.Token);
            if(result is null){Messages.Show(this,T("EvidenceIsUnavailable"),T("InvestigateProblem"));return;}
            var text=T("ReadingSavedMetadataFromTheLast24HoursUpToThe256LatestSamplesEventLo")+
                string.Join("\n",result.Events.Select(x=>T("EventRecordIDStop0x",x.AtUtc,x.EventId,x.RecordId,x.BugCheckCode,x.DumpFileName??T("NoDumpReference"))));
            text+="\n"+T("DataTrustEventCoverage",DescribeData(result.EventData,DateTimeOffset.UtcNow));
            if(result.Events.Count==0 && result.EventData is {Status:"available"})
                text+=T("NoMatchingSavedEventsWereFoundThisDoesNotProveThereAreNoneInEventLog");
            text+=T("TheseMetadataCannotIdentifyASpecificFaultyDriverDumpsAreNotSentAnywh");
            Messages.Show(this,text,T("InvestigateProblemReadOnly"));
        }
        catch(OperationCanceledException) when(_closing.IsCancellationRequested){}
    }
    private void ExportClick(object sender,RoutedEventArgs e)
    {
        if(_selectedDetail is not {} detail){Messages.Show(this,T("FirstSelectAnIncidentWithAvailableDetails"));return;}
        var path=LocalizedMessages.ChooseReportPath(this,Localization,"host-agent-incident-"+detail.Incident.Id+".json");
        if(path is null)return;
        try {File.WriteAllText(path,DashboardController.Export(detail));}
        catch {Messages.Show(this,T("CouldNotSaveTheReport"));}
    }
    private async void OperationClick(object sender,RoutedEventArgs e)
    {
        if(!RequireDeveloperMode())return;
        if(_controller.Snapshot is not {} view)return;
        var readiness=OperationReadiness(view);
        if(readiness is not null){OperationHintText.Text=T(readiness);return;}
        if(OperationChoice.SelectedItem is not ComboBoxItem selectedAction || selectedAction.Tag is not string actionTag ||
            !Enum.TryParse<HelperOperation>(actionTag,out var op))
        {
            OperationHintText.Text=T("SelectActionAfterReviewingEvidence");
            return;
        }
        try
        {
            var process=op==HelperOperation.TerminateProcess;
            int? pid=process&&int.TryParse(PidInput.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var p)?p:null;
            long? ticks=process&&long.TryParse(TicksInput.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var t)?t:null;
            _=DashboardCommands.ProposalArguments(view,_selectedOperationIncidentId!,op,AliasInput.Text,pid,ticks);
            if(Messages.Show(this,T("OpenAdministratorVerificationForThisExactActionEndingAProcessOrStopp"),T("ManualAction"),MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
            if(!RequireDeveloperMode())return;
            _observation.Begin(view);_comparisonText=null;ComparisonText.Text="";OperationButton.IsEnabled=false;
            _childWorkflowPending=true;
            try
            {
                using var child=DashboardCommands.Propose(view,_selectedOperationIncidentId!,op,AliasInput.Text,pid,ticks);
                if(child is null)throw new InvalidOperationException();
                await child.WaitForExitAsync(_closing.Token);
                _observation.Complete(DateTimeOffset.UtcNow,child.ExitCode==0);
                var succeeded=child.ExitCode==0;
                _comparisonText=()=>succeeded?T("CLIFinishedWaitForANewMeasurementAndUseTheComparisonImprovementHasNo"):T("TheCLIDidNotConfirmTheActionOrRejectedItASuccessfulRepairHasNotBeenE");
                ComparisonText.Text=_comparisonText();
                await Refresh();
            }
            finally{_childWorkflowPending=false;}
        }
        catch(OperationCanceledException) when(_closing.IsCancellationRequested){_observation.Complete(DateTimeOffset.UtcNow,false);}
        catch {_observation.Complete(DateTimeOffset.UtcNow,false);Messages.Show(this,T("CannotPrepareTheActionCheckTheModeInstalledCLIVersionRegisteredAlias"));}
    }
    private string? OperationReadiness(DashboardSnapshot? view)
    {
        if(_observation.Pending||_childWorkflowPending)return "WaitingForTheApprovalWindowToFinish";
        if(_refreshFailed)return "ActionsAreBlockedUntilTheStatusCanBeRefreshed";
        if(view is null||!ViewFreshness.IsFresh(view.GeneratedAtUtc,DateTimeOffset.UtcNow))return "ActionsRequireFreshStatus";
        if(view.KillSwitch)return "ActionsAreBlockedByTheEmergencySwitchKillSwitch";
        if(view.Mode=="Shadow")return "ShadowModeViewAndInvestigateTheStatusActionsAreBlocked";
        if(view.ReadOnly)return "TheServiceIsInReadOnlyModeActionsAreBlocked";
        if(view.Mode!="Confirmation")return "ActionsAreNotAvailableYetWaitingForTheCurrentServiceMode";
        if(_selectedOperationIncidentId is not { } incidentId||!Guid.TryParseExact(incidentId,"N",out var incident)||incident==Guid.Empty)
            return "SelectIncidentBeforeAction";
        if(OperationChoice.SelectedItem is not ComboBoxItem {Tag:string actionTag}||!Enum.TryParse<HelperOperation>(actionTag,out var operation))
            return "SelectActionAfterReviewingEvidence";
        var alias=AliasInput.Text;
        if(alias.Length is <1 or >64||!alias.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_' or '.'))
            return "EnterRegisteredTargetAlias";
        if(operation==HelperOperation.TerminateProcess&&
            (!int.TryParse(PidInput.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var pid)||pid<=0||
             !long.TryParse(TicksInput.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var ticks)||ticks<=0||ticks>DateTimeOffset.MaxValue.UtcTicks))
            return "SelectProcessBeforeAction";
        try{_ = DashboardCommands.ProposalArguments(view,incidentId,operation,alias,
            operation==HelperOperation.TerminateProcess?int.Parse(PidInput.Text,CultureInfo.InvariantCulture):null,
            operation==HelperOperation.TerminateProcess?long.Parse(TicksInput.Text,CultureInfo.InvariantCulture):null);}
        catch{return "ActionTargetNotRegisteredOrRequestInvalid";}
        return null;
    }

    private bool CanOfferOperation(DashboardSnapshot? view)=>OperationReadiness(view) is null;
    private void UpdateOperationReadiness()
    {
        var key=OperationReadiness(_renderedView);
        OperationButton.IsEnabled=key is null;
        OperationHintText.Text=T(key??"ActionReadyRequiresSeparateApproval");
    }
    private void OperationFieldsChanged(object sender,TextChangedEventArgs e)=>UpdateOperationReadiness();
    private void OperationChoiceChanged(object sender,SelectionChangedEventArgs e)
    {UpdateProcessFields();UpdateOperationReadiness();}
    private async void KillSwitchClick(object sender,RoutedEventArgs e)
    {
        if(!RequireDeveloperMode())return;
        if(_childWorkflowPending)return;
        if(Messages.Show(this,T("BlockRepairsInBothTheMonitorAndHelperTheCLIWillRequestUACAndAPPLYCon"),T("EmergencySwitch"),MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
        try
        {
            _childWorkflowPending=true;
            using var child=DashboardCommands.BlockRepairs();if(child is null)return;
            await child.WaitForExitAsync(_closing.Token);await Refresh();
            if(child.ExitCode!=0)Messages.Show(this,T("TheCLIDidNotConfirmTheChangeTheKillSwitchHasNotBeenVerifiedAsOn"));
        }
        catch(OperationCanceledException) when(_closing.IsCancellationRequested){}
        catch {Messages.Show(this,T("CouldNotVerifyTheKillSwitchDoNotPerformAnyFurtherActions"));}
        finally{_childWorkflowPending=false;}
    }
    private async void CompareClick(object sender,RoutedEventArgs e)
    {
        if(!RequireDeveloperMode())return;
        await Refresh();var after=_controller.Snapshot;
        var state=_observation.Compare(after);
        var now=DateTimeOffset.UtcNow;
        var before=_observation.Before;
        var beforeCpu=CurrentDisplayMetric(before?.Cpu,before?.DataState?.Cpu,now);
        var beforeMemory=CurrentDisplayMetric(before?.Memory,before?.DataState?.Memory,now,allowLegacyZero:false);
        var afterCpu=_observation.PostOperationMetric(CurrentDisplayMetric(after?.Cpu,after?.DataState?.Cpu,now));
        var afterMemory=_observation.PostOperationMetric(CurrentDisplayMetric(after?.Memory,after?.DataState?.Memory,now,allowLegacyZero:false));
        _comparisonText=()=>state switch
        {
            ObservationState.Pending=>T("TheApprovalWindowIsStillOpenCompletionOfTheActionIsUnconfirmed"),
            ObservationState.Unconfirmed=>T("TheCLIDidNotConfirmSuccessLowerUtilizationAloneDoesNotProveThatARepa"),
            ObservationState.Observed=>T("MeasurementsAfterCLICompletionCPURAMTheSymbolMeansThereIsNoNewSample",Percent(beforeCpu),Percent(afterCpu),Percent(beforeMemory),Percent(afterMemory)),
            ObservationState.AwaitingMeasurement=>T("ThereIsNoMeasurementNewerThanCLICompletionYetTheResultOfTheActionIsU"),
            _=>T("TheStatusBeforeOrAfterTheActionIsMissing")
        };
        ComparisonText.Text=_comparisonText();
    }
    private void DrawChart()
    {
        Chart.Children.Clear();var width=Math.Max(1,Chart.ActualWidth);var height=Math.Max(1,Chart.ActualHeight-2);var now=DateTimeOffset.UtcNow;
        foreach(var fraction in new[]{0d,0.5,1})
            Chart.Children.Add(new Line{X1=0,X2=width,Y1=1+height*fraction,Y2=1+height*fraction,Stroke=(Brush)FindResource("LineBrush"),StrokeThickness=1,StrokeDashArray=new DoubleCollection{4,4}});
        var seriesDescriptions=new List<string>();
        var bucketTicks=TimeSpan.FromMinutes(15).Ticks;
        foreach(var (points,legend,label) in new[]{(_cpuHistory,CpuHistoryLegend,CpuHistoryLabel),(_memoryHistory,RamHistoryLegend,RamHistoryLabel)})
        {
            var color=legend.Foreground;
            var count=0;
            Polyline? line=null;DateTimeOffset? previous=null;
            foreach(var point in points.OrderBy(x=>x.AtUtc))
            {
                if(point.Value is not (>=0 and <=100)||point.AtUtc>now||now-point.AtUtc>TimeSpan.FromHours(24))continue;
                if(line is null || previous is {} before && point.AtUtc.UtcTicks/bucketTicks-before.UtcTicks/bucketTicks>1)
                {line=new Polyline{Stroke=color,StrokeThickness=2};System.Windows.Automation.AutomationProperties.SetName(line,label.Text);Chart.Children.Add(line);}
                var pointPosition=new Point(width*(1-(now-point.AtUtc).TotalHours/24),1+height*(1-point.Value/100));
                line.Points.Add(pointPosition);previous=point.AtUtc;count++;
                var dot=new Ellipse{Width=4,Height=4,Fill=color};Canvas.SetLeft(dot,pointPosition.X-2);Canvas.SetTop(dot,pointPosition.Y-2);Chart.Children.Add(dot);
            }
            if(count>0)seriesDescriptions.Add(T("HistorySeriesSavedPoints",label.Text,count));
        }
        ChartEmptyText.Visibility=seriesDescriptions.Count==0?Visibility.Visible:Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(ChartSummary,seriesDescriptions.Count==0
            ?T("CPUAndRAMHistoryIsUnavailable")
            :T("CPUAndRAMHistoryOver24Hours")+" "+string.Join(" ",seriesDescriptions));
    }
    private void ChartSizeChanged(object sender,SizeChangedEventArgs e)=>DrawChart();
    private void ShowDashboard(){Show();WindowState=WindowState.Normal;Activate();_tray.Visible=false;}
    private void TrayClick(object sender,RoutedEventArgs e){_tray.Visible=true;Hide();}
    private async void RefreshClick(object sender,RoutedEventArgs e)=>await Refresh();
    private async void InvestigateClick(object sender,RoutedEventArgs e)=>await Investigate();
    private void OpenManagementArea(GuidedAction action)
    {
        var area=action switch
        {
            GuidedAction.Storage=>WindowsArea.Disks,
            GuidedAction.Apps=>WindowsArea.Applications,
            GuidedAction.Startup=>WindowsArea.Startup,
            _=>(WindowsArea?)null
        };
        if(area is {} value)OpenManagementArea(value);
    }

    private void OpenManagementArea(WindowsArea area)
    {
        NavigateTo(DesktopRoute.WindowsTools,focus:false);
        WindowsInventoryArea.SelectedItem=WindowsInventoryArea.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item=>string.Equals(item.Tag as string,area.ToString(),StringComparison.Ordinal));
        WindowsAdministrationTab.Focus();
    }

    private void StorageClick(object sender,RoutedEventArgs e)=>OpenManagementArea(WindowsArea.Disks);
    private void AppsClick(object sender,RoutedEventArgs e)=>OpenManagementArea(WindowsArea.Applications);
    private void StartupClick(object sender,RoutedEventArgs e)=>OpenManagementArea(WindowsArea.Startup);

    private void WindowsAdministrationClick(object sender,RoutedEventArgs e)
    {
        NavigateTo(DesktopRoute.WindowsTools);
    }

    private async void WindowsDashboardTabChanged(object sender,SelectionChangedEventArgs e)
    {
        if(ReferenceEquals(e.Source,DashboardTabs))
        {
            if(DashboardTabs.SelectedItem is not TabItem selectedTab)return;
            var selected=selectedTab.Tag as DesktopRoute?;
            if(!DesktopModePolicy.CanNavigateTo(selected,_desktopMode))
            {NavigateTo(_lastStandardTab,focus:false);return;}
            if(selected is {} named && !DesktopModePolicy.IsDeveloperTab(named))_lastStandardTab=named;
        }
        if(ReferenceEquals(e.Source,DashboardTabs)&&WindowsAdministrationTab?.IsSelected==true)
            await ReadWindowsInventory();
    }

    private async void WindowsAreaChanged(object sender,SelectionChangedEventArgs e)
    {
        if(WindowsAdministrationTab?.IsSelected==true)await ReadWindowsInventory();
    }

    private async void WindowsInventoryRefresh(object sender,RoutedEventArgs e)=>await ReadWindowsInventory();

    private async Task ReadWindowsInventory()
    {
        if(WindowsInventoryArea.SelectedItem is not ComboBoxItem {Tag:string name} ||
            !Enum.TryParse<WindowsArea>(name,out var area))return;
        var request=++_windowsInventoryRequest;
        _windowsInventory=null;
        WindowsToolStatus.ToolTip=null;
        WindowsInventoryGrid.ItemsSource=null;
        SetInventoryStatus("WinInventoryLoading");
        try
        {
            await _windowsInventoryGate.WaitAsync(_closing.Token);
            try
            {
                if(request!=_windowsInventoryRequest||!WindowsAdministrationTab.IsSelected)return;
                var result=await Task.Run(()=>_readWindowsInventory(area),_closing.Token);
                if(request!=_windowsInventoryRequest||_closing.IsCancellationRequested||!WindowsAdministrationTab.IsSelected)return;
                _windowsInventory=result with { AtUtc=DateTimeOffset.UtcNow };
                RenderWindowsInventory();
            }
            finally{_windowsInventoryGate.Release();}
        }
        catch(OperationCanceledException) when(_closing.IsCancellationRequested){}
        catch(Exception error) when(error is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException
            or System.Diagnostics.Eventing.Reader.EventLogException)
        {
            if(request==_windowsInventoryRequest&&!_closing.IsCancellationRequested)
            {
                SetInventoryStatus("WinInventoryUnavailable");
                var reason=error is UnauthorizedAccessException or Win32Exception {NativeErrorCode:5}
                    ? "access_denied" : "collector_unavailable";
                WindowsToolStatus.ToolTip=DescribeData(new("Windows",DateTimeOffset.UtcNow,"unavailable",reason),DateTimeOffset.UtcNow);
            }
        }
    }

    private void WindowsInventorySearchChanged(object sender,TextChangedEventArgs e)=>RenderWindowsInventory();

    private void RenderWindowsInventory()
    {
        if(_windowsInventory is not {} inventory)return;
        var isProcesses=WindowsInventoryArea.SelectedItem is ComboBoxItem {Tag:"Processes"};
        WindowsInventoryGrid.Columns[1].Header=T(isProcesses?"ProcessPID":"WinInventoryDetail");
        WindowsInventoryGrid.Columns[3].Header=T(isProcesses?"Memory":"WinInventoryValue");
        var search=WindowsInventorySearch.Text.Trim();
        var rows=inventory.Rows.Select(row=>row with
        {
            State=row.StateKey is {} key?T(key):TechnicalText.Format(row.State,Localization.Culture)
        }).Where(row=>search.Length==0||new[]{row.Name,row.Detail,row.State,row.Value}.Any(
            field=>field.Contains(search,StringComparison.CurrentCultureIgnoreCase))).ToArray();
        WindowsInventoryGrid.ItemsSource=rows;
        var now=DateTimeOffset.UtcNow;
        var dataState=new ViewDataState("Windows",inventory.AtUtc,inventory.Partial?"partial":"available",
            inventory.Partial?"incomplete_sample":null);
        if(!dataState.HasCurrentData(now))
        {
            WindowsInventoryGrid.ItemsSource=null;
            SetInventoryStatus("DataTrustStale");
        }
        else SetInventoryStatus(inventory.Partial?"WinInventoryPartial":rows.Length==0?"WinInventoryEmpty":"WinInventoryLoaded");
        WindowsToolStatus.ToolTip=DescribeData(dataState,now);
        WindowsAreaCapabilityText.Text=T(WindowsInventoryArea.SelectedItem is ComboBoxItem {Tag:"Applications"}?"WinInventoryApplicationsReadOnly":
            WindowsInventoryArea.SelectedItem is ComboBoxItem {Tag:"Startup"}?"WinInventoryStartupReadOnly":
            WindowsInventoryArea.SelectedItem is ComboBoxItem {Tag:"Services"}?"WinInventoryServicesCapability":
            WindowsInventoryArea.SelectedItem is ComboBoxItem {Tag:"Processes"}?"WinInventoryProcessesCapability":
            WindowsInventoryArea.SelectedItem is ComboBoxItem {Tag:"Disks"}?"WinInventoryDisksCapability":
            WindowsInventoryArea.SelectedItem is ComboBoxItem {Tag:"Network"}?"WinInventoryNetworkCapability":"WinInventoryEventsCapability");
        UpdateWindowsInventoryActionButtons();
    }

    private void UpdateWindowsInventoryActionButtons()
    {
        var process = WindowsInventoryArea.SelectedItem is ComboBoxItem {Tag:"Processes"};
        var service = WindowsInventoryArea.SelectedItem is ComboBoxItem {Tag:"Services"};
        PrepareProcessActionButton.Visibility=process&&WindowsInventoryGrid.SelectedItem is WindowsInventoryRow {ProcessId:not null,ProcessStartTimeUtcTicks:not null}
            ?Visibility.Visible:Visibility.Collapsed;
        PrepareServiceActionButton.Visibility=service&&WindowsInventoryGrid.SelectedItem is WindowsInventoryRow
            ?Visibility.Visible:Visibility.Collapsed;
        ManageSelectedDiskButton.Visibility=WindowsInventoryArea.SelectedItem is ComboBoxItem {Tag:"Disks"}
            &&WindowsInventoryGrid.SelectedItem is WindowsInventoryRow {Name:var drive}
            &&MaintenanceWindow.MatchesDefaultVolume(drive.TrimEnd('\\'))?Visibility.Visible:Visibility.Collapsed;
    }

    private void SetInventoryStatus(string key)=>WindowsToolStatus.SetResourceReference(TextBlock.TextProperty,"Loc."+key);

    private void WindowsInventorySelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        UpdateWindowsInventoryActionButtons();
    }

    private void PrepareProcessActionClick(object sender,RoutedEventArgs e)
    {
        if(WindowsInventoryGrid.SelectedItem is not WindowsInventoryRow {ProcessId:{} pid,ProcessStartTimeUtcTicks:{} ticks})return;
        if(!RequireDeveloperMode())return;
        NavigateTo(DesktopRoute.Maintenance,focus:false);
        PidInput.Text=pid.ToString(CultureInfo.InvariantCulture);
        TicksInput.Text=ticks.ToString(CultureInfo.InvariantCulture);
        OperationChoice.SelectedIndex=3;
        AliasInput.Focus();
        UpdateOperationReadiness();
    }

    private void PrepareServiceActionClick(object sender,RoutedEventArgs e)
    {
        if(WindowsInventoryArea.SelectedItem is not ComboBoxItem {Tag:"Services"}
            ||WindowsInventoryGrid.SelectedItem is not WindowsInventoryRow service)return;
        if(!RequireDeveloperMode())return;
        NavigateTo(DesktopRoute.Maintenance,focus:false);
        OperationChoice.SelectedIndex=-1;
        PidInput.Clear();TicksInput.Clear();
        AliasInput.Text=service.Name;
        AliasInput.Focus();
        UpdateOperationReadiness();
    }

    private async void ManageSelectedDiskClick(object sender,RoutedEventArgs e)
    {
        if(!RequireDeveloperMode())return;
        if(WindowsInventoryGrid.SelectedItem is not WindowsInventoryRow {Name:var drive})return;
        await OpenDiskCleanup(drive.TrimEnd('\\'));
    }

    private void RenderWindowsDiagnosis(DashboardSnapshot? view)
    {
        var now=DateTimeOffset.UtcNow;
        var recommendations=view is null?Array.Empty<Recommendation>():new RecommendationEngine().Evaluate(view,now);
        _cleanupDrive=view?.Disks.FirstOrDefault(disk=>MaintenanceWindow.MatchesDefaultVolume(disk.Drive)
            &&recommendations.Any(card=>card.Code=="disk_space:"+disk.Drive))?.Drive;
        _recoveryIncident=view is null?null:view.Incidents.FirstOrDefault(incident=>DashboardCommands.CanReviewServiceRecovery(view,incident,now));
        DiskCleanupAction.Visibility=_cleanupDrive is null?Visibility.Collapsed:Visibility.Visible;
        ServiceRecoveryAction.Visibility=_recoveryIncident is null?Visibility.Collapsed:Visibility.Visible;
        if(_recoveryIncident is not null)ServiceRecoveryAction.Content=T("ServiceRecoveryReview",_recoveryIncident.AffectedService);
        NetworkProbesAction.Visibility=view?.NetworkChecks is null?Visibility.Collapsed:Visibility.Visible;
        NetworkProbesAction.Content=T(view?.NetworkChecks?.Enabled==true?"NetworkDisableChecks":"NetworkEnableChecks");
        ServiceRecoveryAction.IsEnabled=NetworkProbesAction.IsEnabled=!_guidedActionRunning;
        if(view is null)
        {
            WindowsDiagnosis.Text=T("WinDiagnosisUnavailable");
            return;
        }
        var findings=recommendations.Select(card=>
        {
            var localized=RecommendationText.Localize(card,view,Localization);
            return $"{localized.Title}\n{localized.Evidence}\n{localized.Benefit}";
        }).ToList();
        var service=_recoveryIncident??view.Incidents.FirstOrDefault(i=>i.Code is ("service_crash" or "service_stopped")
            &&i.State is ("Detected" or "Active" or "Acknowledged")&&ViewFreshness.IsFresh(i.AtUtc,now));
        if(service is not null) findings.Add(T(service.Code=="service_crash"?"ServiceCrashFinding":"ServiceStopFinding",service.AffectedService??"—"));
        if(view.NetworkChecks is {} checks && ViewFreshness.IsFresh(checks.AtUtc,now))
            findings.Add(DescribeNetwork(checks,view.DataState?.Network.Status is "available" or null?view.ConnectedAdapters:null));
        WindowsDiagnosis.Text=findings.Count==0?T("WinDiagnosisNone"):string.Join("\n\n",findings);
    }

    private string DescribeNetwork(ViewNetworkChecks checks,int? adapters)
    {
        if(!checks.Enabled)return T("NetworkChecksDisabled");
        string Status(string kind)=>checks.Probes.FirstOrDefault(p=>p.Kind==kind)?.Status??"Unavailable";
        string Display(string status)=>T(status switch
        {
            "Succeeded"=>"NetworkProbePassed",
            "Failed"=>"NetworkProbeFailed",
            "TimedOut"=>"NetworkProbeTimeout",
            _=>"NetworkProbeMissing"
        });
        static bool Failed(string status)=>status is "Failed" or "TimedOut";
        var gateway=Status("Gateway");
        var external=Status("InternetIp");
        var dns=Status("DnsResolution");
        var finding=adapters==0?"NetworkFindingAdapter":Failed(gateway)?"NetworkFindingGateway":
            gateway=="Succeeded"&&external=="Succeeded"&&Failed(dns)?"NetworkFindingDns":
            gateway=="Succeeded"&&Failed(external)?"NetworkFindingExternal":
            gateway=="Succeeded"&&external=="Succeeded"&&dns=="Succeeded"?"NetworkFindingHealthy":"NetworkFindingUnknown";
        return T("NetworkProbeSummary",Display(gateway),Display(external),Display(dns))+"\n"+T(finding);
    }

    private async void DiskCleanupClick(object sender,RoutedEventArgs e)
    {
        if(!RequireDeveloperMode())return;
        if(_cleanupDrive is {} drive)await OpenDiskCleanup(drive);
    }

    private async Task OpenDiskCleanup(string drive)
    {
        if(!RequireDeveloperMode())return;
        if(!MaintenanceWindow.MatchesDefaultVolume(drive) || _controller.Snapshot is not {} view ||
            !new RecommendationEngine().Evaluate(view,DateTimeOffset.UtcNow).Any(card=>card.Code=="disk_space:"+drive))return;
        try
        {
            _maintenanceWindowOpen=true;
            var window=new MaintenanceWindow(_controller,null,Localization,drive,()=>DeveloperFeaturesEnabled){Owner=this};
            OrbitTheme.ApplyToDialog(window, this);
            window.ShowDialog();
            _maintenanceWindowOpen=false;
            await Refresh();
            if(WindowsAdministrationTab.IsSelected&&DeveloperFeaturesEnabled)await ReadWindowsInventory();
        }
        catch(UnauthorizedAccessException){SetWindowsActionStatus("SelectedFolderUnavailable");}
        finally{_maintenanceWindowOpen=false;}
    }

    private async void ServiceRecoveryClick(object sender,RoutedEventArgs e)
    {
        if(!RequireDeveloperMode())return;
        if(_guidedActionRunning||_controller.Snapshot is not {} view||_recoveryIncident is not {} incident)return;
        await RunGuidedAction(async ()=>
        {
            using var child=DashboardCommands.ReviewServiceRecovery(view,incident)??throw new InvalidOperationException();
            await child.WaitForExitAsync(_closing.Token);
            if(child.ExitCode!=0){SetWindowsActionStatus("ServiceRecoveryUnverified");return;}
            using var service=new System.ServiceProcess.ServiceController(incident.AffectedService!);
            for(var second=0;second<=10;second++)
            {
                service.Refresh();
                if(service.Status!=System.ServiceProcess.ServiceControllerStatus.Running)
                {
                    SetWindowsActionStatus("ServiceRecoveryUnverified");
                    return;
                }
                if(second<10)await Task.Delay(TimeSpan.FromSeconds(1),_closing.Token);
            }
            SetWindowsActionStatus("ServiceRecoveryRunning",incident.AffectedService);
        });
    }

    private async void NetworkProbesClick(object sender,RoutedEventArgs e)
    {
        if(!RequireDeveloperMode())return;
        if(_guidedActionRunning||_controller.Snapshot?.NetworkChecks is not {} checks||
            !ViewFreshness.IsFresh(checks.AtUtc,DateTimeOffset.UtcNow))return;
        var enable=!checks.Enabled;
        if(Messages.Show(this,T("NetworkChecksConfirm",T(enable?"NetworkEnableChecks":"NetworkDisableChecks")),
            T("WinGroupNetwork"),MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        await RunGuidedAction(async ()=>
        {
            using var child=DashboardCommands.ConfigureNetworkProbes(enable)??throw new InvalidOperationException();
            await child.WaitForExitAsync(_closing.Token);
            if(child.ExitCode!=0){SetWindowsActionStatus("NetworkSettingUnverified");return;}
            await Refresh();
            SetWindowsActionStatus(_controller.Snapshot?.NetworkChecks?.Enabled==enable
                ?"NetworkSettingVerified":"NetworkSettingUnverified");
        });
    }

    private async Task RunGuidedAction(Func<Task> action)
    {
        if(!RequireDeveloperMode())return;
        _guidedActionRunning=true;
        RenderWindowsDiagnosis(_renderedView);
        try{await action();}
        catch(OperationCanceledException) when(_closing.IsCancellationRequested){}
        catch(Exception error) when(error is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {SetWindowsActionStatus("WinActionUnverified");}
        finally{_guidedActionRunning=false;RenderWindowsDiagnosis(_renderedView);}
    }

    private void SetWindowsActionStatus(string key,params object?[] args)
    {
        _windowsActionText=()=>T(key,args);
        WindowsActionStatus.Text=_windowsActionText();
    }
}
