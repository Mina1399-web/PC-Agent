using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.IO;
using HostAgent.Core.Dashboard;
using HostAgent.Desktop;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Maintenance;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class MaintenanceUiTests
{
    [Fact]
    public async Task Confirmation_screen_quarantines_and_restores_only_after_exact_ids_are_typed()
    {
        using var fixture = new Fixture();
        var original = fixture.File("synthetic-old.tmp", "synthetic payload");
        await RunSta(() =>
        {
            var view = Snapshot("Confirmation");
            var window = new MaintenanceWindow(Controller(view), fixture.Service("Confirmation"));
            window.Show();
            try
            {
                Click(window, "MaintenancePreviewButton");
                PumpUntil(() => window.CurrentPreview is { Items.Count: 1 });
                var preview = window.CurrentPreview!;
                var approval = (TextBox)window.FindName("MaintenanceApproval");
                Assert.Equal(string.Empty, approval.Text);
                approval.Text = preview.Id;
                Click(window, "MaintenanceQuarantineButton");
                PumpUntil(() => !File.Exists(original));
                Assert.Contains("quarantine_complete", ((TextBlock)window.FindName("MaintenanceStatus")).Text);

                Click(window, "MaintenanceListButton");
                var grid = (DataGrid)window.FindName("MaintenanceEntriesGrid");
                PumpUntil(() => grid.Items.Count == 1);
                grid.SelectedIndex = 0;
                var entry = (QuarantineItem)grid.SelectedItem;
                var restoreApproval = (TextBox)window.FindName("MaintenanceRestoreApproval");
                Assert.Equal(string.Empty, restoreApproval.Text);
                Click(window, "MaintenanceRestoreButton");
                PumpUntil(() => ((TextBlock)window.FindName("MaintenanceStatus")).Text.Contains("maintenance_confirmation_required"));
                Assert.False(File.Exists(original));
                restoreApproval.Text = entry.Id;
                Click(window, "MaintenanceRestoreButton");
                PumpUntil(() => File.Exists(original));
                Assert.Equal("synthetic payload", File.ReadAllText(original));
                Assert.Contains("restore_complete", ((TextBlock)window.FindName("MaintenanceStatus")).Text);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Shadow_dashboard_keeps_preview_readonly_and_refuses_a_mutation()
    {
        using var fixture = new Fixture();
        var original = fixture.File("synthetic-old.tmp", "synthetic payload");
        await RunSta(() =>
        {
            var window = new MaintenanceWindow(Controller(Snapshot("Shadow")), fixture.Service("Shadow"));
            window.Show();
            try
            {
                Click(window, "MaintenancePreviewButton");
                PumpUntil(() => window.CurrentPreview is { Items.Count: 1 });
                var preview = window.CurrentPreview!;
                ((TextBox)window.FindName("MaintenanceApproval")).Text = preview.Id;
                Click(window, "MaintenanceQuarantineButton");
                PumpUntil(() => ((TextBlock)window.FindName("MaintenanceStatus")).Text.Contains("maintenance_not_approved"));
                Assert.True(File.Exists(original));
            }
            finally { window.Close(); }
        });
    }

    private static DashboardController Controller(DashboardSnapshot snapshot) => new((payload, _) =>
    {
        var request = ViewProtocol.Parse(payload);
        return ValueTask.FromResult(request.Command == "overview"
            ? AdminProtocol.Reply(true, "overview", snapshot)
            : AdminProtocol.Reply(false, "view_unavailable"));
    });

    private static DashboardSnapshot Snapshot(string mode) => new(DateTimeOffset.UtcNow, mode, false, false,
        null, null, null, [], [], [], null, [], [], [], []);

    private static void Click(Window window, string name)
        => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Task RunSta(Action action) => StaTest.RunAsync(action);

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(20);
        }
        Assert.True(condition());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "HostAgent-Maintenance-UI-" + Guid.NewGuid().ToString("N"));
        private string Source => Path.Combine(_root, "source");
        private string Quarantine => Path.Combine(_root, "quarantine");
        public Fixture() => Directory.CreateDirectory(Source);
        public string File(string name, string contents)
        {
            var path = Path.Combine(Source, name);
            System.IO.File.WriteAllText(path, contents);
            System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-9));
            return path;
        }
        public UserMaintenance Service(string mode) => new(Source, Quarantine, TimeProvider.System,
            _ => ValueTask.FromResult<DashboardSnapshot?>(Snapshot(mode)));
        public void Dispose()
        {
            if (!Directory.Exists(_root)) return;
            foreach (var child in Directory.GetFileSystemEntries(_root))
            {
                if (Directory.Exists(child)) Directory.Delete(child, true);
                else System.IO.File.Delete(child);
            }
            Directory.Delete(_root, false);
        }
    }
}
