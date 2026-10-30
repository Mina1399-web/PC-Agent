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
    public Task Health_table_explains_partial_process_metrics_and_keeps_raw_diagnostics() => WithWindow((window, view) =>
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
        Assert.Contains("future_collector_reason", explanation.Text);
        Assert.Equal("process_cpu_time_access_denied, future_collector_reason", explanation.ToolTip);
        Assert.Contains("collector_partial", Assert.IsType<TextBlock>(grid.Columns[1].GetCellContent(row)).Text);
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
        PumpUntil(() => ((TextBox)window.FindName("DetailText")).Text.Contains(view.Incident.Id));
        grid.SelectedIndex = -1;
        Assert.DoesNotContain(view.Incident.Id, ((TextBox)window.FindName("DetailText")).Text);
        Assert.Equal("", ((TextBox)window.FindName("OperationIncident")).Text);
    });

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
