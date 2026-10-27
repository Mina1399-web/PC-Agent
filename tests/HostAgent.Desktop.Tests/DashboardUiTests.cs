using System.Windows;
using System.Globalization;
using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Polyline = System.Windows.Shapes.Polyline;
using HostAgent.Core.Dashboard;
using HostAgent.Desktop;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class DashboardUiTests
{
    // Each test process keeps its own synthetic captures; never overwrite earlier evidence.
    private static readonly string CaptureDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HostAgent-DEV-evidence",
        "dashboard-design-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task Real_wpf_window_renders_synthetic_data_and_clears_disconnected_metrics()
    {
        await StaTest.RunAsync(()=>
        {
            MainWindow? window=null;
            try
            {
                var now=DateTimeOffset.UtcNow;var online=true;var stale=false;var historyRequests=new List<string>();
                var incident=new ViewIncident(Guid.NewGuid().ToString("N"),"Active","windows_bugcheck",88,"High",now.AddHours(-1),2);
                var view=new DashboardSnapshot(now,"Shadow",true,false,new(now,42),new(now,64),new(now,7200),
                    [new("C:",5_368_709_120,137_438_953_472,now)],[new(42,"Ukázkový proces",1,1_610_612_736){StartTimeUtcTicks=100}],
                    [new("Ukázková služba","Running")],1,[incident,incident with{Id=Guid.NewGuid().ToString("N"),State="Resolved",Code="memory_pressure"}],
                    [new("cpu-memory","collector_ok",false),new("disk","smart_unavailable",false)],[],[]);
                var history=Enumerable.Range(0,96).Where(i=>i is <40 or >52)
                    .Select(i=>new ViewMetric(now.AddMinutes(-15*(96-i)),42+16*Math.Sin(i/8d))).ToArray();
                var controller=new DashboardController((payload,_)=>
                {
                    var request=ViewProtocol.Parse(payload);
                    if(request.Command=="history")historyRequests.Add(request.Body.GetProperty("metric").GetString()??"null");
                    if(!online)return ValueTask.FromResult(AdminProtocol.Reply(false,"view_unavailable"));
                    return ValueTask.FromResult(request.Command switch
                    {
                        "overview"=>AdminProtocol.Reply(true,"overview",stale?view with{GeneratedAtUtc=now.AddHours(-1)}:view),
                        "history"=>AdminProtocol.Reply(true,"history",request.Body.GetProperty("metric").GetString()=="cpu"?history:history.Select(x=>x with{Value=x.Value+20}).ToArray()),
                        "incident"=>AdminProtocol.Reply(true,"incident",new ViewIncidentDetail(incident,"stop_code_classification_only",[new("eventlog",10,now)],false)),
                        _=>AdminProtocol.Reply(false,"view_unavailable")
                    });
                });
                window=new MainWindow(controller,TestLocalization.Czech());window.Show();
                PumpUntil(()=>((TextBlock)window.FindName("CpuText")).Text.Contains("42"));
                Assert.Contains("64",((TextBlock)window.FindName("MemoryText")).Text);
                Assert.Equal("2 h 00 min",((TextBlock)window.FindName("UptimeText")).Text);
                Assert.Equal(Color.FromRgb(0x14,0x24,0x3B),((SolidColorBrush)((TextBlock)window.FindName("CpuText")).Foreground).Color);
                Assert.False(((Button)window.FindName("OperationButton")).IsEnabled);
                Assert.Contains("Shadow",((TextBlock)window.FindName("OperationHintText")).Text);
                Assert.Equal("1",((TextBlock)window.FindName("IncidentCountText")).Text);
                Assert.Single(((DataGrid)window.FindName("DisksGrid")).Items.Cast<object>());
                Assert.Single(((DataGrid)window.FindName("ServicesGrid")).Items.Cast<object>());
                PumpUntil(()=>((Canvas)window.FindName("Chart")).Children.OfType<Polyline>().Count()==4);
                var chartLines=((Canvas)window.FindName("Chart")).Children.OfType<Polyline>().ToArray();
                var chartDetails=string.Join(",",chartLines.Select(line=>line.Points.Count.ToString(CultureInfo.InvariantCulture)));
                Assert.True(chartLines.Length==4,$"historyRequests={string.Join(",",historyRequests)}; linePointCounts={chartDetails}");
                Assert.Equal(Visibility.Collapsed,((TextBlock)window.FindName("ChartEmptyText")).Visibility);
                var processGrid=(DataGrid)window.FindName("ProcessGrid");processGrid.SelectedIndex=0;
                Assert.Equal("42",((TextBox)window.FindName("PidInput")).Text);
                Assert.Equal("100",((TextBox)window.FindName("TicksInput")).Text);
                var tabs=(TabControl)window.FindName("DashboardTabs");
                Assert.True(((TabItem)tabs.Items[1]).Focus());
                Assert.Equal(1,tabs.SelectedIndex);
                ((TabItem)tabs.Items[0]).Focus();
                Assert.Equal(0,tabs.SelectedIndex);
                foreach(var size in new[]{(1280d,880d),(1000d,760d),(850d,620d)})
                {
                    window.Width=size.Item1;window.Height=size.Item2;window.UpdateLayout();
                    Assert.Equal(window.ActualWidth<1050?2:4,((System.Windows.Controls.Primitives.UniformGrid)window.FindName("MetricsGrid")).Columns);
                    Capture(window,$"overview-{size.Item1:0}-synthetic");
                }
                window.Width=1280;window.Height=880;
                var overviewScroll=(ScrollViewer)((TabItem)tabs.Items[0]).Content;
                overviewScroll.ScrollToBottom();Capture(window,"overview-tables-synthetic");overviewScroll.ScrollToTop();
                for(var index=1;index<tabs.Items.Count;index++)
                {
                    tabs.SelectedIndex=index;window.UpdateLayout();
                    if(index==1)
                    {
                        ((DataGrid)window.FindName("IncidentsGrid")).SelectedIndex=0;
                        PumpUntil(()=>((TextBox)window.FindName("DetailText")).Text.Contains("stop kódu"));
                        Assert.Equal(incident.Id,((TextBox)window.FindName("OperationIncident")).Text);
                    }
                    Capture(window,$"section-{index}-synthetic");
                }
                window.Width=850;window.Height=620;Capture(window,"maintenance-850-synthetic");
                window.Width=1280;window.Height=880;
                tabs.SelectedIndex=0;
                online=false;
                var refresh=(Button)window.FindName("RefreshButton");
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(()=>((TextBlock)window.FindName("CpuText")).Text=="—");
                Assert.Null(controller.Snapshot);Assert.Empty(((DataGrid)window.FindName("DisksGrid")).Items.Cast<object>());
                Assert.Equal("—",((TextBlock)window.FindName("IncidentCountText")).Text);
                Assert.Empty(((DataGrid)window.FindName("ServicesGrid")).Items.Cast<object>());
                Assert.Equal(Visibility.Visible,((TextBlock)window.FindName("ChartEmptyText")).Visibility);
                Assert.Empty(((Canvas)window.FindName("Chart")).Children.OfType<Polyline>());
                Assert.Equal("",((TextBox)window.FindName("OperationIncident")).Text);
                Capture(window,"disconnected");
                online=true;stale=true;
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(()=>((TextBlock)window.FindName("StatusText")).Text.Contains("zastaralá"));
                Assert.Null(controller.Snapshot);Assert.False(((Button)window.FindName("OperationButton")).IsEnabled);
                Capture(window,"stale");
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    online = false;
                    refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => ((TextBlock)window.FindName("CpuText")).Text == "—");
                    Assert.Null(controller.Snapshot);
                    Assert.False(((Button)window.FindName("OperationButton")).IsEnabled);
                    online = true; stale = false;
                    refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => ((TextBlock)window.FindName("CpuText")).Text == "42 %");
                    Assert.NotNull(controller.Snapshot);
                }
            }
            finally {window?.Close();}
        }, TimeSpan.FromSeconds(60));
    }
    private static void Capture(MainWindow window,string name)
    {
        window.Title="Host Agent — UKÁZKOVÁ DATA / test rozhraní";
        System.Windows.Automation.AutomationProperties.SetName(window,"Host Agent, ukázková syntetická data");
        var status=(TextBlock)window.FindName("StatusText");
        if(!status.Text.StartsWith("UKÁZKOVÁ DATA"))status.Text="UKÁZKOVÁ DATA • "+status.Text;
        window.UpdateLayout();
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
        var content=(FrameworkElement)window.Content;
        var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory=CaptureDirectory;Directory.CreateDirectory(directory);
        using var output=File.Create(Path.Combine(directory,name+".png"));encoder.Save(output);
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var deadline=DateTime.UtcNow.AddSeconds(8);
        while(!condition() && DateTime.UtcNow<deadline)
        {
            var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));
            Dispatcher.PushFrame(frame);Thread.Sleep(20);
        }
        Assert.True(condition());
    }
}
