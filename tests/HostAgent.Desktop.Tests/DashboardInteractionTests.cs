using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HostAgent.Core.Dashboard;
using HostAgent.Desktop;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class DashboardInteractionTests
{
    [Fact]
    public Task Triage_link_selects_the_ranked_record_instead_of_the_previous_row() => WithWindow((window, view) =>
    {
        var now=DateTimeOffset.UtcNow;
        var lower=view.Incident with{Id="newer-warning",Code="disk_capacity",Severity="Warning",AtUtc=now.AddMinutes(-1)};
        var highest=view.Incident with{Code="windows_bugcheck",Severity="High",AtUtc=now.AddHours(-1)};
        view.Snapshot=view.Snapshot with{Incidents=[lower,highest]};
        Refresh(window);
        var grid=(DataGrid)window.FindName("IncidentsGrid");
        grid.SelectedIndex=0;
        ((Button)window.FindName("TriageActionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(highest.Id,Assert.IsType<ViewIncident>(grid.SelectedItem).Id);
        Assert.Contains("UTC",((TextBlock)window.FindName("TriageIncidentText")).Text);
        Assert.Equal(1,((TabControl)window.FindName("DashboardTabs")).SelectedIndex);
    });

    [Fact]
    public Task Information_severity_ranks_above_unknown_and_preserves_unknown_detail() => WithWindow((window, view) =>
    {
        var now=DateTimeOffset.UtcNow;
        var unknown=view.Incident with{Id="unknown",Code="unknown_diagnostic",Severity="new-severity",AtUtc=now.AddMinutes(-1)};
        var information=view.Incident with{Severity="Information",AtUtc=now.AddMinutes(-2)};
        view.Snapshot=view.Snapshot with{Incidents=[unknown,information]};
        Refresh(window);
        ((Button)window.FindName("TriageActionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var grid=(DataGrid)window.FindName("IncidentsGrid");
        Assert.Equal(information.Id,Assert.IsType<ViewIncident>(grid.SelectedItem).Id);
        Assert.Contains("Informace",((TextBlock)window.FindName("TriageIncidentText")).Text);
        Assert.Contains(grid.Items.Cast<ViewIncident>(),x=>x.Id==unknown.Id && x.Severity=="new-severity");
    });

    [Fact]
    public Task Triage_surfaces_recorded_fault_and_current_disk_pressure_independently_of_collector_health() => WithWindow((window, view) =>
    {
        var now=DateTimeOffset.UtcNow;
        view.Snapshot=view.Snapshot with
        {
            Health=[new("cpu-memory","collector_healthy",false)],
            Incidents=[view.Incident with{Code="windows_bugcheck",AtUtc=now.AddHours(-1)}],
            Disks=[new("C:",4,100,now)]
        };
        Refresh(window);
        Assert.Contains("Pád Windows",((TextBlock)window.FindName("TriageIncidentText")).Text);
        Assert.Contains("před 60 min",((TextBlock)window.FindName("TriageIncidentText")).Text);
        Assert.Contains("neprokazuje",((TextBlock)window.FindName("TriageActionText")).Text);
        Assert.Contains("místní služby Host Agent",((TextBlock)window.FindName("TriageEvidenceMetaText")).Text);
        Assert.Contains("Prohlédnout uloženou událost",((Button)window.FindName("TriageActionButton")).Content.ToString());
        Assert.Contains("4 %",((TextBlock)window.FindName("TriageDiskText")).Text);
        Assert.Contains("0 z 1",((TextBlock)window.FindName("HealthSummaryText")).Text);
        view.Snapshot=view.Snapshot with{GeneratedAtUtc=now.AddHours(-1)};
        Refresh(window);
        Assert.DoesNotContain("Pád Windows",((TextBlock)window.FindName("TriageIncidentText")).Text);
        Assert.Equal("Obnovit měření",((Button)window.FindName("TriageActionButton")).Content.ToString());
        Assert.Equal("refresh",((Button)window.FindName("TriageActionButton")).Tag);
        Assert.Contains("Čas a zdroj měření",((TextBlock)window.FindName("TriageEvidenceMetaText")).Text);
        Assert.Equal(Visibility.Collapsed,((TextBlock)window.FindName("TriageDiskText")).Visibility);
    });

    [Fact]
    public Task Situation_uses_a_safe_recommendation_step_when_there_is_no_open_incident() => WithWindow((window, view) =>
    {
        view.Snapshot=view.Snapshot with{Incidents=[]};
        Refresh(window);
        var action=(Button)window.FindName("TriageActionButton");
        Assert.Equal("Zobrazit doporučené kroky",action.Content.ToString());
        Assert.Equal("2",action.Tag);
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2,((TabControl)window.FindName("DashboardTabs")).SelectedIndex);
        Assert.False(((Button)window.FindName("OperationButton")).IsEnabled);
    });

    [Fact]
    public Task Empty_recommendations_offer_read_only_saved_evidence_review() => WithWindow((window, view) =>
    {
        view.Snapshot=view.Snapshot with{Incidents=[],Health=[new("cpu-memory","collector_healthy",false)]};
        Refresh(window);
        var panel=(StackPanel)window.FindName("RecommendationsPanel");
        var buttons=panel.Children.OfType<Border>().SelectMany(card=>(card.Child as StackPanel)?.Children.OfType<Button>()??Enumerable.Empty<Button>());
        Assert.Contains(buttons,button=>button.Content?.ToString()=="Prověřit uložené důkazy");
    });

    [Fact]
    public Task Manual_action_has_no_default_and_requires_an_explicit_choice() => WithWindow((window, view) =>
    {
        view.Snapshot=view.Snapshot with{Mode="Confirmation",ReadOnly=false};
        Refresh(window);
        var choice=(ComboBox)window.FindName("OperationChoice");
        var action=(Button)window.FindName("OperationButton");
        Assert.Contains("není doporučením opravy",((TextBlock)window.FindName("ActionRecommendationNotice")).Text);
        Assert.Equal(-1,choice.SelectedIndex);
        Assert.False(action.IsEnabled);
        choice.SelectedIndex=0;
        Assert.False(action.IsEnabled);
        Assert.Contains("událost",((TextBlock)window.FindName("OperationHintText")).Text,StringComparison.OrdinalIgnoreCase);
        ((DataGrid)window.FindName("IncidentsGrid")).SelectedIndex=0;
        Assert.Contains("alias",((TextBlock)window.FindName("OperationHintText")).Text,StringComparison.OrdinalIgnoreCase);
        ((TextBox)window.FindName("AliasInput")).Text="review-target";
        Assert.True(action.IsEnabled);
        choice.SelectedIndex=-1;
        Assert.False(action.IsEnabled);
    });

    [Fact]
    public void Standard_uses_localized_diagnostic_labels_and_developer_detail_can_show_codes()
    {
        var converter=new TechnicalTextConverter();
        var culture=CultureInfo.GetCultureInfo("cs-CZ");
        var diagnosis=converter.Convert("other_diagnosis",typeof(string),"Label",culture).ToString();
        var partial=converter.Convert("collectors_partial",typeof(string),"Label",culture).ToString();
        var unknown=converter.Convert("future_diagnostic",typeof(string),"Label",culture).ToString();
        var developer=converter.Convert("other_diagnosis",typeof(string),"Developer",culture).ToString();
        Assert.Contains("Jiný zaznamenaný stav",diagnosis);
        Assert.DoesNotContain("other_diagnosis",diagnosis);
        Assert.Contains("Částečná data",partial);
        Assert.DoesNotContain("collectors_partial",partial);
        Assert.DoesNotContain("future_diagnostic",unknown);
        Assert.Contains("other_diagnosis",developer);
    }

    [Fact]
    public Task Process_identity_fields_are_only_presented_for_process_actions() => WithWindow((window, view) =>
    {
        var fields=(StackPanel)window.FindName("ProcessIdentityPanel");
        var operation=(ComboBox)window.FindName("OperationChoice");
        Assert.Equal(Visibility.Collapsed,fields.Visibility);
        operation.SelectedIndex=3;
        Assert.Equal(Visibility.Visible,fields.Visibility);
        operation.SelectedIndex=0;
        Assert.Equal(Visibility.Collapsed,fields.Visibility);
    });

    [Fact]
    public Task Theme_switch_keeps_data_selection_and_approval_state() => WithWindow((window, view) =>
    {
        var process = (DataGrid)window.FindName("ProcessGrid");
        process.SelectedIndex = 0;
        var cpu = ((TextBlock)window.FindName("CpuText")).Text;
        var operationEnabled = ((Button)window.FindName("OperationButton")).IsEnabled;
        var theme = (Button)window.FindName("ThemeButton");
        theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Světlý motiv", theme.Content);
        Assert.Equal(cpu, ((TextBlock)window.FindName("CpuText")).Text);
        Assert.Equal("42", ((TextBox)window.FindName("PidInput")).Text);
        Assert.Equal(operationEnabled, ((Button)window.FindName("OperationButton")).IsEnabled);
        window.Localization.SelectLanguage("de");
        Assert.Equal("Heller Modus", theme.Content);
        theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Dunkler Modus", theme.Content);
        Assert.Equal(cpu, ((TextBlock)window.FindName("CpuText")).Text);
    });

    [Fact]
    public Task Primary_safe_step_navigates_to_evidence_without_requesting_a_repair() => WithWindow((window, view) =>
    {
        ((Button)window.FindName("TriageActionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, ((TabControl)window.FindName("DashboardTabs")).SelectedIndex);
        var incidentSummary=((TextBlock)window.FindName("OperationIncidentSummary")).Text;
        Assert.DoesNotContain(view.Incident.Id,incidentSummary);
        Assert.DoesNotContain("Není vybraná",incidentSummary);
        Assert.False(((Button)window.FindName("OperationButton")).IsEnabled);
    });

    [Fact]
    public Task Metric_meters_clear_when_current_measurements_are_missing() => WithWindow((window, view) =>
    {
        view.Snapshot = view.Snapshot with { Cpu = new(DateTimeOffset.UtcNow, 37) };
        Refresh(window);
        var meter = (ProgressBar)window.FindName("CpuMeter");
        Assert.Equal(37, meter.Value);
        Assert.Equal(Visibility.Visible, meter.Visibility);
        view.Snapshot = view.Snapshot with { Cpu = null };
        Refresh(window);
        Assert.Equal(0, meter.Value);
        Assert.Equal(Visibility.Hidden, meter.Visibility);
        Assert.Equal("—", ((TextBlock)window.FindName("CpuText")).Text);
    });

    [Fact]
    public Task Summary_reports_missing_measurements_and_clears_previous_health_on_disconnect() => WithWindow((window, view) =>
    {
        view.Snapshot = view.Snapshot with { Degraded = true, Cpu = null,
            Health = [new("cpu-memory", "collector_partial", true, ["process_cpu_time_access_denied"])] };
        Refresh(window);
        var text = (TextBlock)window.FindName("HealthSummaryText");
        Assert.Contains("1 z 1", text.Text);
        Assert.Contains("Chybí aktuální celkové měření CPU", text.Text);
        view.Snapshot = view.Snapshot with { GeneratedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-4) };
        Refresh(window);
        Assert.Contains("Aktuální souhrn není dostupný", text.Text);
        Assert.DoesNotContain("1 z 1", text.Text);
    });

    [Fact]
    public Task Empty_health_list_is_reported_as_missing_data() => WithWindow((window, view) =>
    {
        Assert.Contains("Stav komponent zatím nebyl předán", ((TextBlock)window.FindName("HealthSummaryText")).Text);
        view.Snapshot = view.Snapshot with { Degraded = true };
        Refresh(window);
        Assert.Contains("bez podrobných důvodů", ((TextBlock)window.FindName("HealthSummaryText")).Text);
    });

    [Fact]
    public Task Fresh_overview_does_not_hide_stale_or_future_metric_timestamps() => WithWindow((window, view) =>
    {
        view.Snapshot = view.Snapshot with { Cpu = new(DateTimeOffset.UtcNow.AddMinutes(-4), 12),
            Memory = new(DateTimeOffset.UtcNow.AddMinutes(2), 30) };
        Refresh(window);
        var text = ((TextBlock)window.FindName("HealthSummaryText")).Text;
        Assert.Contains("Chybí aktuální celkové měření CPU", text);
        Assert.Contains("Chybí aktuální celkové měření paměti", text);
    });

    [Fact]
    public Task Health_table_explains_partial_data_without_exposing_raw_codes() => WithWindow((window, view) =>
    {
        view.Snapshot = view.Snapshot with
        {
            Degraded = true,
            Health = [new("cpu-memory", "collector_partial", true,
                ["process_cpu_time_access_denied", "future_collector_reason"])]
        };
        Refresh(window);
        ((TabControl)window.FindName("DashboardTabs")).SelectedIndex = 3;
        var grid = (DataGrid)window.FindName("HealthGrid");
        grid.UpdateLayout();
        var row = grid.Items[0];
        var explanation = Assert.IsType<TextBlock>(grid.Columns[2].GetCellContent(row));
        Assert.Contains("Čas CPU některých procesů", explanation.Text);
        Assert.Contains("další omezení sběru dat", explanation.Text);
        Assert.DoesNotContain("future_collector_reason", explanation.Text);
        Assert.Equal(explanation.Text, explanation.ToolTip);
        var code=Assert.IsType<TextBlock>(grid.Columns[1].GetCellContent(row)).Text;
        Assert.Contains("Částečná data",code);
        Assert.DoesNotContain("collector_partial",code);
        Assert.True(Assert.IsType<ViewHealth>(row).Degraded);
    });

    [Fact]
    public Task Language_switch_during_an_inflight_refresh_preserves_selection_and_operation_identity()=>WithWindow((window,view)=>
    {
        var grid=(DataGrid)window.FindName("ProcessGrid");grid.SelectedIndex=0;
        view.PendingOverview=new TaskCompletionSource<byte[]>();
        try
        {
            Refresh(window);PumpUntil(()=>view.OverviewDelayed);
            window.Localization.SelectLanguage("de");Drain();
            Assert.IsType<ViewProcess>(grid.SelectedItem);
            Assert.Equal("42",((TextBox)window.FindName("PidInput")).Text);
            Assert.Equal("100",((TextBox)window.FindName("TicksInput")).Text);
        }
        finally{view.PendingOverview.SetResult(AdminProtocol.Reply(true,"overview",view.Snapshot));Drain();}
    });

    [Fact]
    public Task Language_choice_is_available_to_keyboard_and_accessibility_clients() => WithWindow((window, _) =>
    {
        var choice = Assert.IsType<ComboBox>(window.FindName("LanguageChoice"));
        Assert.True(choice.Focusable);
        Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(choice)));
        Assert.Equal(3, choice.Items.Count);
    });

    [Theory]
    [InlineData(-31, -14, 4)]
    [InlineData(-29, -1, 2)]
    public Task Chart_gaps_follow_missing_quarter_hour_buckets(int firstOffset, int secondOffset, int expectedSegments) => WithWindow((window, view) =>
    {
        var interval = TimeSpan.FromMinutes(15).Ticks;
        var bucket = new DateTimeOffset(DateTimeOffset.UtcNow.UtcTicks / interval * interval, TimeSpan.Zero);
        view.History = [new(bucket.AddMinutes(firstOffset), 40), new(bucket.AddMinutes(secondOffset), 50)];
        Refresh(window);
        Assert.Equal(expectedSegments, ((Canvas)window.FindName("Chart")).Children.OfType<System.Windows.Shapes.Polyline>().Count());
    });

    [Fact]
    public Task Refresh_preserves_only_the_exact_selected_process_instance() => WithWindow((window, view) =>
    {
        var grid = (DataGrid)window.FindName("ProcessGrid");
        grid.SelectedIndex = 0;
        view.Snapshot = view.Snapshot with { Processes = [new(42, "OriginalInstance", 15, 2048) { StartTimeUtcTicks = 100 }] };
        Refresh(window);
        Assert.IsType<ViewProcess>(grid.SelectedItem);
        Assert.Equal("42", ((TextBox)window.FindName("PidInput")).Text);
        Assert.Equal("100", ((TextBox)window.FindName("TicksInput")).Text);
    });

    [Fact]
    public Task A_reused_pid_clears_the_previous_instance_and_its_operation_fields() => WithWindow((window, view) =>
    {
        var grid = (DataGrid)window.FindName("ProcessGrid");
        grid.SelectedIndex = 0;
        view.Snapshot = view.Snapshot with { Processes = [new(42, "NewInstance", 0, 1024) { StartTimeUtcTicks = 200 }] };
        Refresh(window);
        Assert.Null(grid.SelectedItem);
        Assert.Equal("", ((TextBox)window.FindName("PidInput")).Text);
        Assert.Equal("", ((TextBox)window.FindName("TicksInput")).Text);
    });

    [Fact]
    public Task Deselecting_an_incident_clears_the_visible_detail_and_operation_target() => WithWindow((window, view) =>
    {
        var grid = (DataGrid)window.FindName("IncidentsGrid");
        grid.SelectedIndex = 0;
        PumpUntil(() => !((TextBlock)window.FindName("OperationIncidentSummary")).Text.Contains("Není vybraná"));
        grid.SelectedIndex = -1;
        Assert.DoesNotContain(view.Incident.Id, ((TextBox)window.FindName("DetailText")).Text);
        Assert.Contains("Není vybraná",((TextBlock)window.FindName("OperationIncidentSummary")).Text);
    });

    [Fact]
    public async Task New_profile_starts_standard_and_gates_developer_tabs_until_explicit_enable()
    {
        await StaTest.RunAsync(() =>
        {
            MainWindow? window=null;
            try
            {
                var view=new FakeView();var inventoryReads=0;
                window=new MainWindow(new DashboardController(view.Send),TestLocalization.CzechStandard(),null,
                    _=>{inventoryReads++;return new WindowsInventory([]);},()=>true);
                window.Show();PumpUntil(()=>((TextBlock)window.FindName("CpuText")).Text.Contains("42"));
                Assert.Equal(DesktopMode.Standard,window.CurrentDesktopMode);
                var tabs=(TabControl)window.FindName("DashboardTabs");
                Assert.All(tabs.Items.Cast<TabItem>(),tab=>Assert.Equal(Visibility.Visible,tab.Visibility));
                Assert.False(((TabItem)tabs.Items[4]).IsEnabled);
                Assert.True(((TabItem)tabs.Items[5]).IsEnabled);
                Assert.Contains("Standardní režim",((Button)window.FindName("DesktopModeButton")).Content.ToString());
                Assert.Equal(new[]{"Situace","Události","Doporučení","Systém","Údržba","Nástroje"},
                    tabs.Items.Cast<TabItem>().Select(System.Windows.Automation.AutomationProperties.GetName));
                Assert.True(DesktopModePolicy.CanNavigateTo(3,DesktopMode.Standard));
                Assert.False(DesktopModePolicy.CanNavigateTo(4,DesktopMode.Standard));
                Assert.True(DesktopModePolicy.CanNavigateTo(5,DesktopMode.Standard));
                Assert.True(DesktopModePolicy.CanNavigateTo(5,DesktopMode.Developer));
                Assert.Equal("Prohlédnout uloženou událost",((Button)window.FindName("TriageActionButton")).Content.ToString());
                tabs.SelectedIndex=5;Drain();
                Assert.Equal(5,tabs.SelectedIndex);Assert.True(inventoryReads>0);

                ((Button)window.FindName("DesktopModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(DesktopMode.Developer,window.CurrentDesktopMode);
                Assert.Equal(DesktopMode.Developer,window.Localization.ReadDesktopMode());
                Assert.True(((TabItem)tabs.Items[4]).IsEnabled);
                Assert.True(((TabItem)tabs.Items[5]).IsEnabled);
                Assert.Contains("Vývojářský režim zapnutý",((Button)window.FindName("DesktopModeButton")).Content.ToString());
                tabs.SelectedIndex=5;Drain();
                Assert.True(inventoryReads>0);
                Assert.Equal("Shadow",view.Snapshot.Mode); // UI preference did not change service safety mode.

                ((Button)window.FindName("DesktopModeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(DesktopMode.Standard,window.CurrentDesktopMode);
                Assert.Equal(5,tabs.SelectedIndex);
                Assert.Equal("Shadow",view.Snapshot.Mode);
            }
            finally{window?.Close();}
        });
    }

    [Fact]
    public Task An_older_detail_reply_cannot_replace_a_newer_reply_for_the_same_incident() => WithWindow((window, view) =>
    {
        view.DelayDetails = true;
        var grid = (DataGrid)window.FindName("IncidentsGrid");
        grid.SelectedIndex = 0;
        PumpUntil(() => view.Details.Count == 1);
        grid.SelectedIndex = -1;
        grid.SelectedIndex = 0;
        PumpUntil(() => view.Details.Count == 2);
        view.Details[1].SetResult(view.Detail("newer_evidence"));
        PumpUntil(() => ((TextBox)window.FindName("DetailText")).Text.Contains("newer_evidence"));
        view.Details[0].SetResult(view.Detail("older_evidence"));
        Drain();
        Assert.Contains("newer_evidence", ((TextBox)window.FindName("DetailText")).Text);
        Assert.DoesNotContain("older_evidence", ((TextBox)window.FindName("DetailText")).Text);
    });

    [Fact]
    public async Task Sta_fixture_waits_for_window_cleanup_before_reporting_completion()
    {
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCleanup = new ManualResetEventSlim();
        var fixture = WithWindow((window, _) => window.Closed += (_, _) =>
        {
            cleanupEntered.TrySetResult();
            if (!releaseCleanup.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Synthetic window cleanup was not released.");
        });
        try
        {
            await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // A bounded negative observation while the fixture teardown is
            // deliberately blocked: completion cannot precede its release.
            var first = await Task.WhenAny(fixture, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.NotSame(fixture, first);
        }
        finally
        {
            releaseCleanup.Set();
            await fixture;
        }
    }

    private static Task WithWindow(Action<MainWindow, FakeView> check) => StaTest.RunAsync(() =>
    {
        MainWindow? window = null;
        try
        {
            var view = new FakeView();
            window = new MainWindow(new DashboardController(view.Send),TestLocalization.Czech());
            window.Show();
            PumpUntil(() => ((TextBlock)window.FindName("CpuText")).Text.Contains("42"));
            check(window, view);
        }
        finally { window?.Close(); }
    });

    private static void Refresh(MainWindow window)
    {
        ((Button)window.FindName("RefreshButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Drain();
    }

    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition() && DateTime.UtcNow < deadline) { Drain(); Thread.Sleep(10); }
        Assert.True(condition());
    }

    private sealed class FakeView
    {
        public ViewIncident Incident { get; } = new(Guid.NewGuid().ToString("N"), "Active", "memory_pressure", 80, "High", DateTimeOffset.UtcNow, 1);
        public DashboardSnapshot Snapshot { get; set; }
        public bool DelayDetails { get; set; }
        public ViewMetric[] History { get; set; } = [];
        public List<TaskCompletionSource<byte[]>> Details { get; } = [];
        public TaskCompletionSource<byte[]>? PendingOverview {get;set;}
        public bool OverviewDelayed {get;private set;}

        public FakeView()
        {
            var now = DateTimeOffset.UtcNow;
            Snapshot = new(now, "Shadow", true, false, new(now, 42), new(now, 64), new(now, 7200), [],
                [new(42, "OriginalInstance", 0, 1024) { StartTimeUtcTicks = 100 }], [], 1, [Incident], [], [], []);
        }

        public ValueTask<byte[]> Send(byte[] bytes, CancellationToken token)
        {
            var request = ViewProtocol.Parse(bytes);
            if(request.Command=="overview"&&PendingOverview is not null){OverviewDelayed=true;return new(PendingOverview.Task);}
            if (request.Command == "incident" && DelayDetails)
            {
                var pending = new TaskCompletionSource<byte[]>();
                Details.Add(pending);
                return new(pending.Task);
            }
            // A refresh deserializes new row objects, as the real pipe does.
            return ValueTask.FromResult(request.Command switch
            {
                "overview" => AdminProtocol.Reply(true, "overview", Snapshot),
                "history" => AdminProtocol.Reply(true, "history", History),
                "incident" => Detail("current_evidence"),
                _ => AdminProtocol.Reply(false, "view_unavailable")
            });
        }

        public byte[] Detail(string source) => AdminProtocol.Reply(true, "incident",
            new ViewIncidentDetail(Incident, "probable_cause_estimate", [new(source, 1, DateTimeOffset.UtcNow)], false));
    }
}
