using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HostAgent.Core.Dashboard;
using HostAgent.Core.Models.Incidents;
using HostAgent.Desktop;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class IncidentHistoryUiTests
{
    [Fact]
    public Task More_pages_keep_selection_on_failure_and_late_replies_cannot_overwrite_refresh() => StaTest.RunAsync(() =>
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        ViewIncident Row(int i) => new(i.ToString("x32"), "Resolved", "memory_pressure", 80, "Warning", now, 1)
            { CreatedAtUtc = now, UpdatedAtUtc = now };
        byte[] Ok(string code, object data) => AdminProtocol.Reply(true, code, data);
        var loc = TestLocalization.CzechStandard();
        var rows = Enumerable.Range(6, 20).Reverse().Select(Row).ToArray();
        var view = new DashboardSnapshot(DateTimeOffset.UtcNow, "Shadow", true, false,
            null, null, null, [], [], [], null, rows, [], [], []) { SupportsIncidentHistory = true };
        var cursor = new IncidentHistoryCursor(now.UtcTicks, rows[^1].Id);
        var pending = new TaskCompletionSource<byte[]>();
        var firstReads = 0;
        var olderReads = 0;
        var controller = new DashboardController((payload, _) =>
        {
            var request = ViewProtocol.Parse(payload);
            if (request.Command == "overview") return ValueTask.FromResult(Ok("overview", view));
            if (request.Command == "history") return ValueTask.FromResult(Ok("history", Array.Empty<ViewMetric>()));
            if (request.Command == "incident")
                return ValueTask.FromResult(Ok("incident", new ViewIncidentDetail(rows[0], "probable_cause_estimate", [], false)));
            if (ViewProtocol.ReadIncidentCursor(request.Body) is null)
            {
                firstReads++;
                return ValueTask.FromResult(Ok("incident-page", firstReads == 1
                    ? new ViewIncidentPage(rows, cursor) : new ViewIncidentPage([rows[0], Row(4)], null)));
            }
            olderReads++;
            return olderReads == 1
                ? ValueTask.FromResult(AdminProtocol.Reply(false, "view_request_rejected", null))
                : new ValueTask<byte[]>(pending.Task);
        });
        var window = new MainWindow(controller, loc);
        try
        {
            window.Show();
            Drain();
            Assert.Equal(0, firstReads);
            var tabs = Assert.IsAssignableFrom<TabControl>(window.FindName("DashboardTabs"));
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Tag, DesktopRoute.Events));
            Drain();
            var grid = Assert.IsAssignableFrom<DataGrid>(window.FindName("IncidentsGrid"));
            var older = Assert.IsAssignableFrom<Button>(window.FindName("OlderIncidentsButton"));
            var refresh = Assert.IsAssignableFrom<Button>(window.FindName("RefreshHistoryButton"));
            var status = Assert.IsAssignableFrom<TextBlock>(window.FindName("IncidentsEmptyText"));
            Assert.Equal(20, grid.Items.Count);
            Assert.True(older.IsEnabled);
            grid.SelectedIndex = 0;
            Assert.Contains(loc.Text("IncidentHistoryRecoveryUnverified"),
                Assert.IsAssignableFrom<TextBox>(window.FindName("DetailText")).Text);
            var selectedId = Assert.IsType<ViewIncident>(grid.SelectedItem).Id;

            older.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain();
            Assert.Equal(loc.Text("IncidentHistoryOlderFailed"), status.Text);
            Assert.Equal(selectedId, Assert.IsType<ViewIncident>(grid.SelectedItem).Id);
            Assert.Equal(20, grid.Items.Count);

            older.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain();
            Assert.Equal(2, grid.Items.Count);
            Assert.Equal(selectedId, Assert.IsType<ViewIncident>(grid.SelectedItem).Id);
            Assert.False(older.IsEnabled);
            pending.SetResult(Ok("incident-page", new ViewIncidentPage([Row(5)], null)));
            Drain();
            Assert.Equal(2, grid.Items.Count);
            Assert.DoesNotContain(grid.Items.OfType<ViewIncident>(), row => row.Id == Row(5).Id);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Older_service_keeps_the_recent_list_and_does_not_probe_paging() => StaTest.RunAsync(() =>
    {
        var now = DateTimeOffset.UtcNow;
        var row = new ViewIncident(Guid.NewGuid().ToString("N"), "Active", "memory_pressure", 80, "Warning", now, 1);
        var view = new DashboardSnapshot(now, "Shadow", true, false, null, null, null, [], [], [], null, [row], [], [], []);
        var requests = new List<string>();
        var controller = new DashboardController((payload, _) =>
        {
            var command = ViewProtocol.Parse(payload).Command;
            requests.Add(command);
            return ValueTask.FromResult(command == "overview"
                ? AdminProtocol.Reply(true, "overview", view)
                : AdminProtocol.Reply(true, "history", Array.Empty<ViewMetric>()));
        });
        var loc = TestLocalization.CzechStandard();
        var window = new MainWindow(controller, loc);
        try
        {
            window.Show();
            Drain();
            var tabs = Assert.IsAssignableFrom<TabControl>(window.FindName("DashboardTabs"));
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Tag, DesktopRoute.Events));
            Drain();
            var grid = Assert.IsAssignableFrom<DataGrid>(window.FindName("IncidentsGrid"));
            Assert.Equal(row, Assert.Single(grid.Items.OfType<ViewIncident>()));
            Assert.DoesNotContain("incident-page", requests);
            Assert.Equal(loc.Text("IncidentHistoryLegacy"),
                Assert.IsAssignableFrom<TextBlock>(window.FindName("IncidentsEmptyText")).Text);
            Assert.Equal(Visibility.Collapsed, Assert.IsAssignableFrom<Button>(window.FindName("OlderIncidentsButton")).Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Reconnect_reloads_selected_detail_at_the_same_revision_and_discards_previous_reply() =>
        StaTest.RunAsync(() =>
        {
            var now = DateTimeOffset.UtcNow;
            var loc = TestLocalization.CzechStandard();
            var row = new ViewIncident(Guid.NewGuid().ToString("N"), "Active", "memory_pressure", 80, "Warning", now, 1)
                { CreatedAtUtc = now, UpdatedAtUtc = now };
            var view = new DashboardSnapshot(now, "Shadow", true, false, null, null, null, [], [], [],
                null, [row], [], [], []) { SupportsIncidentHistory = true };
            var connected = true;
            var detailReads = 0;
            var previousDetail = new TaskCompletionSource<byte[]>();
            byte[] Detail(string source) => AdminProtocol.Reply(true, "incident",
                new ViewIncidentDetail(row, "probable_cause_estimate", [new ViewEvidence(source, 1, now)], false));
            var controller = new DashboardController((payload, _) =>
            {
                var command = ViewProtocol.Parse(payload).Command;
                if (!connected)
                    return ValueTask.FromResult(AdminProtocol.Reply(false, "view_request_rejected", null));
                switch (command)
                {
                    case "overview": return ValueTask.FromResult(AdminProtocol.Reply(true, command, view));
                    case "history": return ValueTask.FromResult(AdminProtocol.Reply(true, command, Array.Empty<ViewMetric>()));
                    case "incident-page": return ValueTask.FromResult(AdminProtocol.Reply(true, command, new ViewIncidentPage([row], null)));
                    case "incident":
                        detailReads++;
                        return detailReads == 1 ? new ValueTask<byte[]>(previousDetail.Task)
                            : ValueTask.FromResult(Detail("restored-source"));
                    default: throw new InvalidOperationException(command);
                }
            });

            var window = new MainWindow(controller, loc);
            try
            {
                window.Show();
                Drain();
                var tabs = Assert.IsAssignableFrom<TabControl>(window.FindName("DashboardTabs"));
                tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Tag, DesktopRoute.Events));
                Drain();
                var grid = Assert.IsAssignableFrom<DataGrid>(window.FindName("IncidentsGrid"));
                var refresh = Assert.IsAssignableFrom<Button>(window.FindName("RefreshButton"));
                var details = Assert.IsAssignableFrom<TextBox>(window.FindName("DetailText"));
                grid.SelectedIndex = 0;
                Assert.Equal(1, detailReads);

                connected = false;
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Drain();
                Assert.Equal(row, Assert.IsType<ViewIncident>(grid.SelectedItem));
                Assert.Equal(loc.Text("DetailsAreUnavailableAPreviousStatusIsNotCurrentEvidence"), details.Text);

                connected = true;
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Drain();
                Assert.Equal(row, Assert.IsType<ViewIncident>(grid.SelectedItem));
                Assert.Equal(2, detailReads);
                Assert.Contains("restored-source", details.Text);

                previousDetail.SetResult(Detail("previous-source"));
                Drain();
                Assert.Contains("restored-source", details.Text);
                Assert.DoesNotContain("previous-source", details.Text);
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData(true, "matching")]
    [InlineData(false, "matching")]
    [InlineData(false, "missing")]
    [InlineData(false, "mismatched")]
    public Task Crash_recommendation_opens_exact_saved_evidence_and_ignores_previous_request(
        bool targetOnPage, string response) => StaTest.RunAsync(() =>
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var target = new ViewIncident(Guid.NewGuid().ToString("N"), "Active", "windows_bugcheck", 80, "Warning", now, 1)
            { CreatedAtUtc = now, UpdatedAtUtc = now };
        var other = new ViewIncident(Guid.NewGuid().ToString("N"), "Active", "memory_pressure", 80, "Warning", now, 1)
            { CreatedAtUtc = now, UpdatedAtUtc = now };
        var listed = targetOnPage ? target : other;
        var view = new DashboardSnapshot(DateTimeOffset.UtcNow, "Shadow", true, false, null, null, null, [], [], [], null,
            [target, other], [], [], []) { SupportsIncidentHistory = true };
        var loc = TestLocalization.CzechStandard();
        var pending = new TaskCompletionSource<byte[]>();
        var requestedIds = new List<string>();
        var investigations = 0;

        byte[] Detail(ViewIncident row, string source) => AdminProtocol.Reply(true, "incident",
            new ViewIncidentDetail(row, "stop_code_classification_only", [new ViewEvidence(source, 1, now)], false));

        var controller = new DashboardController((payload, _) =>
        {
            var request = ViewProtocol.Parse(payload);
            switch (request.Command)
            {
                case "overview": return ValueTask.FromResult(AdminProtocol.Reply(true, "overview", view));
                case "history": return ValueTask.FromResult(AdminProtocol.Reply(true, "history", Array.Empty<ViewMetric>()));
                case "incident-page":
                    return ValueTask.FromResult(AdminProtocol.Reply(true, "incident-page", new ViewIncidentPage([listed], null)));
                case "investigate":
                    investigations++;
                    return ValueTask.FromResult(AdminProtocol.Reply(false, "view_request_rejected", null));
                case "incident":
                    requestedIds.Add(request.Body.GetProperty("incidentId").GetString()!);
                    return requestedIds.Count == 1 ? new ValueTask<byte[]>(pending.Task) : ValueTask.FromResult(response switch
                    {
                        "matching" => Detail(target, "target-evidence"),
                        "mismatched" => Detail(other, "wrong-evidence"),
                        _ => AdminProtocol.Reply(false, "view_request_rejected", null)
                    });
                default: throw new InvalidOperationException(request.Command);
            }
        });

        var window = new MainWindow(controller, loc);
        try
        {
            window.Show();
            Drain();
            var tabs = Assert.IsAssignableFrom<TabControl>(window.FindName("DashboardTabs"));
            TabItem Tab(DesktopRoute route) => tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Tag, route));
            tabs.SelectedItem = Tab(DesktopRoute.Events);
            Drain();
            var grid = Assert.IsAssignableFrom<DataGrid>(window.FindName("IncidentsGrid"));
            grid.SelectedIndex = 0;
            Assert.Equal(new[] { listed.Id }, requestedIds);
            tabs.SelectedItem = Tab(DesktopRoute.Recommendations);
            Drain();

            var panel = Assert.IsAssignableFrom<StackPanel>(window.FindName("RecommendationsPanel"));
            var card = Assert.IsAssignableFrom<StackPanel>(Assert.Single(panel.Children.OfType<Border>()).Child);
            Assert.Single(card.Children.OfType<Button>()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain();

            Assert.Same(Tab(DesktopRoute.Events), tabs.SelectedItem);
            Assert.Equal(new[] { listed.Id, target.Id }, requestedIds);
            Assert.Equal(0, investigations);
            if (targetOnPage) Assert.Equal(target.Id, Assert.IsType<ViewIncident>(grid.SelectedItem).Id);
            else Assert.Null(grid.SelectedItem);

            var detail = Assert.IsAssignableFrom<TextBox>(window.FindName("DetailText"));
            if (response == "matching") Assert.Contains("target-evidence", detail.Text);
            else Assert.Equal(loc.Text("DetailsAreUnavailable"), detail.Text);
            var latestText = detail.Text;
            pending.SetResult(Detail(listed, "late-evidence"));
            Drain();
            Assert.Equal(latestText, detail.Text);
        }
        finally { window.Close(); }
    });

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
