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

    public MainWindow(DashboardController controller)
    {
        InitializeComponent();_controller=controller;
        SizeChanged+=(_,_)=>MetricsGrid.Columns=ActualWidth<1050?2:4;
        var menu=new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Otevřít přehled",null,(_,_)=>Dispatcher.Invoke(ShowDashboard));
        menu.Items.Add("Ukončit přehled",null,(_,_)=>Dispatcher.Invoke(Close));
        _tray.ContextMenuStrip=menu;
        _tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(ShowDashboard);
        _timer.Tick+=async(_,_)=>await Refresh();
        Loaded+=async(_,_)=>{_timer.Start();await Refresh();};
        Closed+=(_,_)=>{_timer.Stop();_closing.Cancel();_tray.Dispose();};
        ProcessGrid.SelectionChanged+=(_,_)=>
        {
            var process=ProcessGrid.SelectedItem as ViewProcess;
            PidInput.Text=process?.Pid.ToString(CultureInfo.InvariantCulture)??"";
            TicksInput.Text=process?.StartTimeUtcTicks?.ToString(CultureInfo.InvariantCulture)??"";
        };
    }
    private async Task Refresh()
    {
        if(!await _reading.WaitAsync(0))return;
        try
        {
            await _controller.RefreshAsync(_closing.Token);
            var view=_controller.Snapshot;
            if(view is null)
            {
                StatusText.Text=_controller.State=="stale"?"Data služby jsou zastaralá. Čerstvý stav není dostupný. Zkuste Obnovit.":"Místní služba není dostupná. Ověřte, že běží verze Host Agent podporující přehled, a zkuste Obnovit.";
                StatusBanner.Background=(Brush)FindResource("ErrorSurfaceBrush");
                StatusText.Foreground=(Brush)FindResource("ErrorBrush");
                CpuText.Text=MemoryText.Text=UptimeText.Text=IncidentCountText.Text="—";NetworkText.Text="Síť: nedostupná data";
                DisksGrid.ItemsSource=ProcessGrid.ItemsSource=HealthGrid.ItemsSource=IncidentsGrid.ItemsSource=ServicesGrid.ItemsSource=null;
                SetEmptyStates(null);
                _cpuHistory=[];_memoryHistory=[];DrawChart();RecommendationsPanel.Children.Clear();OperationButton.IsEnabled=false;
                RecommendationsPanel.Children.Add(Notice("Doporučení nejsou dostupná bez aktuálních měření. Ověřte místní službu a obnovte data."));
                OperationHintText.Text="Zásahy jsou zablokované: aktuální stav služby není dostupný.";
                _selectedDetail=null;DetailText.Text="Detail není dostupný. Dřívější stav není aktuální důkaz.";return;
            }
            StatusText.Text=$"{(view.Degraded?"Částečná telemetrie / omezení":"Monitorování běží")}  •  {view.Mode}  •  {(view.ReadOnly?"Jen čtení":"Zásahy pouze se schválením")}  •  Kill switch: {(view.KillSwitch?"zapnutý":"vypnutý")}  •  {view.GeneratedAtUtc.ToLocalTime():HH:mm:ss}";
            StatusBanner.Background=(Brush)FindResource(view.Degraded?"WarningSurfaceBrush":"GreenSurfaceBrush");
            StatusText.Foreground=(Brush)FindResource(view.Degraded?"WarningBrush":"GreenBrush");
            CpuText.Text=Percent(view.Cpu);MemoryText.Text=Percent(view.Memory);
            UptimeText.Text=view.Uptime is {} up?TimeSpan.FromSeconds(Math.Max(0,up.Value)).ToString(up.Value>=86400?"d' d 'hh' h'":"h' h 'mm' m'",CultureInfo.InvariantCulture):"—";
            IncidentCountText.Text=view.Incidents.Count(x=>x.State!="Resolved").ToString(CultureInfo.CurrentCulture);
            NetworkText.Text=view.ConnectedAdapters is {} count?$"Síť: {count} aktivních adaptérů. Dostupnost internetu tím není ověřena.":"Síť: nedostupná data";
            var selectedProcess=ProcessGrid.SelectedItem as ViewProcess;
            DisksGrid.ItemsSource=view.Disks;ProcessGrid.ItemsSource=view.Processes;HealthGrid.ItemsSource=view.Health;ServicesGrid.ItemsSource=view.Services;
            ProcessGrid.SelectedItem=selectedProcess?.StartTimeUtcTicks is >0
                ?view.Processes.FirstOrDefault(x=>x.Pid==selectedProcess.Pid&&x.StartTimeUtcTicks==selectedProcess.StartTimeUtcTicks)
                :null;
            SetEmptyStates(view);
            var selected=(IncidentsGrid.SelectedItem as ViewIncident)?.Id;
            IncidentsGrid.ItemsSource=view.Incidents;
            if(selected is not null)IncidentsGrid.SelectedItem=view.Incidents.FirstOrDefault(x=>x.Id==selected);
            OperationButton.IsEnabled=view.Mode=="Confirmation"&&!view.ReadOnly&&!view.KillSwitch&&!_observation.Pending;
            OperationHintText.Text=_observation.Pending?"Čekám na dokončení schvalovacího okna.":view.KillSwitch?"Zásahy jsou zablokované nouzovým vypínačem (kill switch).":view.Mode=="Shadow"?"Režim Shadow: sledujete a prověřujete stav. Zásahy jsou zablokované.":view.ReadOnly?"Služba je v režimu jen pro čtení. Zásahy jsou zablokované.":"Zásah můžete připravit po výběru incidentu a zadání registrovaného cíle. Provedení vyžaduje samostatné schválení.";
            RenderRecommendations(view);
            _cpuHistory=await _controller.ReadAsync<ViewMetric[]>("history",new{metric="cpu"},_closing.Token)??[];
            _memoryHistory=await _controller.ReadAsync<ViewMetric[]>("history",new{metric="memory"},_closing.Token)??[];
            DrawChart();
        }
        catch(OperationCanceledException) when(_closing.IsCancellationRequested){}
        catch {StatusText.Text="Obnovení dat se nepodařilo. Další pokus proběhne automaticky.";OperationButton.IsEnabled=false;OperationHintText.Text="Zásahy jsou zablokované, dokud se nepodaří obnovit stav.";}
        finally {_reading.Release();}
    }
    private static string Percent(ViewMetric? metric)=>metric is {} point?point.Value.ToString("0.#",CultureInfo.CurrentCulture)+" %":"—";
    private void SetEmptyStates(DashboardSnapshot? view)
    {
        DisksEmptyText.Visibility=view?.Disks.Count>0?Visibility.Collapsed:Visibility.Visible;
        ProcessesEmptyText.Visibility=view?.Processes.Count>0?Visibility.Collapsed:Visibility.Visible;
        HealthEmptyText.Visibility=view?.Health.Count>0?Visibility.Collapsed:Visibility.Visible;
        ServicesEmptyText.Visibility=view?.Services.Count>0?Visibility.Collapsed:Visibility.Visible;
        IncidentsEmptyText.Text=view is null?"Incidenty nejsou dostupné bez spojení se službou.":view.Incidents.Count==0?"V posledních uložených záznamech nejsou incidenty.":$"Zobrazeno {view.Incidents.Count} posledních záznamů. Jistota diagnózy neznamená záruku úspěšné opravy.";
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
        if(cards.Count==0)RecommendationsPanel.Children.Add(Notice("Aktuální důkazy nevyžadují nové doporučení. Chybějící měření neznamená bezchybný stav."));
        foreach(var card in cards)
        {
            var panel=new StackPanel();
            panel.Children.Add(new TextBlock{Text=card.Title,FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
            foreach(var text in new[]{"Důkaz: "+card.Evidence,"Přínos: "+card.Benefit,"Dopad: "+card.Impact,"Návrat: "+card.Rollback})
                panel.Children.Add(new TextBlock{Text=text,Style=(Style)FindResource("Caption"),Margin=new Thickness(0,0,0,8)});
            var button=new Button{Content=card.Action==GuidedAction.Investigate?"Prověřit uložené důkazy":"Otevřít příslušné nastavení…",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,10,0,0)};
            button.Click+=async(_,_)=>{if(card.Action==GuidedAction.Investigate)await Investigate();else OpenSettings(card.Action);};
            panel.Children.Add(button);RecommendationsPanel.Children.Add(new Border{Child=panel,Style=(Style)FindResource("Card"),Margin=new Thickness(0,0,0,16)});
        }
    }
    private async void IncidentSelected(object sender,SelectionChangedEventArgs e)
    {
        var requestVersion=++_detailRequestVersion;
        _selectedDetail=null;
        if(IncidentsGrid.SelectedItem is not ViewIncident incident)
        {
            OperationIncident.Text="";
            DetailText.Text="Vyberte incident. Zobrazí se dostupné kódy, časy a číselné důkazy.";
            return;
        }
        OperationIncident.Text=incident.Id;DetailText.Text="Načítám omezené důkazy…";
        try
        {
            var detail=await _controller.ReadAsync<ViewIncidentDetail>("incident",new{incidentId=incident.Id},_closing.Token);
            if(_closing.IsCancellationRequested||requestVersion!=_detailRequestVersion
                ||(IncidentsGrid.SelectedItem as ViewIncident)?.Id!=incident.Id)return;
            _selectedDetail=detail;
            if(detail is null){DetailText.Text="Detail není dostupný.";return;}
            var scope=detail.ConfidenceScope=="stop_code_classification_only"?"Confidence určuje klasifikaci stop kódu. Neurčuje vadný ovladač.":"Confidence je odhad pravděpodobné příčiny, nikoli záruka správné opravy.";
            DetailText.Text=$"Incident: {detail.Incident.Id}\nKód: {detail.Incident.Code}\nStav: {detail.Incident.State}\nConfidence: {detail.Incident.Confidence} %\n{scope}\n\nOmezené důkazy (zdroj / hodnota / UTC):\n"+
                string.Join("\n",detail.Evidence.Select(x=>$"{x.Source} / {x.Value?.ToString(CultureInfo.InvariantCulture)??"—"} / {x.AtUtc:O}"))+
                (detail.Truncated?"\nDalší důkazy byly kvůli omezení rozsahu vynechány.":"")+"\n\nAutomatická oprava není tímto zobrazením schválena.";
        }
        catch(OperationCanceledException) when(_closing.IsCancellationRequested){}
    }
    private async Task Investigate()
    {
        try
        {
            var result=await _controller.ReadAsync<ViewInvestigation>("investigate",new{},_closing.Token);
            if(result is null){MessageBox.Show(this,"Důkazy nejsou dostupné.","Prověřit problém");return;}
            var text="Čtení uložených metadat za 24 hodin, nejvýše 256 posledních vzorků. Event Log ani checkpointy se nemění.\n\n"+
                string.Join("\n",result.Events.Select(x=>$"{x.AtUtc:O} | Event {x.EventId} | RecordID {x.RecordId} | Stop 0x{x.BugCheckCode:X} | {x.DumpFileName??"bez reference dumpu"}"));
            if(result.Events.Count==0)text+="Nejsou nalezeny odpovídající uložené události. To nedokazuje, že v Event Logu žádné nejsou.";
            text+="\n\nKonkrétní vadný ovladač nelze určit z těchto metadat. Dumpy se nikam neposílají.";
            MessageBox.Show(this,text,"Prověřit problém • jen čtení");
        }
        catch(OperationCanceledException) when(_closing.IsCancellationRequested){}
    }
    private void ExportClick(object sender,RoutedEventArgs e)
    {
        if(_selectedDetail is not {} detail){MessageBox.Show(this,"Nejdřív vyberte dostupný detail incidentu.");return;}
        var dialog=new Microsoft.Win32.SaveFileDialog{Filter="JSON report (*.json)|*.json",FileName="host-agent-incident-"+detail.Incident.Id+".json",OverwritePrompt=true};
        if(dialog.ShowDialog(this)!=true)return;
        try {File.WriteAllText(dialog.FileName,DashboardController.Export(detail));}
        catch {MessageBox.Show(this,"Report se nepodařilo uložit.");}
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
            if(MessageBox.Show(this,"Otevřít administrátorské ověření přesného zásahu?\nUkončení procesu nebo zastavení služby může přerušit práci. Uložte svá data. K provedení bude potřeba opsat GUID v CLI.","Ruční zásah",MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
            _observation.Begin(view);OperationButton.IsEnabled=false;
            using var child=DashboardCommands.Propose(view,OperationIncident.Text,op,AliasInput.Text,pid,ticks);
            if(child is null)throw new InvalidOperationException();
            await child.WaitForExitAsync(_closing.Token);
            _observation.Complete(DateTimeOffset.UtcNow,child.ExitCode==0);
            ComparisonText.Text=child.ExitCode==0?"CLI dokončeno. Počkejte na nové měření a použijte porovnání; zlepšení zatím není ověřeno.":"CLI zásah nepotvrdilo nebo jej odmítlo. Úspěšná oprava není doložena.";
            await Refresh();
        }
        catch(OperationCanceledException) when(_closing.IsCancellationRequested){_observation.Complete(DateTimeOffset.UtcNow,false);}
        catch {_observation.Complete(DateTimeOffset.UtcNow,false);MessageBox.Show(this,"Zásah nelze připravit. Ověřte režim, instalovanou verzi CLI, registrovaný alias, incident a přesnou identitu procesu.");}
    }
    private async void KillSwitchClick(object sender,RoutedEventArgs e)
    {
        if(MessageBox.Show(this,"Zablokovat opravy v monitoru i helperu?\nCLI vyžádá UAC a potvrzení APPLY. Restartují se pouze služby Host Agent. Oznámení se nemění.","Kill switch",MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
        try
        {
            using var child=DashboardCommands.BlockRepairs();if(child is null)return;
            await child.WaitForExitAsync(_closing.Token);await Refresh();
            if(child.ExitCode!=0)MessageBox.Show(this,"CLI změnu nepotvrdilo; kill switch není ověřen jako zapnutý.");
        }
        catch(OperationCanceledException) when(_closing.IsCancellationRequested){}
        catch {MessageBox.Show(this,"Kill switch se nepodařilo ověřit. Neprovádějte další zásahy.");}
    }
    private async void CompareClick(object sender,RoutedEventArgs e)
    {
        await Refresh();var after=_controller.Snapshot;
        var state=_observation.Compare(after);
        ComparisonText.Text=state switch
        {
            ObservationState.Pending=>"Schvalovací okno je stále otevřené. Dokončení zásahu není potvrzeno.",
            ObservationState.Unconfirmed=>"CLI nepotvrdilo úspěch. Nižší vytížení samo nedokazuje provedení opravy.",
            ObservationState.Observed=>$"Měření po dokončení CLI: CPU {Percent(_observation.Before!.Cpu)} → {Percent(_observation.PostOperationMetric(after!.Cpu))}; RAM {Percent(_observation.Before.Memory)} → {Percent(_observation.PostOperationMetric(after.Memory))}. Symbol — znamená, že dosud není nový vzorek dané metriky. Samotná změna nedokazuje opravu ani příčinnou souvislost.",
            ObservationState.AwaitingMeasurement=>"Zatím není měření novější než dokončení CLI. Výsledek zásahu není ověřen.",
            _=>"Chybí stav před nebo po zásahu."
        };
    }
    private void DrawChart()
    {
        Chart.Children.Clear();var width=Math.Max(1,Chart.ActualWidth);var height=Math.Max(1,Chart.ActualHeight-2);var now=DateTimeOffset.UtcNow;
        foreach(var fraction in new[]{0d,0.5,1})
            Chart.Children.Add(new Line{X1=0,X2=width,Y1=1+height*fraction,Y2=1+height*fraction,Stroke=(Brush)FindResource("LineBrush"),StrokeThickness=1,StrokeDashArray=new DoubleCollection{4,4}});
        var count=0;
        foreach(var (points,color) in new[]{(_cpuHistory,(Brush)FindResource("AccentBrush")),(_memoryHistory,(Brush)FindResource("RamBrush"))})
        {
            Polyline? line=null;DateTimeOffset? previous=null;
            foreach(var point in points.OrderBy(x=>x.AtUtc))
            {
                if(!double.IsFinite(point.Value)||point.AtUtc>now||now-point.AtUtc>TimeSpan.FromHours(24))continue;
                if(line is null || previous is {} before && point.AtUtc-before>TimeSpan.FromMinutes(30))
                {line=new Polyline{Stroke=color,StrokeThickness=2};Chart.Children.Add(line);}
                var pointPosition=new Point(width*(1-(now-point.AtUtc).TotalHours/24),1+height*(1-Math.Clamp(point.Value,0,100)/100));
                line.Points.Add(pointPosition);previous=point.AtUtc;count++;
                var dot=new Ellipse{Width=4,Height=4,Fill=color};Canvas.SetLeft(dot,pointPosition.X-2);Canvas.SetTop(dot,pointPosition.Y-2);Chart.Children.Add(dot);
            }
        }
        ChartEmptyText.Visibility=count==0?Visibility.Visible:Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(Chart,count==0?"Historie CPU a RAM není dostupná":$"Historie CPU a RAM za 24 hodin, {count} uložených bodů. CPU modře, RAM fialově.");
    }
    private void ChartSizeChanged(object sender,SizeChangedEventArgs e)=>DrawChart();
    private void ShowDashboard(){Show();WindowState=WindowState.Normal;Activate();_tray.Visible=false;}
    private void TrayClick(object sender,RoutedEventArgs e){_tray.Visible=true;Hide();}
    private async void RefreshClick(object sender,RoutedEventArgs e)=>await Refresh();
    private async void InvestigateClick(object sender,RoutedEventArgs e)=>await Investigate();
    private void OpenSettings(GuidedAction action)
    {try {DashboardCommands.OpenSettings(action);}catch {MessageBox.Show(this,"Nastavení Windows se nepodařilo otevřít.");}}
    private void StorageClick(object sender,RoutedEventArgs e)=>OpenSettings(GuidedAction.StorageSettings);
    private void AppsClick(object sender,RoutedEventArgs e)=>OpenSettings(GuidedAction.AppsSettings);
    private void StartupClick(object sender,RoutedEventArgs e)=>OpenSettings(GuidedAction.StartupSettings);
}
