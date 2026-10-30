using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using HostAgent.Core.Dashboard;
using HostAgent.Desktop;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class LocalizationSwitchTests
{
    [Fact]
    public Task Long_product_dialog_keeps_actions_visible_and_evidence_scrollable_in_selected_language()=>StaTest.RunAsync(()=>
    {
        var owner=new Window{Width=700,Height=700};owner.Show();
        var language=TestLocalization.Czech();language.SelectLanguage("de");
        Exception? failure=null;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
        {
            Window? dialog=null;
            try
            {
                dialog=owner.OwnedWindows.Cast<Window>().Single();dialog.UpdateLayout();
                var scroll=Descendants(dialog).OfType<ScrollViewer>().FirstOrDefault();
                Assert.NotNull(scroll);Assert.True(scroll.ScrollableHeight>0);
                Assert.Equal("de",dialog.Language.IetfLanguageTag);
                var button=Descendants(dialog).OfType<Button>().Single();
                Assert.Equal(language.Text("Confirm"),button.Content);
                var position=button.TranslatePoint(new Point(0,0),dialog);
                Assert.InRange(position.Y,0,dialog.ActualHeight-button.ActualHeight);
            }
            catch(Exception error){failure=error;}
            finally{dialog?.Close();}
        }));
        try{new LocalizedMessages(language).Show(owner,string.Join("\n",Enumerable.Repeat("synthetic evidence",256)));}
        finally{owner.Close();}
        if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=System.Windows.Media.VisualTreeHelper.GetChild(root,i);yield return child;
            foreach(var descendant in Descendants(child))yield return descendant;
        }
    }
    [Fact]
    public Task Switching_translates_loaded_UI_without_transport_or_operation_side_effects()=>StaTest.RunAsync(()=>
    {
        using var fixture=new DesktopLocalizationTests.PreferencesFixture();
        var language=new DesktopLocalization(fixture.Preferences,CultureInfo.GetCultureInfo("cs-CZ"));
        var now=DateTimeOffset.UtcNow;
        var incident=new ViewIncident("11111111111111111111111111111111","Active","memory_pressure",83,"High",new DateTimeOffset(2026,1,2,3,4,0,TimeSpan.Zero),1);
        var snapshot=new DashboardSnapshot(now,"Shadow",true,true,new(now,42.5),new(now,64.5),new(now,7200),
            [new("C:",1000,100000,now)],[new(42,"synthetic",1.5,1610612736){StartTimeUtcTicks=100}],[],1,[incident],[new("cpu-memory","collector_partial",true,["process_cpu_time_access_denied"])],[],[]);
        var requests=0;
        var controller=new DashboardController((payload,_)=>
        {
            requests++;
            return ValueTask.FromResult(ViewProtocol.Parse(payload).Command switch
            {
                "overview"=>AdminProtocol.Reply(true,"overview",snapshot),
                "history"=>AdminProtocol.Reply(true,"history",Array.Empty<ViewMetric>()),
                "incident"=>AdminProtocol.Reply(true,"incident",new ViewIncidentDetail(incident,"probable_cause_estimate",[new("synthetic",1.5,incident.AtUtc)],false)),
                _=>AdminProtocol.Reply(false,"view_unavailable")
            });
        });
        var window=new MainWindow(controller,language);
        try
        {
            window.Show();PumpUntil(()=>controller.Snapshot is not null);
            window.Width=850;
            var incidents=(DataGrid)window.FindName("IncidentsGrid");incidents.SelectedIndex=0;
            PumpUntil(()=>((TextBox)window.FindName("DetailText")).Text.Contains(incident.Id));
            ((DataGrid)window.FindName("ProcessGrid")).SelectedIndex=0;
            ((TextBox)window.FindName("AliasInput")).Text="preserve-alias";
            ((TextBox)window.FindName("PidInput")).Text="4200";
            ((TextBox)window.FindName("TicksInput")).Text="10000";
            var loaded=controller.Snapshot;var before=requests;
            var choice=(ComboBox)window.FindName("LanguageChoice");
            foreach(var code in new[]{"en","de","cs-CZ","en"})
            {
                choice.SelectedValue=code;Drain();
                Assert.Equal(code,language.LanguageCode);
                Assert.Equal(language.Text("Refresh"),((Button)window.FindName("RefreshButton")).Content);
                Assert.Equal(language.Text("InterfaceLanguage"),AutomationProperties.GetName(choice));
                Assert.Equal(language.Text("FreeSpace"),((DataGrid)window.FindName("DisksGrid")).Columns[1].Header);
                Assert.Contains(language.Text("ReadOnly"),((TextBlock)window.FindName("StatusText")).Text);
                Assert.Contains(code=="en"?"1.5": "1,5",((TextBox)window.FindName("DetailText")).Text);
                Assert.Equal(before,requests);Assert.Same(loaded,controller.Snapshot);
                Assert.Equal("Shadow",controller.Snapshot!.Mode);Assert.True(controller.Snapshot.ReadOnly);
                Assert.False(((Button)window.FindName("OperationButton")).IsEnabled);
                Assert.Equal("preserve-alias",((TextBox)window.FindName("AliasInput")).Text);
                Assert.Equal("4200",((TextBox)window.FindName("PidInput")).Text);
                Assert.Equal("10000",((TextBox)window.FindName("TicksInput")).Text);
                Assert.Equal(42,((ViewProcess)((DataGrid)window.FindName("ProcessGrid")).SelectedItem).Pid);
                Assert.Equal(incident.Id,((ViewIncident)incidents.SelectedItem).Id);
                Assert.Equal(code,new DesktopLocalization(fixture.Preferences,CultureInfo.GetCultureInfo("fr")).LanguageCode);
                var tabs=(TabControl)window.FindName("DashboardTabs");tabs.SelectedIndex=1;window.UpdateLayout();Drain();
                var date=Assert.IsType<TextBlock>(incidents.Columns[0].GetCellContent(incidents.Items[0]));
                Assert.Contains(code=="en"?"1/2/2026":"02.01.2026",date.Text);
                Assert.True(incidents.Columns[1].ActualWidth>=180,"The cause/code column must remain reachable at the minimum window width.");
                if(code=="de")VerifyLocalizedExport(window,language,fixture.Directory,incident.Id);
                Capture(window,code+"-incidents");
                tabs.SelectedIndex=0;window.UpdateLayout();Drain();Capture(window,code+"-overview");
            }
        }
        finally{window.Close();}
    });

    [Fact]
    public Task Switching_while_disconnected_keeps_unavailable_values_and_actions_blocked()=>StaTest.RunAsync(()=>
    {
        var language=TestLocalization.Czech();
        var controller=new DashboardController((_,_)=>ValueTask.FromResult(AdminProtocol.Reply(false,"view_unavailable")));
        var window=new MainWindow(controller,language);
        try
        {
            window.Show();PumpUntil(()=>((TextBlock)window.FindName("CpuText")).Text=="—");
            language.SelectLanguage("de");Drain();
            Assert.Equal(language.Text("TheLocalServiceIsUnavailableMakeSureAVersionOfHostAgentThatSupportsT"),((TextBlock)window.FindName("StatusText")).Text);
            Assert.Equal("—",((TextBlock)window.FindName("CpuText")).Text);
            Assert.False(((Button)window.FindName("OperationButton")).IsEnabled);
            Assert.Null(controller.Snapshot);
        }
        finally{window.Close();}
    });

    internal static void Drain()
    {
        var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
    }
    private static void VerifyLocalizedExport(Window owner,DesktopLocalization language,string directory,string incidentId)
    {
        var file=System.IO.Path.Combine(directory,"synthetic-report.json");Exception? failure=null;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
        {
            Window? dialog=null;
            try
            {
                dialog=owner.OwnedWindows.Cast<Window>().Single();
                Assert.Equal("de",dialog.Language.IetfLanguageTag);
                var path=Descendants(dialog).OfType<TextBox>().Single();path.Text=file;
                Assert.Equal(language.Text("FullPathToJSONFile"),AutomationProperties.GetName(path));
                Descendants(dialog).OfType<Button>().Single(x=>Equals(x.Content,language.Text("Save"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch(Exception error){failure=error;dialog?.Close();}
        }));
        Descendants(owner).OfType<Button>().Single(x=>Equals(x.Content,language.Text("ExportIncident"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        using var json=System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(file));
        Assert.Equal(1,json.RootElement.GetProperty("format").GetInt32());
        Assert.Equal(incidentId,json.RootElement.GetProperty("incident").GetProperty("Id").GetString());
    }
    internal static void Capture(Window window,string name)
    {
        var directory=Environment.GetEnvironmentVariable("HOSTAGENT_LOCALIZATION_CAPTURE");
        if(string.IsNullOrEmpty(directory))return;
        System.IO.Directory.CreateDirectory(directory);
        window.UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream=System.IO.File.Create(System.IO.Path.Combine(directory,name+".png"));encoder.Save(stream);
    }
    internal static void PumpUntil(Func<bool> condition)
    {
        var deadline=DateTime.UtcNow.AddSeconds(8);
        while(!condition()&&DateTime.UtcNow<deadline){Drain();Thread.Sleep(10);}
        Assert.True(condition());
    }
}
