using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    public async Task Standard_mode_can_preview_fixed_temp_without_showing_developer_details()
    {
        using var fixture = new Fixture();
        fixture.File("eligible.tmp", "old payload");
        fixture.File("recent.tmp", "recent payload", 1);
        await RunSta(() =>
        {
            var window = new MaintenanceWindow(Controller(Snapshot("Confirmation")), fixture.Service("Confirmation"),
                TestLocalization.Czech(), developerMode: () => false);
            try
            {
                window.Show();
                Click(window, "MaintenancePreviewButton");
                PumpUntil(() => window.CurrentPreview is { Items.Count: 1 });
                var preview = window.CurrentPreview!;
                var previewInfo = ((TextBlock)window.FindName("MaintenancePreviewInfo")).Text;
                var scopeInfo = ((TextBlock)window.FindName("MaintenanceScopeInfo")).Text;
                var previewGrid = (DataGrid)window.FindName("MaintenancePreviewGrid");
                var skippedGrid = (DataGrid)window.FindName("MaintenanceSkippedGrid");
                var folderButton = (Button)window.FindName("MaintenanceFolderButton");
                var exactLocationButton = (Button)window.FindName("MaintenanceExactLocationButton");

                Assert.DoesNotContain(preview.Id, previewInfo);
                Assert.DoesNotContain(fixture.Source, scopeInfo);
                Assert.DoesNotContain(fixture.Quarantine, scopeInfo);
                Assert.Contains("neuvolní", ((TextBlock)window.FindName("MaintenanceNoSpaceWarning")).Text);
                Assert.Equal(Visibility.Collapsed, folderButton.Visibility);
                Assert.Equal(Visibility.Visible, exactLocationButton.Visibility);
                Assert.Equal("Zobrazit přesné umístění", exactLocationButton.Content);
                Assert.Equal(Visibility.Collapsed, ((TextBlock)window.FindName("MaintenanceExactLocationDetails")).Visibility);
                Assert.Equal(Visibility.Collapsed, previewGrid.Columns[3].Visibility);
                Assert.Single(skippedGrid.Items);
                var reasonBinding = Assert.IsType<Binding>(((DataGridTextColumn)skippedGrid.Columns[1]).Binding);
                var reason = reasonBinding.Converter!.Convert(MaintenanceSkipReasonCodes.TooRecent, typeof(string), null!, TestLocalization.Czech().Culture);
                Assert.DoesNotContain(MaintenanceSkipReasonCodes.TooRecent, Assert.IsType<string>(reason));
                Assert.Null(window.FindName("MaintenanceDeleteButton"));
                Assert.Null(window.FindName("MaintenanceApproval"));
                Assert.Null(window.FindName("MaintenanceRestoreApproval"));
                Click(window, "MaintenanceExactLocationButton");
                var exactDetails = ((TextBlock)window.FindName("MaintenanceExactLocationDetails")).Text;
                Assert.Contains(fixture.Source, exactDetails);
                Assert.Contains(fixture.Quarantine, exactDetails);
                Click(window, "MaintenanceExactLocationButton");
                Assert.Equal(Visibility.Collapsed, ((TextBlock)window.FindName("MaintenanceExactLocationDetails")).Visibility);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task One_argument_constructor_defaults_to_standard_maintenance_scope()
    {
        await RunSta(() =>
        {
            var window = new MaintenanceWindow(Controller(Snapshot("Confirmation")));
            try
            {
                Assert.Equal(Visibility.Collapsed, ((Button)window.FindName("MaintenanceFolderButton")).Visibility);
                Assert.Equal(Visibility.Collapsed, ((DataGrid)window.FindName("MaintenancePreviewGrid")).Columns[3].Visibility);
                Assert.Equal(Visibility.Collapsed, ((DataGrid)window.FindName("MaintenanceEntriesGrid")).Columns[3].Visibility);
                Assert.Null(window.FindName("MaintenanceDeleteButton"));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Developer_folder_picker_remains_available_with_drive_readback_enabled()
    {
        using var fixture = new Fixture();
        await RunSta(() =>
        {
            var drive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))!.TrimEnd('\\');
            var window = new MaintenanceWindow(Controller(Snapshot("Confirmation")), fixture.Service("Confirmation"),
                TestLocalization.Czech(), targetDrive: drive, developerMode: () => true);
            try
            {
                var folderButton = (Button)window.FindName("MaintenanceFolderButton");
                Assert.Equal(Visibility.Visible, folderButton.Visibility);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Incomplete_expired_and_empty_previews_cannot_enable_quarantine()
    {
        var now = DateTimeOffset.UtcNow;
        var item = new MaintenanceItem("item", "file.tmp", 1, now, new string('0', 64));
        var valid = new MaintenancePreview("preview", now.AddMinutes(1), [item], 0, false);
        var incomplete = valid with { Truncated = true };
        var expired = valid with { ExpiresAtUtc = now.AddSeconds(-1) };
        var empty = valid with { Items = Array.Empty<MaintenanceItem>() };

        Assert.True(MaintenanceWindow.CanQuarantinePreview(valid, now));
        Assert.False(MaintenanceWindow.CanQuarantinePreview(incomplete, now));
        Assert.False(MaintenanceWindow.CanQuarantinePreview(expired, now));
        Assert.False(MaintenanceWindow.CanQuarantinePreview(empty, now));
    }

    [Fact]
    public void Maintenance_skip_and_action_reasons_are_localized_for_all_supported_languages()
    {
        var cultures = new[] { System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.CultureInfo.GetCultureInfo("cs-CZ"), System.Globalization.CultureInfo.GetCultureInfo("de") };
        var technicalKeys = TechnicalText.Sources.Distinct().ToArray();
        var maintenanceResultCodes = new[]
        {
            "maintenance_preview_incomplete", "maintenance_preview_expired", "quarantine_empty", "maintenance_not_approved",
            "quarantine_cancelled", "maintenance_state_unavailable", "maintenance_root_unavailable", "maintenance_busy",
            "maintenance_volume_mismatch", "maintenance_journal_integrity", "maintenance_quota_reached", "quarantine_cancelled_partial",
            "quarantine_outcome_unknown", "quarantine_partial_failure", "quarantine_failed", "quarantine_partial_blocked_recovery_required",
            "quarantine_partial_blocked", "quarantine_blocked", "quarantine_recovery_required", "quarantine_complete",
            "quarantine_partial", "quarantine_no_files_moved", "maintenance_payload_changed", "restore_conflict",
            "maintenance_journal_write_failed", "restore_recovery_required", "restore_complete", "restore_failed",
            "restore_outcome_unknown", "restore_cancelled", "maintenance_confirmation_required", "maintenance_entry_unavailable",
            "maintenance_journal_limit", "maintenance_scope", "quarantine_busy", "quarantine_limit_reached",
            "quarantine_lock_unavailable", "quarantine_root_unavailable"
        };
        var specificResultMessages = new (string Code, string ResourceKey)[]
        {
            ("maintenance_preview_incomplete", "PreviewIncomplete"),
            ("maintenance_state_unavailable", "MaintenanceResultStateUnavailable"),
            ("quarantine_blocked", "MaintenanceResultBlocked"),
            ("quarantine_cancelled", "MaintenanceResultCancelled"),
            ("quarantine_cancelled_partial", "MaintenanceResultCancelledPartial"),
            ("quarantine_failed", "MaintenanceResultFailed"),
            ("quarantine_outcome_unknown", "MaintenanceResultOutcomeUnknown"),
            ("quarantine_partial_blocked", "MaintenanceResultPartialBlocked"),
            ("quarantine_partial_blocked_recovery_required", "MaintenanceResultPartialBlockedRecovery"),
            ("quarantine_partial_failure", "MaintenanceResultPartialFailure"),
            ("restore_cancelled", "MaintenanceResultCancelled"),
            ("restore_outcome_unknown", "MaintenanceResultOutcomeUnknown")
        };
        foreach (var culture in cultures)
        {
            var resources = DesktopLocalization.Catalog.GetResourceSet(culture, true, false)!;
            foreach (var key in technicalKeys) Assert.NotNull(resources.GetObject(key, false));
            foreach (var key in new[] { "CancelMaintenanceAction", "Unknown", "ShowExactLocation", "HideExactLocation", "MaintenancePreviewLocationDetails", "RestoreToOriginalLocation" })
                Assert.NotNull(resources.GetObject(key, false));

            var actionStates = new (string State, string ResourceKey)[]
            {
                (MaintenanceActionStates.Moved, "MaintenanceActionMoved"),
                (MaintenanceActionStates.Restored, "Restored"),
                (MaintenanceActionStates.Skipped, "MaintenanceActionSkipped"),
                (MaintenanceActionStates.Blocked, "MaintenanceActionBlocked"),
                (MaintenanceActionStates.Failed, "ActionFailed"),
                (MaintenanceActionStates.Cancelled, "MaintenanceActionCancelled"),
                (MaintenanceActionStates.Unknown, "UnknownStatus")
            };
            foreach (var (state, resourceKey) in actionStates)
            {
                var expected = DesktopLocalization.Text(culture, resourceKey);
                Assert.Equal(expected, TechnicalText.Format(state, culture));
                Assert.Equal(expected, TechnicalText.FormatMaintenanceActionState(state, culture));
                Assert.Contains(state, TechnicalText.Format(state, culture, true));
                Assert.Contains(state, TechnicalText.FormatMaintenanceActionState(state, culture, true));
            }

            foreach (var key in new[]
            {
                "MaintenanceResultStateUnavailable", "MaintenanceResultBlocked", "MaintenanceResultCancelled",
                "MaintenanceResultCancelledPartial", "MaintenanceResultFailed", "MaintenanceResultOutcomeUnknown",
                "MaintenanceResultPartialBlocked", "MaintenanceResultPartialBlockedRecovery", "MaintenanceResultPartialFailure"
            }) Assert.NotNull(resources.GetObject(key, false));

            foreach (var code in maintenanceResultCodes)
                Assert.NotEqual(DesktopLocalization.Text(culture, "UnclassifiedDiagnostic"), TechnicalText.Format(code, culture));
            foreach (var (code, resourceKey) in specificResultMessages)
                Assert.Equal(DesktopLocalization.Text(culture, resourceKey), TechnicalText.Format(code, culture));
        }
        Assert.DoesNotContain("source_hash_changed", TechnicalText.FormatMaintenanceReason("source_hash_changed", TestLocalization.Czech().Culture));
        Assert.Contains("source_hash_changed", TechnicalText.FormatMaintenanceReason("source_hash_changed", TestLocalization.Czech().Culture, true));
        Assert.DoesNotContain("future_reason", TechnicalText.FormatMaintenanceReason("future_reason", TestLocalization.Czech().Culture));
    }

    [Fact]
    public async Task Developer_selected_scope_is_discarded_when_window_downgrades_to_standard()
    {
        using var fixture = new Fixture();
        var original = fixture.File("synthetic-mode-change.tmp", "synthetic payload");
        await RunSta(() =>
        {
            var developer = true;
            var window = new MaintenanceWindow(Controller(Snapshot("Confirmation")), fixture.SelectedService("Confirmation"),
                TestLocalization.Czech(), developerMode: () => developer);
            try
            {
                window.Show();
                Click(window, "MaintenancePreviewButton");
                PumpUntil(() => window.CurrentPreview is { Items.Count: 1 });
                developer = false;
                Click(window, "MaintenanceQuarantineButton");
                Assert.Null(window.CurrentPreview);
                Assert.True(File.Exists(original));
                Assert.Equal(Visibility.Collapsed, ((Button)window.FindName("MaintenanceFolderButton")).Visibility);
                Assert.Empty(((DataGrid)window.FindName("MaintenanceEntriesGrid")).Items);
                Assert.Contains("Developer", ((TextBlock)window.FindName("MaintenanceStatus")).Text);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Language_switch_keeps_the_preview_and_localizes_all_maintenance_surfaces()
    {
        using var fixture = new Fixture();
        fixture.File("synthetic-language.tmp", "synthetic payload");
        await RunSta(() =>
        {
            var language = TestLocalization.Czech();
            var window = new MaintenanceWindow(Controller(Snapshot("Shadow")), fixture.Service("Shadow"), language, developerMode: () => true);
            try
            {
                window.Show();
                Click(window, "MaintenancePreviewButton");
                PumpUntil(() => window.CurrentPreview is { Items.Count: 1 });
                var preview = window.CurrentPreview!;
                Assert.Contains(preview.Id, ((TextBlock)window.FindName("MaintenancePreviewInfo")).Text);
                language.SelectLanguage("de");
                LocalizationSwitchTests.Drain();
                Assert.Same(preview, window.CurrentPreview);
                Assert.Contains(preview.Id, ((TextBlock)window.FindName("MaintenancePreviewInfo")).Text);
                Assert.Equal(language.Text("HostAgentSafeMaintenance"), window.Title);
                Assert.Equal(language.Text("CreateSafePreview"), ((Button)window.FindName("MaintenancePreviewButton")).Content);
                Assert.Equal("Liste aktualisieren", ((Button)window.FindName("MaintenanceListButton")).Content);
                var catalog = DesktopLocalization.Catalog.GetResourceSet(System.Globalization.CultureInfo.GetCultureInfo("de"), true, false)!;
                var translations = catalog.Cast<System.Collections.DictionaryEntry>().Select(x => (string)x.Value!).ToHashSet(StringComparer.Ordinal);
                foreach (var name in new[] { "MaintenancePreviewButton", "MaintenanceListButton", "MaintenanceCancelButton" })
                    Assert.Contains(Assert.IsType<string>(((Button)window.FindName(name)).Content), translations);
                Assert.Equal(language.Text("Name"), ((DataGrid)window.FindName("MaintenancePreviewGrid")).Columns[0].Header);
                Assert.Equal(language.Text("MaintenanceReason_file_locked"), DesktopLocalization.Text(System.Globalization.CultureInfo.GetCultureInfo("de"), "MaintenanceReason_file_locked"));
                LocalizationSwitchTests.Capture(window, "de-maintenance");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Confirmation_mode_moves_and_restores_one_selected_file_with_per_item_receipts()
    {
        using var fixture = new Fixture();
        var original = fixture.File("synthetic-old.tmp", "synthetic payload");
        await RunSta(() =>
        {
            var window = new MaintenanceWindow(Controller(Snapshot("Confirmation")), fixture.Service("Confirmation"), TestLocalization.Czech(), developerMode: () => true);
            window.Show();
            try
            {
                Click(window, "MaintenancePreviewButton");
                PumpUntil(() => window.CurrentPreview is { Items.Count: 1 });
                Click(window, "MaintenanceQuarantineButton");
                PumpUntil(() => !File.Exists(original));
                var resultGrid = (DataGrid)window.FindName("MaintenanceResultGrid");
                Assert.Single(resultGrid.Items);
                Assert.Equal(MaintenanceActionStates.Moved, ((MaintenanceActionItem)resultGrid.Items[0]).State);

                Click(window, "MaintenanceListButton");
                var grid = (DataGrid)window.FindName("MaintenanceEntriesGrid");
                PumpUntil(() => grid.Items.Count == 1);
                grid.SelectedIndex = 0;
                var restoreTarget = ((TextBlock)window.FindName("MaintenanceRestoreTarget")).Text;
                Assert.Contains(original, restoreTarget);
                Assert.DoesNotContain(((QuarantineItem)grid.SelectedItem).Id, restoreTarget);
                Click(window, "MaintenanceRestoreButton");
                PumpUntil(() => File.Exists(original));
                Assert.Equal("synthetic payload", File.ReadAllText(original));
                Assert.Equal(MaintenanceActionStates.Restored, ((MaintenanceActionItem)resultGrid.Items[0]).State);
                Assert.Contains("obnoveno 1", ((TextBlock)window.FindName("MaintenanceStatus")).Text, StringComparison.OrdinalIgnoreCase);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Shadow_dashboard_keeps_preview_readonly_and_refuses_mutation_with_item_receipt()
    {
        using var fixture = new Fixture();
        var original = fixture.File("synthetic-shadow.tmp", "synthetic payload");
        await RunSta(() =>
        {
            var window = new MaintenanceWindow(Controller(Snapshot("Shadow")), fixture.Service("Shadow"), TestLocalization.Czech(), developerMode: () => true);
            window.Show();
            try
            {
                Click(window, "MaintenancePreviewButton");
                PumpUntil(() => window.CurrentPreview is { Items.Count: 1 });
                Click(window, "MaintenanceQuarantineButton");
                var resultGrid = (DataGrid)window.FindName("MaintenanceResultGrid");
                PumpUntil(() => resultGrid.Items.Count == 1);
                Assert.True(File.Exists(original));
                Assert.Equal(MaintenanceActionStates.Blocked, ((MaintenanceActionItem)resultGrid.Items[0]).State);
                Assert.Contains("maintenance_not_approved", ((TextBlock)window.FindName("MaintenanceStatus")).Text);
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
    {
        var button = (Button)window.FindName(name);
        PumpUntil(() => button.IsEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => !((MaintenanceWindow)window).ActionInProgress);
    }

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
        public string Source => Path.Combine(_root, "source");
        public string Quarantine => Path.Combine(_root, "quarantine");
        public Fixture() => Directory.CreateDirectory(Source);
        public string File(string name, string contents, int ageDays = 9)
        {
            var path = Path.Combine(Source, name);
            System.IO.File.WriteAllText(path, contents);
            System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-ageDays));
            return path;
        }
        public UserMaintenance Service(string mode) => new(Source, Quarantine, TimeProvider.System,
            _ => ValueTask.FromResult<DashboardSnapshot?>(Snapshot(mode)));
        public UserMaintenance SelectedService(string mode) => new(Source, Quarantine, TimeProvider.System,
            _ => ValueTask.FromResult<DashboardSnapshot?>(Snapshot(mode)), selectedDirectory: true);
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
