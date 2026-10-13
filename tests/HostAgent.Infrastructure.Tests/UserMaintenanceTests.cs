using HostAgent.Core.Dashboard;
using HostAgent.Infrastructure.Maintenance;
using Xunit;
using System.Diagnostics;
using System.Security.AccessControl;

namespace HostAgent.Infrastructure.Tests;

public sealed class UserMaintenanceTests
{
    // A preview that scans recursively or accepts recent files proposes unrelated active data.
    [Fact]
    public async Task Preview_selects_only_old_top_level_files_and_does_not_move_them()
    {
        using var fixture = new Fixture();
        var old = fixture.File("old.tmp", "old", 9);
        fixture.File("recent.tmp", "recent", 1);
        Directory.CreateDirectory(Path.Combine(fixture.Source, "nested"));
        fixture.File("nested/other.tmp", "nested", 9);
        var preview = await fixture.Service.PreviewAsync();
        var item = Assert.Single(preview.Items);
        Assert.Equal("old.tmp", item.Name);
        Assert.Equal(3, item.Bytes);
        Assert.True(File.Exists(old));
        Assert.False(Directory.Exists(fixture.Quarantine));
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("large")]
    [InlineData("hardlink")]
    public async Task Preview_retains_eligible_file_and_excludes_unsafe_candidates(string kind)
    {
        using var fixture = new Fixture();
        fixture.File("eligible.tmp", "old", 9);
        var candidate = fixture.File("excluded.tmp", "excluded", 9);
        FileStream? held = null;
        try
        {
            if (kind == "locked") held = new FileStream(candidate, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (kind == "large")
            {
                using (var stream = new FileStream(candidate, FileMode.Open, FileAccess.Write)) stream.SetLength(17 * 1024 * 1024);
                System.IO.File.SetLastWriteTimeUtc(candidate, DateTime.UtcNow.AddDays(-9));
            }
            if (kind == "hardlink") await Link("/H", Path.Combine(fixture.Root, "outside-link.tmp"), candidate);
            var preview = await fixture.Service.PreviewAsync();
            Assert.Equal("eligible.tmp", Assert.Single(preview.Items).Name);
            Assert.True(System.IO.File.Exists(candidate));
            Assert.True(preview.Skipped >= 1);
        }
        finally { held?.Dispose(); }
    }

    [Fact]
    public async Task A_junction_source_is_rejected_instead_of_traversed()
    {
        using var fixture = new Fixture();
        var original = fixture.File("old.tmp", "old", 9);
        var alias = Path.Combine(fixture.Root, "alias");
        await Link("/J", alias, fixture.Source);
        try
        {
            var service = new UserMaintenance(alias, fixture.Quarantine, TimeProvider.System, _ => ValueTask.FromResult<DashboardSnapshot?>(null));
            var preview = await service.PreviewAsync();
            Assert.Equal("maintenance_root_unavailable", preview.Code);
            Assert.Empty(preview.Items);
            Assert.True(System.IO.File.Exists(original));
        }
        finally { Directory.Delete(alias, false); }
    }

    [Fact]
    public async Task Exact_confirmation_quarantines_and_restart_restore_returns_the_original_file()
    {
        using var fixture = new Fixture("Confirmation");
        var original = fixture.File("old.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();

        var moved = await fixture.Service.QuarantineAsync(preview.Id, preview.Id);

        Assert.Equal("quarantine_complete", moved.Code);
        Assert.Equal(1, moved.Moved);
        Assert.False(System.IO.File.Exists(original));
        var restarted = fixture.NewService("Confirmation");
        var entry = Assert.Single(await restarted.ListAsync());
        Assert.True(System.IO.File.Exists(Path.Combine(fixture.Quarantine, entry.Id + ".payload")), string.Join("|", Directory.GetFiles(fixture.Quarantine).Select(Path.GetFileName)));
        Assert.Equal("Quarantined", entry.State);
        var restored = await restarted.RestoreAsync(entry.Id, entry.Id);
        Assert.Equal("restore_complete", restored.Code);
        Assert.Equal("synthetic", await System.IO.File.ReadAllTextAsync(original));
    }

    [Fact]
    public async Task Shadow_mode_denies_quarantine_without_consuming_or_moving_the_file()
    {
        using var fixture = new Fixture("Shadow");
        var original = fixture.File("old.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();

        var moved = await fixture.Service.QuarantineAsync(preview.Id, preview.Id);

        Assert.Equal("maintenance_not_approved", moved.Code);
        Assert.Equal(0, moved.Moved);
        Assert.True(System.IO.File.Exists(original));
    }

    [Fact]
    public async Task Restore_never_replaces_a_new_file_created_at_the_original_path()
    {
        using var fixture = new Fixture("Confirmation");
        var original = fixture.File("old.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();
        Assert.Equal("quarantine_complete", (await fixture.Service.QuarantineAsync(preview.Id, preview.Id)).Code);
        await System.IO.File.WriteAllTextAsync(original, "user replacement");

        var entry = Assert.Single(await fixture.Service.ListAsync());
        var restored = await fixture.Service.RestoreAsync(entry.Id, entry.Id);

        Assert.Equal("restore_conflict", restored.Code);
        Assert.Equal("user replacement", await System.IO.File.ReadAllTextAsync(original));
    }

    [Fact]
    public async Task Wrong_confirmation_does_not_move_or_consume_the_exact_approval()
    {
        using var fixture = new Fixture("Confirmation");
        var original = fixture.File("old.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();
        Assert.Equal("maintenance_confirmation_required", (await fixture.Service.QuarantineAsync(preview.Id, "another operation")).Code);
        Assert.True(System.IO.File.Exists(original));
        Assert.Equal("quarantine_complete", (await fixture.Service.QuarantineAsync(preview.Id, preview.Id)).Code);
    }

    [Theory]
    [InlineData("Confirmation", true, false)]
    [InlineData("Confirmation", false, true)]
    [InlineData("Shadow", false, false)]
    public async Task Readonly_killswitch_and_shadow_modes_deny_mutation(string mode, bool readOnly, bool killSwitch)
    {
        using var fixture = new Fixture(mode, readOnly, killSwitch);
        var original = fixture.File("old.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();
        var moved = await fixture.Service.QuarantineAsync(preview.Id, preview.Id);
        Assert.Equal("maintenance_not_approved", moved.Code);
        Assert.True(System.IO.File.Exists(original));
    }

    [Fact]
    public async Task Stale_service_snapshot_denies_mutation()
    {
        using var fixture = new Fixture("Confirmation", generatedAt: DateTimeOffset.UtcNow.AddMinutes(-4));
        var original = fixture.File("old.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();
        Assert.Equal("maintenance_not_approved", (await fixture.Service.QuarantineAsync(preview.Id, preview.Id)).Code);
        Assert.True(System.IO.File.Exists(original));
    }

    [Fact]
    public async Task Restore_refuses_payload_whose_contents_changed_after_quarantine()
    {
        using var fixture = new Fixture("Confirmation");
        var original = fixture.File("old.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();
        Assert.Equal("quarantine_complete", (await fixture.Service.QuarantineAsync(preview.Id, preview.Id)).Code);
        var entry = Assert.Single(await fixture.Service.ListAsync());
        await System.IO.File.WriteAllTextAsync(Path.Combine(fixture.Quarantine, entry.Id + ".payload"), "altered");

        var restored = await fixture.Service.RestoreAsync(entry.Id, entry.Id);

        Assert.Equal("maintenance_payload_changed", restored.Code);
        Assert.False(System.IO.File.Exists(original));
    }

    [Fact]
    public async Task Interrupted_rename_journal_remains_restorable_after_restart()
    {
        using var fixture = new Fixture("Confirmation");
        var original = fixture.File("old.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();
        Assert.Equal("quarantine_complete", (await fixture.Service.QuarantineAsync(preview.Id, preview.Id)).Code);
        var entry = Assert.Single(await fixture.Service.ListAsync());
        var journalPath = Path.Combine(fixture.Quarantine, entry.Id + ".json");
        var journal = await System.IO.File.ReadAllTextAsync(journalPath);
        await System.IO.File.WriteAllTextAsync(journalPath, journal.Replace("\"State\":\"Quarantined\"", "\"State\":\"Intent\"", StringComparison.Ordinal));

        var restarted = fixture.NewService("Confirmation");
        var recovered = Assert.Single(await restarted.ListAsync());
        Assert.Equal("Quarantined", recovered.State);
        Assert.Equal("restore_complete", (await restarted.RestoreAsync(recovered.Id, recovered.Id)).Code);
        Assert.Equal("synthetic", await System.IO.File.ReadAllTextAsync(original));
    }

    [Theory]
    [InlineData(",\"Injected\":\"unexpected\"")]
    [InlineData(",\"State\":\"Restored\"")]
    public async Task Malformed_or_duplicate_journal_fields_are_rejected(string extra)
    {
        using var fixture = new Fixture("Confirmation");
        fixture.File("old.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();
        Assert.Equal("quarantine_complete", (await fixture.Service.QuarantineAsync(preview.Id, preview.Id)).Code);
        var entry = Assert.Single(await fixture.Service.ListAsync());
        var path = Path.Combine(fixture.Quarantine, entry.Id + ".json");
        var journal = await System.IO.File.ReadAllTextAsync(path);
        await System.IO.File.WriteAllTextAsync(path, journal[..^1] + extra + "}");

        var listed = Assert.Single(await fixture.Service.ListAsync());
        Assert.Equal("NeedsReview", listed.State);
        Assert.Equal("maintenance_journal_integrity", (await fixture.Service.RestoreAsync(entry.Id, entry.Id)).Code);
        Assert.True(System.IO.File.Exists(Path.Combine(fixture.Quarantine, entry.Id + ".payload")));
    }

    [Fact]
    public async Task Journal_with_null_required_field_is_ignored_without_crashing_listing()
    {
        using var fixture = new Fixture("Confirmation");
        fixture.File("old.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();
        Assert.Equal("quarantine_complete", (await fixture.Service.QuarantineAsync(preview.Id, preview.Id)).Code);
        var entry = Assert.Single(await fixture.Service.ListAsync());
        var path = Path.Combine(fixture.Quarantine, entry.Id + ".json");
        var journal = await System.IO.File.ReadAllTextAsync(path);
        using var document = System.Text.Json.JsonDocument.Parse(journal);
        var sha256 = document.RootElement.GetProperty("Sha256").GetString();
        var malformed = journal.Replace("\"Sha256\":\"" + sha256 + "\"", "\"Sha256\":null", StringComparison.Ordinal);
        Assert.NotEqual(journal, malformed);
        await System.IO.File.WriteAllTextAsync(path, malformed);

        var listed = await fixture.Service.ListAsync();

        Assert.Equal("NeedsReview", Assert.Single(listed).State);
    }

    [Fact]
    public async Task Corrupt_journal_inventory_blocks_new_quarantine_instead_of_escaping_quota()
    {
        using var fixture = new Fixture("Confirmation");
        var candidate = fixture.File("old.tmp", "synthetic", 9);
        Directory.CreateDirectory(fixture.Quarantine);
        for (var index = 0; index < 257; index++)
            await System.IO.File.WriteAllTextAsync(Path.Combine(fixture.Quarantine, Guid.NewGuid().ToString("N") + ".json"), "{}");
        var preview = await fixture.Service.PreviewAsync();

        var result = await fixture.Service.QuarantineAsync(preview.Id, preview.Id);

        Assert.Equal("maintenance_journal_integrity", result.Code);
        Assert.True(System.IO.File.Exists(candidate));
    }

    [Theory]
    [InlineData("orphan.payload")]
    [InlineData("abandoned.json.tmp")]
    public async Task Orphan_payload_or_journal_temporary_file_blocks_new_mutations(string debrisName)
    {
        using var fixture = new Fixture("Confirmation");
        var candidate = fixture.File("old.tmp", "synthetic", 9);
        Directory.CreateDirectory(fixture.Quarantine);
        await System.IO.File.WriteAllTextAsync(Path.Combine(fixture.Quarantine, debrisName), "synthetic orphan");
        var preview = await fixture.Service.PreviewAsync();

        var result = await fixture.Service.QuarantineAsync(preview.Id, preview.Id);

        Assert.Equal("maintenance_journal_integrity", result.Code);
        Assert.True(System.IO.File.Exists(candidate));
    }

    [Fact]
    public async Task Syntactically_malformed_journal_blocks_new_mutations_and_is_visible_for_review()
    {
        using var fixture = new Fixture("Confirmation");
        var original = fixture.File("old.tmp", "synthetic", 9);
        var firstPreview = await fixture.Service.PreviewAsync();
        Assert.Equal("quarantine_complete", (await fixture.Service.QuarantineAsync(firstPreview.Id, firstPreview.Id)).Code);
        var entry = Assert.Single(await fixture.Service.ListAsync());
        await System.IO.File.WriteAllTextAsync(Path.Combine(fixture.Quarantine, entry.Id + ".json"), "{");
        var candidate = fixture.File("next.tmp", "synthetic next", 9);
        var preview = await fixture.Service.PreviewAsync();

        var listed = await fixture.Service.ListAsync();
        var result = await fixture.Service.QuarantineAsync(preview.Id, preview.Id);

        Assert.Equal("NeedsReview", Assert.Single(listed).State);
        Assert.Equal("maintenance_journal_integrity", result.Code);
        Assert.False(System.IO.File.Exists(original));
        Assert.True(System.IO.File.Exists(Path.Combine(fixture.Quarantine, entry.Id + ".payload")));
        Assert.True(System.IO.File.Exists(candidate));
    }

    [Theory]
    [InlineData("Intent", "alter-payload")]
    [InlineData("RestoreIntent", "replace-original")]
    public async Task Interrupted_journal_state_requires_matching_file_identity_and_hash(string state, string mutation)
    {
        using var fixture = new Fixture("Confirmation");
        var original = fixture.File("old.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();
        Assert.Equal("quarantine_complete", (await fixture.Service.QuarantineAsync(preview.Id, preview.Id)).Code);
        var item = Assert.Single(await fixture.Service.ListAsync());
        var payload = Path.Combine(fixture.Quarantine, item.Id + ".payload");
        if (mutation == "alter-payload") await System.IO.File.WriteAllTextAsync(payload, "altered");
        else
        {
            System.IO.File.Delete(payload);
            await System.IO.File.WriteAllTextAsync(original, "unrelated replacement");
        }
        var journalPath = Path.Combine(fixture.Quarantine, item.Id + ".json");
        var journal = await System.IO.File.ReadAllTextAsync(journalPath);
        journal = journal.Replace("\"State\":\"Quarantined\"", "\"State\":\"" + state + "\"", StringComparison.Ordinal);
        await System.IO.File.WriteAllTextAsync(journalPath, journal);

        var listed = Assert.Single(await fixture.Service.ListAsync());

        Assert.Equal("NeedsReview", listed.State);
    }

    [Fact]
    public async Task Recovery_rejects_oversized_payload_metadata_before_hashing_contents()
    {
        using var fixture = new Fixture("Confirmation");
        fixture.File("old.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();
        Assert.Equal("quarantine_complete", (await fixture.Service.QuarantineAsync(preview.Id, preview.Id)).Code);
        var entry = Assert.Single(await fixture.Service.ListAsync());
        using (var payload = new FileStream(Path.Combine(fixture.Quarantine, entry.Id + ".payload"), FileMode.Open, FileAccess.Write, FileShare.None))
            payload.SetLength(16L * 1024 * 1024 + 1);
        var hashAttempts = 0;
        var service = fixture.NewService("Confirmation", beforeJournalHash: () => hashAttempts++);

        var listed = Assert.Single(await service.ListAsync());

        Assert.Equal("NeedsReview", listed.State);
        Assert.Equal(0, hashAttempts);
    }

    [Fact]
    public async Task Quarantine_rejects_changed_source_metadata_before_hashing_contents()
    {
        using var fixture = new Fixture("Confirmation");
        var original = fixture.File("old.tmp", "synthetic", 9);
        var hashAttempts = 0;
        var service = fixture.NewService("Confirmation", beforeCandidateHash: () => hashAttempts++);
        var preview = await service.PreviewAsync();
        using (var source = new FileStream(original, FileMode.Open, FileAccess.Write, FileShare.None))
            source.SetLength(16L * 1024 * 1024 + 1);

        var result = await service.QuarantineAsync(preview.Id, preview.Id);

        Assert.Equal(0, result.Moved);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, hashAttempts);
        Assert.True(System.IO.File.Exists(original));
    }

    [Fact]
    public async Task Stale_valid_restore_journal_temp_does_not_block_recovery_after_restart()
    {
        using var fixture = new Fixture("Confirmation");
        var original = fixture.File("old.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();
        Assert.Equal("quarantine_complete", (await fixture.Service.QuarantineAsync(preview.Id, preview.Id)).Code);
        var entry = Assert.Single(await fixture.Service.ListAsync());
        var journalPath = Path.Combine(fixture.Quarantine, entry.Id + ".json");
        var journal = await System.IO.File.ReadAllTextAsync(journalPath);
        var restoreIntent = journal.Replace("\"State\":\"Quarantined\"", "\"State\":\"RestoreIntent\"", StringComparison.Ordinal);
        await System.IO.File.WriteAllTextAsync(journalPath + ".tmp", restoreIntent);
        var restarted = fixture.NewService("Confirmation");

        var result = await restarted.RestoreAsync(entry.Id, entry.Id);

        Assert.Equal("restore_complete", result.Code);
        Assert.Equal("synthetic", await System.IO.File.ReadAllTextAsync(original));
        Assert.Equal("Restored", Assert.Single(await restarted.ListAsync()).State);
    }

    [Fact]
    public async Task Newly_created_quarantine_directory_is_pinned_before_acl_changes()
    {
        using var fixture = new Fixture("Confirmation");
        fixture.File("old.tmp", "synthetic", 9);
        var target = Path.Combine(fixture.Root, "outside-target");
        Directory.CreateDirectory(target);
        var targetInfo = new DirectoryInfo(target);
        var targetAcl = targetInfo.GetAccessControl();
        targetAcl.AddAccessRule(new FileSystemAccessRule(new System.Security.Principal.SecurityIdentifier("S-1-5-32-545"),
            FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        targetInfo.SetAccessControl(targetAcl);
        var before = targetInfo.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
        var aclApplied = false;
        var service = fixture.NewService("Confirmation", child =>
        {
            if (!child.Equals(fixture.Quarantine, StringComparison.OrdinalIgnoreCase)) return;
            Directory.Delete(child);
            Link("/J", child, target).GetAwaiter().GetResult();
        }, afterPrivateAcl: _ => aclApplied = true);
        var preview = await service.PreviewAsync();

        var result = await service.QuarantineAsync(preview.Id, preview.Id);

        var after = targetInfo.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
        Assert.Equal("maintenance_root_unavailable", result.Code);
        Assert.False(aclApplied);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Final_journal_failure_is_reported_separately_from_skipped_files()
    {
        using var fixture = new Fixture("Confirmation");
        fixture.File("old.tmp", "synthetic", 9);
        var service = fixture.NewService("Confirmation", rejectJournalState: state => state == "Quarantined");
        var preview = await service.PreviewAsync();

        var result = await service.QuarantineAsync(preview.Id, preview.Id);

        Assert.Equal("quarantine_recovery_required", result.Code);
        Assert.Equal(1, result.Moved);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.JournalWarnings);
    }

    [Fact]
    public async Task Quarantine_preserves_journal_warning_if_approval_is_revoked_mid_batch()
    {
        using var fixture = new Fixture("Confirmation");
        fixture.File("first.tmp", "first", 9);
        fixture.File("second.tmp", "second", 9);
        var stateReads = 0;
        var service = new UserMaintenance(fixture.Source, fixture.Quarantine, TimeProvider.System, _ =>
        {
            var mode = ++stateReads <= 2 ? "Confirmation" : "Shadow";
            return ValueTask.FromResult<DashboardSnapshot?>(new DashboardSnapshot(DateTimeOffset.UtcNow, mode, false, false,
                null, null, null, [], [], [], null, [], [], [], []));
        }, rejectJournalState: state => state == "Quarantined");
        var preview = await service.PreviewAsync();

        var result = await service.QuarantineAsync(preview.Id, preview.Id);

        Assert.Equal("maintenance_not_approved", result.Code);
        Assert.Equal(1, result.Moved);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(1, result.JournalWarnings);
    }

    [Fact]
    public async Task Final_restore_journal_failure_reports_the_successful_restore_and_recovery_state()
    {
        using var fixture = new Fixture("Confirmation");
        var original = fixture.File("old.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();
        Assert.Equal("quarantine_complete", (await fixture.Service.QuarantineAsync(preview.Id, preview.Id)).Code);
        var entry = Assert.Single(await fixture.Service.ListAsync());
        var service = fixture.NewService("Confirmation", rejectJournalState: state => state == "Restored");

        var result = await service.RestoreAsync(entry.Id, entry.Id);

        Assert.Equal("restore_recovery_required", result.Code);
        Assert.Equal(1, result.Moved);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.JournalWarnings);
        Assert.Equal("synthetic", await System.IO.File.ReadAllTextAsync(original));
        Assert.Equal("Restored", Assert.Single(await service.ListAsync()).State);
    }

    [Fact]
    public async Task Quarantine_creation_rejects_a_junction_ancestor_before_writing_outside_it()
    {
        using var fixture = new Fixture("Confirmation");
        fixture.File("old.tmp", "synthetic", 9);
        var target = Path.Combine(fixture.Root, "outside-target");
        Directory.CreateDirectory(target);
        var parent = Path.Combine(fixture.Root, "HostAgent");
        await Link("/J", parent, target);
        var service = new UserMaintenance(fixture.Source, Path.Combine(parent, "Quarantine"), TimeProvider.System,
            _ => ValueTask.FromResult<DashboardSnapshot?>(new DashboardSnapshot(DateTimeOffset.UtcNow, "Confirmation", false, false,
                null, null, null, [], [], [], null, [], [], [], [])));
        var preview = await service.PreviewAsync();

        var result = await service.QuarantineAsync(preview.Id, preview.Id);
        Assert.Equal("maintenance_root_unavailable", result.Code);

        Assert.False(Directory.Exists(Path.Combine(target, "Quarantine")));
    }

    [Fact]
    public async Task Quarantine_never_exceeds_remaining_total_byte_quota()
    {
        using var fixture = new Fixture("Confirmation");
        const long chunk = 15L * 1024 * 1024;
        for (var index = 0; index < 8; index++) fixture.SizedFile($"large-{index}.tmp", chunk);
        var first = await fixture.Service.PreviewAsync();
        Assert.Equal(8, first.Items.Count);
        var firstResult = await fixture.Service.QuarantineAsync(first.Id, first.Id);
        Assert.Equal(8, firstResult.Moved);
        Assert.Equal(120L * 1024 * 1024, firstResult.Bytes);

        fixture.SizedFile("last.tmp", 16L * 1024 * 1024);
        var next = await fixture.Service.PreviewAsync();
        var result = await fixture.Service.QuarantineAsync(next.Id, next.Id);

        Assert.Equal(0, result.Moved);
        Assert.True(System.IO.File.Exists(Path.Combine(fixture.Source, "last.tmp")));
        Assert.Equal(120L * 1024 * 1024, (await fixture.Service.ListAsync()).Where(item => item.State == "Quarantined").Sum(item => item.Bytes));
    }

    [Fact]
    public async Task Quarantine_uses_actual_payload_sizes_when_enforcing_total_byte_quota()
    {
        using var fixture = new Fixture("Confirmation");
        for (var index = 0; index < 8; index++) fixture.SizedFile($"large-{index}.tmp", 15L * 1024 * 1024);
        var initial = await fixture.Service.PreviewAsync();
        Assert.Equal(8, (await fixture.Service.QuarantineAsync(initial.Id, initial.Id)).Moved);
        var first = Assert.Single(await fixture.Service.ListAsync(), item => item.Name == "large-0.tmp");
        using (var payload = new FileStream(Path.Combine(fixture.Quarantine, first.Id + ".payload"), FileMode.Open, FileAccess.Write, FileShare.None))
            payload.SetLength(130L * 1024 * 1024);
        var candidate = fixture.File("small.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();

        var result = await fixture.Service.QuarantineAsync(preview.Id, preview.Id);

        Assert.Equal("maintenance_quota_reached", result.Code);
        Assert.Equal(0, result.Moved);
        Assert.True(System.IO.File.Exists(candidate));
    }

    [Fact]
    public async Task Quarantine_never_exceeds_global_journal_count_quota()
    {
        using var fixture = new Fixture("Confirmation");
        fixture.File("first.tmp", "a", 9);
        var first = await fixture.Service.PreviewAsync();
        Assert.Equal("quarantine_complete", (await fixture.Service.QuarantineAsync(first.Id, first.Id)).Code);
        var created = DateTimeOffset.UtcNow;
        for (var index = 0; index < 255; index++)
        {
            var id = Guid.NewGuid().ToString("N");
            var journal = System.Text.Json.JsonSerializer.Serialize(new { Id = id, Name = "x.tmp", Bytes = 0L,
                ModifiedAtUtc = created, Sha256 = new string('0', 64), Identity = "00000001:0000000000000001", CreatedAtUtc = created, State = "Restored" });
            await System.IO.File.WriteAllTextAsync(Path.Combine(fixture.Quarantine, id + ".json"), journal);
        }
        var candidate = fixture.File("second.tmp", "b", 9);
        var preview = await fixture.Service.PreviewAsync();

        var result = await fixture.Service.QuarantineAsync(preview.Id, preview.Id);

        Assert.Equal("maintenance_quota_reached", result.Code);
        Assert.True(System.IO.File.Exists(candidate));
    }

    [Fact]
    public async Task Quarantine_respects_cross_instance_exclusive_mutation_lock()
    {
        using var fixture = new Fixture("Confirmation");
        fixture.File("first.tmp", "a", 9);
        var first = await fixture.Service.PreviewAsync();
        Assert.Equal("quarantine_complete", (await fixture.Service.QuarantineAsync(first.Id, first.Id)).Code);
        var candidate = fixture.File("second.tmp", "b", 9);
        var preview = await fixture.Service.PreviewAsync();
        using (var held = MaintenanceMutationLock.TryAcquire(fixture.Quarantine))
        {
            Assert.NotNull(held);
            Assert.Equal("maintenance_busy", (await fixture.Service.QuarantineAsync(preview.Id, preview.Id)).Code);
        }
        Assert.True(System.IO.File.Exists(candidate));
    }

    [Fact]
    public async Task Expired_preview_cannot_be_used()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        using var fixture = new Fixture("Confirmation", clock: clock);
        var original = fixture.File("old.tmp", "synthetic", 9);
        var preview = await fixture.Service.PreviewAsync();
        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal("maintenance_preview_expired", (await fixture.Service.QuarantineAsync(preview.Id, preview.Id)).Code);
        Assert.True(System.IO.File.Exists(original));
    }

    private static async Task Link(string kind, string link, string target)
    {
        var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        info.Arguments = "/d /c mklink " + kind + " \"" + link + "\" \"" + target + "\"";
        using var process = Process.Start(info)!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "HostAgent-Maintenance-Test-" + Guid.NewGuid().ToString("N"));
        public string Root => _root;
        public string Source => Path.Combine(_root, "source");
        public string Quarantine => Path.Combine(_root, "quarantine");
        public UserMaintenance Service { get; }
        private readonly string _mode;
        private readonly bool _readOnly, _killSwitch;
        private readonly DateTimeOffset? _generatedAt;
        private readonly TimeProvider _clock;
        public Fixture(string mode = "Confirmation", bool readOnly = false, bool killSwitch = false,
            DateTimeOffset? generatedAt = null, TimeProvider? clock = null)
        {
            _mode = mode; _readOnly = readOnly; _killSwitch = killSwitch; _generatedAt = generatedAt; _clock = clock ?? TimeProvider.System;
            Directory.CreateDirectory(Source);
            Service = NewService(mode);
        }
        public UserMaintenance NewService(string mode, Action<string>? afterDirectoryCreate = null,
            Func<string, bool>? rejectJournalState = null, Action<string>? afterPrivateAcl = null,
            Action? beforeJournalHash = null, Action? beforeCandidateHash = null) => new(Source, Quarantine, _clock,
            _ => ValueTask.FromResult<DashboardSnapshot?>(new DashboardSnapshot(_generatedAt ?? _clock.GetUtcNow(), mode, _readOnly, false,
                null, null, null, [], [], [], null, [], [], [], []) { KillSwitch = _killSwitch }), afterDirectoryCreate, rejectJournalState, afterPrivateAcl, beforeJournalHash, beforeCandidateHash);
        public string File(string name, string contents, int ageDays)
        {
            var path = Path.Combine(Source, name);
            System.IO.File.WriteAllText(path, contents);
            System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-ageDays));
            return path;
        }
        public string SizedFile(string name, long bytes)
        {
            var path = Path.Combine(Source, name);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) stream.SetLength(bytes);
            System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-9));
            return path;
        }
        public void Dispose()
        {
            // _root is the fixture's exact GUID directory, never an environment-provided path.
            if (Directory.Exists(_root)) DeleteFixtureTree(_root);
        }
        private static void DeleteFixtureTree(string path)
        {
            foreach (var child in Directory.GetFileSystemEntries(path))
            {
                var attributes = System.IO.File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(child, false);
                    else System.IO.File.Delete(child);
                }
                else if ((attributes & FileAttributes.Directory) != 0) DeleteFixtureTree(child);
                else System.IO.File.Delete(child);
            }
            Directory.Delete(path, false);
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
