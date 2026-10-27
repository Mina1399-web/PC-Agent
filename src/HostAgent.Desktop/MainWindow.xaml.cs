using System;
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

namespace HostAgent.Desktop;

public partial class MainWindow:Window
{
    private readonly DashboardController _controller;
    private readonly CancellationTokenSource _closing=new();
    private readonly SemaphoreSlim _reading=new(1,1);
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromSeconds(15)};
    private readonly System.Windows.Forms.NotifyIcon _tray=new(){Text="Host Agent",Icon=System.Drawing.SystemIcons.Information};
    private ViewMetric[] _cpuHistory=[],_memoryHistory=[];
    private ViewIncidentDetail? _selectedDetail;
    private int _detailRequestVersion;
    private readonly OperationObservation _observation=new();
    public DesktopLocalization Localization { get; }
    private bool _hasRefreshed, _refreshFailed, _detailLoading;
    private Func<string>? _comparisonText;
    private DashboardSnapshot? _renderedView;
    private string _renderedState="disconnected";
    private string T(string source, params object?[] args) => Localization.Text(source, args);
    private LocalizedMessages Messages => new(Localization);

    public MainWindow(DashboardController controller) : this(controller, DesktopLocalization.ForCurrentUser()) { }
    public MainWindow(DashboardController controller, DesktopLocalization localization)
    {
        Localization=localization;
        InitializeComponent();_controller=controller;
        Localization.Apply(this);
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
        _tray.ContextMenuStrip!.Items[0].Text=T("OpenDashboard");
        _tray.ContextMenuStrip.Items[1].Text=T("ExitDashboard");
        if(_hasRefreshed)RenderSnapshot(_renderedView,false);
        if(_comparisonText is not null)ComparisonText.Text=_comparisonText();
        RenderDetail();
        foreach(var grid in new[]{DisksGrid,ProcessGrid,HealthGrid,ServicesGrid,IncidentsGrid})grid.Items.Refresh();
    }
    private async Task Refresh()
    {
        if(!await _reading.WaitAsync(0))return;
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
        finally {_reading.Release();}
    }
    private void RenderSnapshot(DashboardSnapshot? view,bool replaceRows)
    {
            if(view is null)
            {
                StatusText.Text=_renderedState=="stale"?T("ServiceDataIsOutOfDateTheCurrentStatusIsUnavailableTryRefresh"):T("TheLocalServiceIsUnavailableMakeSureAVersionOfHostAgentThatSupportsT");
                StatusBanner.Background=(Brush)FindResource("ErrorSurfaceBrush");
                StatusText.Foreground=(Brush)FindResource("ErrorBrush");
                CpuText.Text=MemoryText.Text=UptimeText.Text=IncidentCountText.Text="—";NetworkText.Text=T("NetworkDataUnavailable");
                DisksGrid.ItemsSource=ProcessGrid.ItemsSource=HealthGrid.ItemsSource=IncidentsGrid.ItemsSource=ServicesGrid.ItemsSource=null;
                SetEmptyStates(null);
                _cpuHistory=[];_memoryHistory=[];DrawChart();RecommendationsPanel.Children.Clear();OperationButton.IsEnabled=false;
                RecommendationsPanel.Children.Add(Notice(T("RecommendationsAreUnavailableWithoutCurrentMeasurementsCheckTheLocal")));
                OperationHintText.Text=T("ActionsAreBlockedTheCurrentServiceStatusIsUnavailable");
                _selectedDetail=null;DetailText.Text=T("DetailsAreUnavailableAPreviousStatusIsNotCurrentEvidence");return;
            }
            StatusText.Text=T("KillSwitch",(view.Degraded?T("PartialTelemetryLimitations"):T("MonitoringIsRunning")),TechnicalText.Format(view.Mode,Localization.Culture),(view.ReadOnly?T("ReadOnly"):T("ActionsRequireApproval")),(view.KillSwitch?T("On"):T("Off")),view.GeneratedAtUtc.ToLocalTime());
            StatusBanner.Background=(Brush)FindResource(view.Degraded?"WarningSurfaceBrush":"GreenSurfaceBrush");
            StatusText.Foreground=(Brush)FindResource(view.Degraded?"WarningBrush":"GreenBrush");
            CpuText.Text=Percent(view.Cpu);MemoryText.Text=Percent(view.Memory);
            UptimeText.Text=view.Uptime is {} up?up.Value>=86400?T("UptimeDays",(int)(Math.Max(0,up.Value)/86400),(int)(Math.Max(0,up.Value)/3600)%24,(int)(Math.Max(0,up.Value)/60)%60):T("UptimeHours",(int)(Math.Max(0,up.Value)/3600),(int)(Math.Max(0,up.Value)/60)%60):"—";
            IncidentCountText.Text=view.Incidents.Count(x=>x.State!="Resolved").ToString(Localization.Culture);
            NetworkText.Text=view.ConnectedAdapters is {} count?T("NetworkActiveAdaptersThisDoesNotVerifyInternetAccess",count):T("NetworkDataUnavailable");
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
            }
            SetEmptyStates(view);
            OperationButton.IsEnabled=view.Mode=="Confirmation"&&!view.ReadOnly&&!view.KillSwitch&&!_observation.Pending;
            OperationHintText.Text=_observation.Pending?T("WaitingForTheApprovalWindowToFinish"):view.KillSwitch?T("ActionsAreBlockedByTheEmergencySwitchKillSwitch"):view.Mode=="Shadow"?T("ShadowModeViewAndInvestigateTheStatusActionsAreBlocked"):view.ReadOnly?T("TheServiceIsInReadOnlyModeActionsAreBlocked"):T("ToPrepareAnActionSelectAnIncidentAndEnterARegisteredTargetExecutionR");
            RenderRecommendations(view);
            DrawChart();
            if(_refreshFailed){StatusText.Text=T("CouldNotRefreshTheDataAnotherAttemptWillRunAutomatically");OperationButton.IsEnabled=false;OperationHintText.Text=T("ActionsAreBlockedUntilTheStatusCanBeRefreshed");}
    }
    private void MaintenanceClick(object sender,RoutedEventArgs e)
    {
        var window=new MaintenanceWindow(_controller,null,Localization){Owner=this};
        window.ShowDialog();
    }
    private string Percent(ViewMetric? metric)=>metric is {} point?point.Value.ToString("0.#",Localization.Culture)+" %":"—";
    private void SetEmptyStates(DashboardSnapshot? view)
    {
        DisksEmptyText.Visibility=view?.Disks.Count>0?Visibility.Collapsed:Visibility.Visible;
        ProcessesEmptyText.Visibility=view?.Processes.Count>0?Visibility.Collapsed:Visibility.Visible;
        HealthEmptyText.Visibility=view?.Health.Count>0?Visibility.Collapsed:Visibility.Visible;
        ServicesEmptyText.Visibility=view?.Services.Count>0?Visibility.Collapsed:Visibility.Visible;
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
        if(cards.Count==0)RecommendationsPanel.Children.Add(Notice(T("TheCurrentEvidenceDoesNotCallForANewRecommendationMissingMeasurement")));
        foreach(var card in cards)
        {
            var textCard=RecommendationText.Localize(card,view,Localization);
            var panel=new StackPanel();
            panel.Children.Add(new TextBlock{Text=textCard.Title,FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
            foreach(var text in new[]{T("Evidence")+textCard.Evidence,T("Benefit")+textCard.Benefit,T("Impact")+textCard.Impact,T("Recovery")+textCard.Rollback})
                panel.Children.Add(new TextBlock{Text=text,Style=(Style)FindResource("Caption"),Margin=new Thickness(0,0,0,8)});
            var button=new Button{Content=card.Action==GuidedAction.Investigate?T("ReviewSavedEvidence"):T("OpenRelevantSettings"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,10,0,0)};
            button.Click+=async(_,_)=>{if(card.Action==GuidedAction.Investigate)await Investigate();else OpenSettings(card.Action);};
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
            OperationIncident.Text="";
            DetailText.Text=T("SelectAnIncidentToViewTheAvailableCodesTimesAndNumericEvidence");
            return;
        }
        OperationIncident.Text=incident.Id;_detailLoading=true;RenderDetail();
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
            if(_hasRefreshed&&_renderedView is null){DetailText.Text=T("DetailsAreUnavailableAPreviousStatusIsNotCurrentEvidence");return;}
            if(_detailLoading){DetailText.Text=T("LoadingLimitedEvidence");return;}
            if(IncidentsGrid.SelectedItem is not ViewIncident){DetailText.Text=T("SelectAnIncidentToViewTheAvailableCodesTimesAndNumericEvidence");return;}
            var detail=_selectedDetail;
            if(detail is null){DetailText.Text=T("DetailsAreUnavailable");return;}
            var scope=detail.ConfidenceScope=="stop_code_classification_only"?T("ConfidenceRefersToTheStopCodeClassificationItDoesNotIdentifyAFaultyD"):T("ConfidenceIsAnEstimateOfTheLikelyCauseNotAGuaranteeOfTheCorrectRepai");
            DetailText.Text=T("IncidentCodeStatusConfidence",detail.Incident.Id,detail.Incident.Code,TechnicalText.Format(detail.Incident.State,Localization.Culture),detail.Incident.Confidence)+
                (detail.Incident.AffectedService is{} service?T("AffectedService",service):string.Empty)+
                T("LimitedEvidenceSourceValueUTC",scope)+
                string.Join("\n",detail.Evidence.Select(x=>T("EvidenceValues",x.Source,x.Value?.ToString(Localization.Culture)??"—",x.AtUtc)))+
                (detail.Truncated?T("AdditionalEvidenceWasOmittedToLimitTheScope"):"")+T("ViewingThisInformationDoesNotApproveAnAutomaticRepair");
    }
    private async Task Investigate()
    {
        try
        {
            var result=await _controller.ReadAsync<ViewInvestigation>("investigate",new{},_closing.Token);
            if(result is null){Messages.Show(this,T("EvidenceIsUnavailable"),T("InvestigateProblem"));return;}
            var text=T("ReadingSavedMetadataFromTheLast24HoursUpToThe256LatestSamplesEventLo")+
                string.Join("\n",result.Events.Select(x=>T("EventRecordIDStop0x",x.AtUtc,x.EventId,x.RecordId,x.BugCheckCode,x.DumpFileName??T("NoDumpReference"))));
            if(result.Events.Count==0)text+=T("NoMatchingSavedEventsWereFoundThisDoesNotProveThereAreNoneInEventLog");
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
        if(_controller.Snapshot is not {} view)return;
        try
        {
            var op=Enum.Parse<HelperOperation>((string)((ComboBoxItem)OperationChoice.SelectedItem).Tag);
            var process=op==HelperOperation.TerminateProcess;
            int? pid=process&&int.TryParse(PidInput.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var p)?p:null;
            long? ticks=process&&long.TryParse(TicksInput.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var t)?t:null;
            _=DashboardCommands.ProposalArguments(view,OperationIncident.Text,op,AliasInput.Text,pid,ticks);
            if(Messages.Show(this,T("OpenAdministratorVerificationForThisExactActionEndingAProcessOrStopp"),T("ManualAction"),MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
            _observation.Begin(view);_comparisonText=null;ComparisonText.Text="";OperationButton.IsEnabled=false;
            using var child=DashboardCommands.Propose(view,OperationIncident.Text,op,AliasInput.Text,pid,ticks);
            if(child is null)throw new InvalidOperationException();
            await child.WaitForExitAsync(_closing.Token);
            _observation.Complete(DateTimeOffset.UtcNow,child.ExitCode==0);
            var succeeded=child.ExitCode==0;
            _comparisonText=()=>succeeded?T("CLIFinishedWaitForANewMeasurementAndUseTheComparisonImprovementHasNo"):T("TheCLIDidNotConfirmTheActionOrRejectedItASuccessfulRepairHasNotBeenE");
            ComparisonText.Text=_comparisonText();
            await Refresh();
        }
        catch(OperationCanceledException) when(_closing.IsCancellationRequested){_observation.Complete(DateTimeOffset.UtcNow,false);}
        catch {_observation.Complete(DateTimeOffset.UtcNow,false);Messages.Show(this,T("CannotPrepareTheActionCheckTheModeInstalledCLIVersionRegisteredAlias"));}
    }
    private async void KillSwitchClick(object sender,RoutedEventArgs e)
    {
        if(Messages.Show(this,T("BlockRepairsInBothTheMonitorAndHelperTheCLIWillRequestUACAndAPPLYCon"),T("EmergencySwitch"),MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
        try
        {
            using var child=DashboardCommands.BlockRepairs();if(child is null)return;
            await child.WaitForExitAsync(_closing.Token);await Refresh();
            if(child.ExitCode!=0)Messages.Show(this,T("TheCLIDidNotConfirmTheChangeTheKillSwitchHasNotBeenVerifiedAsOn"));
        }
        catch(OperationCanceledException) when(_closing.IsCancellationRequested){}
        catch {Messages.Show(this,T("CouldNotVerifyTheKillSwitchDoNotPerformAnyFurtherActions"));}
    }
    private async void CompareClick(object sender,RoutedEventArgs e)
    {
        await Refresh();var after=_controller.Snapshot;
        var state=_observation.Compare(after);
        var beforeCpu=_observation.Before?.Cpu;var beforeMemory=_observation.Before?.Memory;
        var afterCpu=_observation.PostOperationMetric(after?.Cpu);var afterMemory=_observation.PostOperationMetric(after?.Memory);
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
        var count=0;
        var bucketTicks=TimeSpan.FromMinutes(15).Ticks;
        foreach(var (points,color) in new[]{(_cpuHistory,(Brush)FindResource("AccentBrush")),(_memoryHistory,(Brush)FindResource("RamBrush"))})
        {
            Polyline? line=null;DateTimeOffset? previous=null;
            foreach(var point in points.OrderBy(x=>x.AtUtc))
            {
                if(!double.IsFinite(point.Value)||point.AtUtc>now||now-point.AtUtc>TimeSpan.FromHours(24))continue;
                if(line is null || previous is {} before && point.AtUtc.UtcTicks/bucketTicks-before.UtcTicks/bucketTicks>1)
                {line=new Polyline{Stroke=color,StrokeThickness=2};Chart.Children.Add(line);}
                var pointPosition=new Point(width*(1-(now-point.AtUtc).TotalHours/24),1+height*(1-Math.Clamp(point.Value,0,100)/100));
                line.Points.Add(pointPosition);previous=point.AtUtc;count++;
                var dot=new Ellipse{Width=4,Height=4,Fill=color};Canvas.SetLeft(dot,pointPosition.X-2);Canvas.SetTop(dot,pointPosition.Y-2);Chart.Children.Add(dot);
            }
        }
        ChartEmptyText.Visibility=count==0?Visibility.Visible:Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(Chart,count==0?T("CPUAndRAMHistoryIsUnavailable"):T("CPUAndRAMHistoryOver24HoursSavedPointsCPUInBlueRAMInPurple",count));
    }
    private void ChartSizeChanged(object sender,SizeChangedEventArgs e)=>DrawChart();
    private void ShowDashboard(){Show();WindowState=WindowState.Normal;Activate();_tray.Visible=false;}
    private void TrayClick(object sender,RoutedEventArgs e){_tray.Visible=true;Hide();}
    private async void RefreshClick(object sender,RoutedEventArgs e)=>await Refresh();
    private async void InvestigateClick(object sender,RoutedEventArgs e)=>await Investigate();
    private void OpenSettings(GuidedAction action)
    {try {DashboardCommands.OpenSettings(action);}catch {Messages.Show(this,T("CouldNotOpenWindowsSettings"));}}
    private void StorageClick(object sender,RoutedEventArgs e)=>OpenSettings(GuidedAction.StorageSettings);
    private void AppsClick(object sender,RoutedEventArgs e)=>OpenSettings(GuidedAction.AppsSettings);
    private void StartupClick(object sender,RoutedEventArgs e)=>OpenSettings(GuidedAction.StartupSettings);
}
