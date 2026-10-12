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

    private static async Task WithWindow(Action<MainWindow, FakeView> check)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                var view = new FakeView();
                window = new MainWindow(new DashboardController(view.Send));
                window.Show();
                PumpUntil(() => ((TextBlock)window.FindName("CpuText")).Text.Contains("42"));
                check(window, view);
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
            finally { window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

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

        public FakeView()
        {
            var now = DateTimeOffset.UtcNow;
            Snapshot = new(now, "Shadow", true, false, new(now, 42), new(now, 64), new(now, 7200), [],
                [new(42, "OriginalInstance", 0, 1024) { StartTimeUtcTicks = 100 }], [], 1, [Incident], [], [], []);
        }

        public ValueTask<byte[]> Send(byte[] bytes, CancellationToken token)
        {
            var request = ViewProtocol.Parse(bytes);
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
