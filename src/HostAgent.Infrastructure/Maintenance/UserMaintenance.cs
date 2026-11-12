using HostAgent.Core.Dashboard;
using System.ComponentModel;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace HostAgent.Infrastructure.Maintenance;

public sealed record MaintenanceItem(string Id, string Name, long Bytes, DateTimeOffset ModifiedAtUtc, string Sha256);
public sealed record MaintenancePreview(string Id, DateTimeOffset ExpiresAtUtc, IReadOnlyList<MaintenanceItem> Items, int Skipped, bool Truncated)
{
    public string Code { get; init; } = "preview_ready";
    public string ScopePath { get; init; } = string.Empty;
    public string QuarantinePath { get; init; } = string.Empty;
    public uint? ScopeVolumeId { get; init; }
    public int InspectedFiles { get; init; }
    public MaintenanceEligibilityLimits EligibilityLimits { get; init; } = new(true, 500, 100, 15, 10, 7,
        16L * 1024 * 1024, 128L * 1024 * 1024, 256);
    public IReadOnlyList<MaintenanceSkippedItem> SkippedItems { get; init; } = Array.Empty<MaintenanceSkippedItem>();
}
public sealed record MaintenanceEligibilityLimits(bool TopLevelOnly, int MaximumInspectedFiles, int MaximumCandidates,
    int PreviewTimeoutSeconds, int PreviewValidityMinutes, int MinimumAgeDays, long MaximumFileBytes,
    long MaximumTotalBytes, int MaximumQuarantineEntries);
public static class MaintenanceSkipReasonCodes
{
    public const string UnsafeName = "unsafe_name";
    public const string TooRecent = "too_recent";
    public const string FileTooLarge = "file_too_large";
    public const string ScopeByteLimit = "scope_byte_limit";
    public const string UnsupportedFile = "unsupported_file";
    public const string MultipleLinks = "multiple_links";
    public const string ReadOnly = "read_only";
    public const string FileLocked = "file_locked";
    public const string AccessDenied = "access_denied";
    public const string FileUnavailable = "file_unavailable";
    public const string VerificationFailed = "verification_failed";
    public const string PreviewTimeLimit = "preview_time_limit";
}
public static class MaintenanceActionStates
{
    public const string Moved = "moved";
    public const string Restored = "restored";
    public const string Skipped = "skipped";
    public const string Blocked = "blocked";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Unknown = "unknown";
}
public static class MaintenanceActionReasonCodes
{
    public const string PreviewIncomplete = "preview_incomplete";
    public const string ServiceNotApproved = "service_not_approved";
    public const string SourceIdentityChanged = "source_identity_changed";
    public const string SourceMetadataChanged = "source_metadata_changed";
    public const string SourceHashChanged = "source_hash_changed";
    public const string DestinationAlreadyExists = "destination_already_exists";
    public const string QuarantineLimitReached = "quarantine_limit_reached";
    public const string OperationFailed = "operation_failed";
    public const string Cancelled = "cancelled";
    public const string OutcomeUnverified = "outcome_unverified";
}
public sealed record QuarantineItem(string Id, string Name, long Bytes, DateTimeOffset CreatedAtUtc, string State);
public sealed record MaintenanceActionItem(string Id, string Name, long Bytes, string SourcePath, string DestinationPath,
    string State, string ReasonCode);
public sealed record MaintenanceFreeSpaceReadback(long? AvailableBytes, DateTimeOffset? MeasuredAtUtc)
{
    public bool IsKnown => AvailableBytes.HasValue;
}
public sealed record MaintenanceResult(string Code, int Moved, int Skipped, long Bytes, int JournalWarnings = 0)
{
    public IReadOnlyList<MaintenanceActionItem> Items { get; init; } = Array.Empty<MaintenanceActionItem>();
    public MaintenanceFreeSpaceReadback FreeSpace { get; init; } = new(null, null);
    public int Restored { get; init; }
    public int Blocked => Items.Count(item => item.State == MaintenanceActionStates.Blocked);
    public int Failed => Items.Count(item => item.State == MaintenanceActionStates.Failed);
    public int Cancelled => Items.Count(item => item.State == MaintenanceActionStates.Cancelled);
    public int Unknown => Items.Count(item => item.State == MaintenanceActionStates.Unknown);
}

public sealed record MaintenanceSkippedItem(string Name, string ReasonCode);

public sealed class UserMaintenance
{
    private readonly string _source, _quarantine;
    private readonly TimeProvider _clock;
    private readonly Func<CancellationToken, ValueTask<DashboardSnapshot?>> _stateReader;
    private readonly Action<string>? _afterDirectoryCreate;
    private readonly Action<string>? _afterPrivateAcl;
    private readonly Action? _beforeJournalHash;
    private readonly Action? _beforeCandidateHash;
    private readonly Action? _beforePreviewHash;
    private readonly Action? _afterQuarantineMove;
    private readonly Func<long?>? _freeSpaceReader;
    private readonly Func<string, bool>? _rejectJournalState;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Plan? _plan;
    private readonly bool _selectedDirectory;
    public string ScopePath => _source;
    public string QuarantinePath => _quarantine;
    public bool IsSelectedDirectory => _selectedDirectory;
    private const int MaximumInspectedFiles = 500;
    private const int MaximumCandidates = 100;
    private const int PreviewTimeoutSeconds = 15;
    private const int PreviewValidityMinutes = 10;
    private const int MinimumAgeDays = 7;
    private const int MaximumQuarantineEntries = 256;
    private sealed record Candidate(MaintenanceItem Item, FileProof Proof);
    private sealed record Plan(MaintenancePreview Preview, DateTimeOffset CreatedAtUtc, Candidate[] Candidates);
    private sealed record Journal(string Id, string Name, long Bytes, DateTimeOffset ModifiedAtUtc,
        string Sha256, string Identity, DateTimeOffset CreatedAtUtc, string State);
    private sealed record JournalInventory(Journal[] Entries, bool HasInvalidEntries, long PayloadBytes = 0);

    internal UserMaintenance(string source, string quarantine, TimeProvider clock,
        Func<CancellationToken, ValueTask<DashboardSnapshot?>> stateReader, Action<string>? afterDirectoryCreate = null,
        Func<string, bool>? rejectJournalState = null, Action<string>? afterPrivateAcl = null, Action? beforeJournalHash = null,
        Action? beforeCandidateHash = null, bool selectedDirectory = false, Action? beforePreviewHash = null,
        Action? afterQuarantineMove = null, Func<long?>? freeSpaceReader = null)
    {
        _source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        _quarantine = Path.GetFullPath(quarantine).TrimEnd(Path.DirectorySeparatorChar);
        _clock = clock; _stateReader = stateReader; _afterDirectoryCreate = afterDirectoryCreate;
        _rejectJournalState = rejectJournalState; _afterPrivateAcl = afterPrivateAcl; _beforeJournalHash = beforeJournalHash;
        _beforeCandidateHash = beforeCandidateHash; _beforePreviewHash = beforePreviewHash;
        _afterQuarantineMove = afterQuarantineMove;
        _freeSpaceReader = freeSpaceReader;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var temp = Path.Combine(local, "Temp");
        _selectedDirectory = selectedDirectory;
        if (!MaintenanceSecurity.NormalUser || !(selectedDirectory ? SafeSelectedDirectory(_source) : Within(_source, temp)) || !Within(_quarantine, local)
            || Within(_quarantine, _source) || Within(_source, _quarantine)) throw new UnauthorizedAccessException("maintenance_scope");
    }

    public static UserMaintenance CreateForCurrentUser(Func<CancellationToken, ValueTask<DashboardSnapshot?>> stateReader)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new UserMaintenance(Path.Combine(local, "Temp"), Path.Combine(local, "HostAgent", "Quarantine"), TimeProvider.System, stateReader);
    }

    public static UserMaintenance CreateForSelectedDirectory(string directory, Func<CancellationToken, ValueTask<DashboardSnapshot?>> stateReader)
    {
        var source = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source.ToUpperInvariant())));
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new UserMaintenance(source, Path.Combine(local, "HostAgent", "SelectedQuarantine", key), TimeProvider.System, stateReader, selectedDirectory: true);
    }

    private static bool SafeSelectedDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.Length <= 3) return false;
        foreach (var folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData })
        {
            var protectedPath = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(protectedPath) && (Within(path, protectedPath) || Within(protectedPath, path))) return false;
        }
        return true;
    }

    private static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public async ValueTask<MaintenancePreview> PreviewAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            _plan = null;
            var now = _clock.GetUtcNow();
            var candidates = new List<Candidate>(); var skippedItems = new List<MaintenanceSkippedItem>(); var inspected = 0; long bytes = 0; var truncated = false;
            uint? scopeVolumeId = null;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(PreviewTimeoutSeconds));
            void Skip(string name, string reasonCode) => skippedItems.Add(new(
                MaintenanceSecurity.SafeName(name) ? name : string.Empty, reasonCode));
            try
            {
                using var source = PinnedDirectory.Open(_source, true);
                scopeVolumeId = source.Volume;
                foreach (var path in Directory.EnumerateFiles(_source, "*", SearchOption.TopDirectoryOnly))
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    if (inspected >= MaximumInspectedFiles || candidates.Count >= MaximumCandidates) { truncated = true; break; }
                    inspected++;
                    var name = Path.GetFileName(path);
                    var opened = false;
                    try
                    {
                        if (!MaintenanceSecurity.SafeName(name)) { Skip(name, MaintenanceSkipReasonCodes.UnsafeName); continue; }
                        using var file = PinnedFile.Open(path);
                        opened = true;
                        if (!_selectedDirectory && now - file.Info.ModifiedAtUtc < TimeSpan.FromDays(MinimumAgeDays))
                        { Skip(name, MaintenanceSkipReasonCodes.TooRecent); continue; }
                        if (file.Info.Bytes > MaximumFileBytes) { Skip(name, MaintenanceSkipReasonCodes.FileTooLarge); continue; }
                        if (file.Info.Bytes > MaximumTotalBytes - bytes) { Skip(name, MaintenanceSkipReasonCodes.ScopeByteLimit); continue; }
                        _beforePreviewHash?.Invoke();
                        var proof = await file.ProofAsync(deadline.Token);
                        if (proof.Identity != file.Info.Identity || proof.Bytes != file.Info.Bytes
                            || proof.ModifiedAtUtc != file.Info.ModifiedAtUtc)
                        { Skip(name, MaintenanceSkipReasonCodes.VerificationFailed); continue; }
                        var item = new MaintenanceItem(Guid.NewGuid().ToString("N"), name, proof.Bytes, proof.ModifiedAtUtc, proof.Sha256);
                        candidates.Add(new Candidate(item, proof)); bytes += proof.Bytes;
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        Skip(name, MaintenanceSkipReasonCodes.PreviewTimeLimit);
                        truncated = true;
                        break;
                    }
                    catch (Exception error) when (MaintenanceSecurity.Expected(error))
                    {
                        Skip(name, error is MaintenanceFileRejectedException rejected ? rejected.ReasonCode
                            : error is Win32Exception win32 && win32.NativeErrorCode is 32 or 33 ? MaintenanceSkipReasonCodes.FileLocked
                            : opened ? MaintenanceSkipReasonCodes.VerificationFailed
                            : error is UnauthorizedAccessException ? MaintenanceSkipReasonCodes.AccessDenied
                            : MaintenanceSkipReasonCodes.FileUnavailable);
                    }
                }
                if (deadline.IsCancellationRequested && !token.IsCancellationRequested) truncated = true;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { truncated = true; }
            catch (Exception error) when (MaintenanceSecurity.Expected(error))
            {
                return new MaintenancePreview(Guid.NewGuid().ToString("D"), now, [], skippedItems.Count, false)
                {
                    Code = "maintenance_root_unavailable",
                    ScopePath = _source,
                    QuarantinePath = _quarantine,
                    EligibilityLimits = CurrentEligibilityLimits,
                    InspectedFiles = inspected,
                    SkippedItems = Array.AsReadOnly(skippedItems.ToArray())
                };
            }
            var preview = new MaintenancePreview(Guid.NewGuid().ToString("D"), now.AddMinutes(PreviewValidityMinutes),
                Array.AsReadOnly(candidates.Select(x => x.Item).ToArray()), skippedItems.Count, truncated)
            {
                ScopePath = _source,
                QuarantinePath = _quarantine,
                ScopeVolumeId = scopeVolumeId,
                InspectedFiles = inspected,
                EligibilityLimits = CurrentEligibilityLimits,
                SkippedItems = Array.AsReadOnly(skippedItems.ToArray())
            };
            _plan = new Plan(preview, now, candidates.ToArray());
            return preview;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<MaintenanceResult> QuarantineAsync(string previewId, string confirmation, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var plan = _plan;
            if (plan is null || !string.Equals(previewId, plan.Preview.Id, StringComparison.Ordinal)
                || !string.Equals(confirmation, plan.Preview.Id, StringComparison.Ordinal)) return Result("maintenance_confirmation_required");
            _plan = null; // One exact, correctly bound confirmation can be attempted only once.
            var items = new List<MaintenanceActionItem>(plan.Candidates.Length);
            var moved = 0; var skipped = 0; var failed = 0; var unknown = 0; var journalWarnings = 0;
            var cancelled = false; long bytes = 0;
            MaintenanceActionItem ActionItem(Candidate candidate, string state, string reasonCode) => new(
                candidate.Item.Id, candidate.Item.Name, candidate.Item.Bytes,
                Path.Combine(_source, candidate.Item.Name), PayloadPath(candidate.Item.Id), state, reasonCode);
            void AddRemaining(int start, string state, string reasonCode)
            {
                for (var index = start; index < plan.Candidates.Length; index++)
                    items.Add(ActionItem(plan.Candidates[index], state, reasonCode));
            }
            MaintenanceResult Finish(string code) => new(code, moved, skipped, bytes, journalWarnings)
            {
                Items = Array.AsReadOnly(items.ToArray()),
                FreeSpace = moved > 0 ? ReadFreeSpaceReadback() : new(null, null)
            };
            MaintenanceResult BlockAll(string code, string reasonCode)
            {
                AddRemaining(0, MaintenanceActionStates.Blocked, reasonCode);
                return Finish(code);
            }

            if (plan.Preview.Truncated) return BlockAll("maintenance_preview_incomplete", MaintenanceActionReasonCodes.PreviewIncomplete);
            if (_clock.GetUtcNow() > plan.Preview.ExpiresAtUtc) return BlockAll("maintenance_preview_expired", "preview_expired");
            if (plan.Candidates.Length == 0) return Finish("quarantine_empty");
            try
            {
                if (!await ApprovedAsync(token)) return BlockAll("maintenance_not_approved", MaintenanceActionReasonCodes.ServiceNotApproved);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                cancelled = true;
                return BlockAll("quarantine_cancelled", MaintenanceActionReasonCodes.Cancelled);
            }
            catch (Exception)
            {
                return BlockAll("maintenance_state_unavailable", MaintenanceActionReasonCodes.ServiceNotApproved);
            }
            if (token.IsCancellationRequested)
            {
                cancelled = true;
                return BlockAll("quarantine_cancelled", MaintenanceActionReasonCodes.Cancelled);
            }

            try { EnsurePrivateRoot(); }
            catch (Exception error) when (MaintenanceSecurity.Expected(error))
            { return BlockAll("maintenance_root_unavailable", "quarantine_root_unavailable"); }

            PinnedDirectory source;
            try { source = PinnedDirectory.Open(_source, true); }
            catch (Exception error) when (MaintenanceSecurity.Expected(error))
            { return BlockAll("maintenance_root_unavailable", "source_root_unavailable"); }
            using (source)
            {
                PinnedDirectory quarantine;
                try { quarantine = PinnedDirectory.Open(_quarantine, true); }
                catch (Exception error) when (MaintenanceSecurity.Expected(error))
                { return BlockAll("maintenance_root_unavailable", "quarantine_root_unavailable"); }
                using (quarantine)
                {
                    FileStream? mutationLock;
                    try { mutationLock = MaintenanceMutationLock.TryAcquire(_quarantine); }
                    catch (Exception error) when (MaintenanceSecurity.Expected(error))
                    { return BlockAll("maintenance_root_unavailable", "quarantine_lock_unavailable"); }
                    if (mutationLock is null) return BlockAll("maintenance_busy", "quarantine_busy");
                    using (mutationLock)
                    {
                        if (source.Volume != quarantine.Volume) return BlockAll("maintenance_volume_mismatch", "volume_mismatch");
                        JournalInventory inventory;
                        try { inventory = ReadJournals(); }
                        catch (Exception error) when (MaintenanceSecurity.Expected(error))
                        { return BlockAll("maintenance_journal_integrity", "journal_unavailable"); }
                        if (inventory.HasInvalidEntries) return BlockAll("maintenance_journal_integrity", "journal_integrity");
                        var existing = inventory.Entries;
                        if (existing.Length >= MaximumQuarantineEntries || inventory.PayloadBytes > MaximumTotalBytes
                            || existing.Sum(item => item.State is "Quarantined" or "Intent" or "RestoreIntent" ? item.Bytes : 0) > MaximumTotalBytes)
                            return BlockAll("maintenance_quota_reached", MaintenanceActionReasonCodes.QuarantineLimitReached);

                        var reservedJournals = existing.Length;
                        var reservedBytes = Math.Max(inventory.PayloadBytes,
                            existing.Sum(item => item.State is "Quarantined" or "Intent" or "RestoreIntent" ? item.Bytes : 0));
                        for (var index = 0; index < plan.Candidates.Length; index++)
                        {
                            var candidate = plan.Candidates[index];
                            if (token.IsCancellationRequested)
                            {
                                cancelled = true;
                                AddRemaining(index, MaintenanceActionStates.Cancelled, MaintenanceActionReasonCodes.Cancelled);
                                break;
                            }
                            bool approved;
                            try { approved = await ApprovedAsync(token); }
                            catch (OperationCanceledException) when (token.IsCancellationRequested)
                            {
                                cancelled = true;
                                AddRemaining(index, MaintenanceActionStates.Cancelled, MaintenanceActionReasonCodes.Cancelled);
                                break;
                            }
                            catch (Exception)
                            {
                                AddRemaining(index, MaintenanceActionStates.Blocked, MaintenanceActionReasonCodes.ServiceNotApproved);
                                break;
                            }
                            if (!approved)
                            {
                                AddRemaining(index, MaintenanceActionStates.Blocked, MaintenanceActionReasonCodes.ServiceNotApproved);
                                break;
                            }

                            var journal = new Journal(candidate.Item.Id, candidate.Item.Name, candidate.Proof.Bytes,
                                candidate.Proof.ModifiedAtUtc, candidate.Proof.Sha256, candidate.Proof.Identity,
                                _clock.GetUtcNow(), "Intent");
                            var payloadPath = PayloadPath(journal.Id);
                            var opened = false; var journalWriteAttempted = false; var intentWritten = false; var itemRecorded = false;
                            try
                            {
                                using var file = PinnedFile.Open(Path.Combine(_source, candidate.Item.Name), true);
                                opened = true;
                                if (file.Info.Identity != candidate.Proof.Identity)
                                {
                                    skipped++;
                                    items.Add(ActionItem(candidate, MaintenanceActionStates.Skipped, MaintenanceActionReasonCodes.SourceIdentityChanged));
                                    continue;
                                }
                                if (file.Info.Bytes != candidate.Proof.Bytes || file.Info.ModifiedAtUtc != candidate.Proof.ModifiedAtUtc)
                                {
                                    skipped++;
                                    items.Add(ActionItem(candidate, MaintenanceActionStates.Skipped, MaintenanceActionReasonCodes.SourceMetadataChanged));
                                    continue;
                                }
                                _beforeCandidateHash?.Invoke();
                                var proof = await file.ProofAsync(token);
                                if (!Same(proof, candidate.Proof))
                                {
                                    skipped++;
                                    items.Add(ActionItem(candidate, MaintenanceActionStates.Skipped, MaintenanceActionReasonCodes.SourceHashChanged));
                                    continue;
                                }
                                if (System.IO.File.Exists(payloadPath) || System.IO.File.Exists(JournalPath(journal.Id)))
                                {
                                    skipped++;
                                    items.Add(ActionItem(candidate, MaintenanceActionStates.Skipped, MaintenanceActionReasonCodes.DestinationAlreadyExists));
                                    continue;
                                }
                                if (reservedJournals >= MaximumQuarantineEntries || journal.Bytes > MaximumTotalBytes - reservedBytes)
                                {
                                    skipped++;
                                    items.Add(ActionItem(candidate, MaintenanceActionStates.Skipped, MaintenanceActionReasonCodes.QuarantineLimitReached));
                                    continue;
                                }
                                journalWriteAttempted = true;
                                WriteJournal(journal); // Durable intent precedes the same-volume handle rename.
                                intentWritten = true;
                                reservedJournals++; reservedBytes += journal.Bytes;
                                MaintenanceNative.Rename(file.Handle, payloadPath);
                                moved++; bytes += journal.Bytes;
                                var itemJournalWarning = false;
                                journal = journal with { State = "Quarantined" };
                                try { WriteJournal(journal); }
                                catch (Exception error) when (MaintenanceSecurity.Expected(error))
                                { journalWarnings++; itemJournalWarning = true; }
                                items.Add(ActionItem(candidate, MaintenanceActionStates.Moved,
                                    itemJournalWarning ? "journal_recovery_required" : string.Empty));
                                itemRecorded = true;
                                _afterQuarantineMove?.Invoke();
                            }
                            catch (OperationCanceledException) when (token.IsCancellationRequested)
                            {
                                cancelled = true;
                                if (!itemRecorded)
                                    items.Add(ActionItem(candidate, MaintenanceActionStates.Cancelled, MaintenanceActionReasonCodes.Cancelled));
                                AddRemaining(index + 1, MaintenanceActionStates.Cancelled, MaintenanceActionReasonCodes.Cancelled);
                                break;
                            }
                            catch (Exception error) when (MaintenanceSecurity.Expected(error))
                            {
                                if (itemRecorded)
                                {
                                    failed++;
                                    AddRemaining(index + 1, MaintenanceActionStates.Blocked, MaintenanceActionReasonCodes.OperationFailed);
                                    break;
                                }
                                if (journalWriteAttempted && !intentWritten)
                                {
                                    failed++;
                                    items.Add(ActionItem(candidate, MaintenanceActionStates.Failed, MaintenanceActionReasonCodes.OperationFailed));
                                    AddRemaining(index + 1, MaintenanceActionStates.Blocked, MaintenanceActionReasonCodes.OperationFailed);
                                    break;
                                }
                                if (intentWritten)
                                {
                                    string outcome;
                                    try { outcome = await VerifyQuarantineOutcomeAsync(journal, CancellationToken.None); }
                                    catch (Exception) { outcome = "unknown"; }
                                    if (outcome == "moved")
                                    {
                                        moved++; bytes += journal.Bytes; journalWarnings++;
                                        items.Add(ActionItem(candidate, MaintenanceActionStates.Moved, "journal_recovery_required"));
                                    }
                                    else if (outcome == "not_moved")
                                    {
                                        failed++;
                                        items.Add(ActionItem(candidate, MaintenanceActionStates.Failed, MaintenanceActionReasonCodes.OperationFailed));
                                    }
                                    else
                                    {
                                        unknown++;
                                        items.Add(ActionItem(candidate, MaintenanceActionStates.Unknown, MaintenanceActionReasonCodes.OutcomeUnverified));
                                    }
                                    AddRemaining(index + 1, MaintenanceActionStates.Blocked, MaintenanceActionReasonCodes.OperationFailed);
                                    break;
                                }
                                skipped++;
                                items.Add(ActionItem(candidate, MaintenanceActionStates.Skipped, opened
                                    ? MaintenanceSkipReasonCodes.VerificationFailed : PreviewSkipReason(error)));
                            }
                        }
                        var blocked = items.Count(item => item.State == MaintenanceActionStates.Blocked);
                        var code = cancelled ? moved > 0 ? "quarantine_cancelled_partial" : "quarantine_cancelled"
                            : unknown > 0 ? "quarantine_outcome_unknown"
                            : failed > 0 ? moved > 0 ? "quarantine_partial_failure" : "quarantine_failed"
                            : blocked > 0 ? moved > 0
                                ? journalWarnings > 0 ? "quarantine_partial_blocked_recovery_required" : "quarantine_partial_blocked"
                                : "quarantine_blocked"
                            : journalWarnings > 0 ? "quarantine_recovery_required"
                            : moved > 0 && skipped == 0 ? "quarantine_complete"
                            : moved > 0 ? "quarantine_partial" : "quarantine_no_files_moved";
                        return Finish(code);
                    }
                }
            }
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<IReadOnlyList<QuarantineItem>> ListAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!Directory.Exists(_quarantine)) return Array.Empty<QuarantineItem>();
            using var root = PinnedDirectory.Open(_quarantine, true);
            var result = new List<QuarantineItem>();
            using var source = PinnedDirectory.Open(_source, true);
            var inventory = ReadJournals();
            foreach (var journal in inventory.Entries.Take(256))
            {
                token.ThrowIfCancellationRequested();
                var state = journal.State;
                var payload = System.IO.File.Exists(PayloadPath(journal.Id));
                var original = System.IO.File.Exists(Path.Combine(_source, journal.Name));
                // These states come from a removed development-only delete flow. Keep their
                // journals readable, but never treat them as restorable or as proof of success.
                if (state is "DeleteIntent" or "Deleted") state = "NeedsReview";
                if (state is "Intent" or "RestoreIntent" or "Quarantined")
                {
                    if (payload && !original)
                        state = await MatchesPathAsync(PayloadPath(journal.Id), journal, token) ? "Quarantined" : "NeedsReview";
                    else if (!payload && original)
                        state = state is "Intent" or "RestoreIntent"
                            ? await MatchesPathAsync(Path.Combine(_source, journal.Name), journal, token)
                                ? state == "Intent" ? "NotMoved" : "Restored"
                                : "NeedsReview"
                            : "NeedsReview";
                    else state = "NeedsReview";
                }
                result.Add(new(journal.Id, journal.Name, journal.Bytes, journal.CreatedAtUtc, state));
            }
            if (inventory.HasInvalidEntries)
                result.Add(new("requires-review", "Poškozený záznam karantény", 0, _clock.GetUtcNow(), "NeedsReview"));
            return result.AsReadOnly();
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<MaintenanceResult> RestoreAsync(string entryId, string confirmation, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!Guid.TryParseExact(entryId, "N", out _) || !string.Equals(entryId, confirmation, StringComparison.Ordinal)) return Result("maintenance_confirmation_required");
            JournalInventory inventory;
            try { inventory = ReadJournals(); }
            catch (Exception error) when (MaintenanceSecurity.Expected(error)) { return Result("maintenance_journal_integrity"); }
            if (inventory.HasInvalidEntries) return Result("maintenance_journal_integrity");
            var journal = inventory.Entries.FirstOrDefault(item => item.Id == entryId);
            if (journal is null || journal.State is not ("Quarantined" or "Intent" or "RestoreIntent")) return Result("maintenance_entry_unavailable");
            var original = Path.Combine(_source, journal.Name);
            var payloadPath = PayloadPath(journal.Id);
            MaintenanceActionItem ActionItem(string state, string reasonCode) => new(journal.Id, journal.Name, journal.Bytes,
                payloadPath, original, state, reasonCode);
            MaintenanceResult Finish(string code, string state, string reasonCode, int restored = 0, int skipped = 0,
                int warnings = 0, bool readback = false) => new(code, 0, skipped, restored > 0 ? journal.Bytes : 0, warnings)
            {
                Items = Array.AsReadOnly(new[] { ActionItem(state, reasonCode) }),
                FreeSpace = readback ? ReadFreeSpaceReadback() : new(null, null),
                Restored = restored
            };

            try
            {
                if (!await ApprovedAsync(token))
                    return Finish("maintenance_not_approved", MaintenanceActionStates.Blocked, MaintenanceActionReasonCodes.ServiceNotApproved);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { return Finish("restore_cancelled", MaintenanceActionStates.Cancelled, MaintenanceActionReasonCodes.Cancelled); }
            catch (Exception)
            { return Finish("maintenance_state_unavailable", MaintenanceActionStates.Blocked, MaintenanceActionReasonCodes.ServiceNotApproved); }

            PinnedDirectory source;
            try { source = PinnedDirectory.Open(_source, true); }
            catch (Exception error) when (MaintenanceSecurity.Expected(error))
            { return Finish("maintenance_root_unavailable", MaintenanceActionStates.Blocked, "source_root_unavailable"); }
            using (source)
            {
                PinnedDirectory quarantine;
                try { quarantine = PinnedDirectory.Open(_quarantine, true); }
                catch (Exception error) when (MaintenanceSecurity.Expected(error))
                { return Finish("maintenance_root_unavailable", MaintenanceActionStates.Blocked, "quarantine_root_unavailable"); }
                using (quarantine)
                {
                    FileStream? mutationLock;
                    try { mutationLock = MaintenanceMutationLock.TryAcquire(_quarantine); }
                    catch (Exception error) when (MaintenanceSecurity.Expected(error))
                    { return Finish("maintenance_root_unavailable", MaintenanceActionStates.Blocked, "quarantine_lock_unavailable"); }
                    if (mutationLock is null) return Finish("maintenance_busy", MaintenanceActionStates.Blocked, "quarantine_busy");
                    using (mutationLock)
                    {
                        if (source.Volume != quarantine.Volume)
                            return Finish("maintenance_volume_mismatch", MaintenanceActionStates.Blocked, "volume_mismatch");
                        if (System.IO.File.Exists(original) || Directory.Exists(original))
                            return Finish("restore_conflict", MaintenanceActionStates.Blocked, MaintenanceActionReasonCodes.DestinationAlreadyExists);
                        PinnedFile payload;
                        try { payload = PinnedFile.Open(payloadPath, true); }
                        catch (Exception error) when (MaintenanceSecurity.Expected(error))
                        { return Finish("maintenance_payload_changed", MaintenanceActionStates.Blocked, "payload_unavailable"); }
                        using (payload)
                        {
                            try
                            {
                                if (!await MatchesJournalAsync(payload, journal, token))
                                    return Finish("maintenance_payload_changed", MaintenanceActionStates.Blocked, "payload_verification_failed");
                                if (!await ApprovedAsync(token))
                                    return Finish("maintenance_not_approved", MaintenanceActionStates.Blocked, MaintenanceActionReasonCodes.ServiceNotApproved);
                            }
                            catch (OperationCanceledException) when (token.IsCancellationRequested)
                            { return Finish("restore_cancelled", MaintenanceActionStates.Cancelled, MaintenanceActionReasonCodes.Cancelled); }
                            catch (Exception)
                            { return Finish("maintenance_payload_changed", MaintenanceActionStates.Blocked, "payload_verification_failed"); }
                            try { WriteJournal(journal with { State = "RestoreIntent" }); }
                            catch (Exception error) when (MaintenanceSecurity.Expected(error))
                            { return Finish("maintenance_journal_write_failed", MaintenanceActionStates.Failed, MaintenanceActionReasonCodes.OperationFailed); }
                            try { MaintenanceNative.Rename(payload.Handle, original); }
                            catch (Exception error) when (MaintenanceSecurity.Expected(error))
                            {
                                string outcome;
                                try { outcome = await VerifyRestoreOutcomeAsync(journal, CancellationToken.None); }
                                catch (Exception) { outcome = "unknown"; }
                                if (outcome == "restored")
                                {
                                    try { WriteJournal(journal with { State = "Restored" }); }
                                    catch (Exception writeError) when (MaintenanceSecurity.Expected(writeError))
                                    { return Finish("restore_recovery_required", MaintenanceActionStates.Restored, "journal_recovery_required", 1, warnings: 1, readback: true); }
                                    return Finish("restore_complete", MaintenanceActionStates.Restored, string.Empty, 1, readback: true);
                                }
                                return outcome == "not_restored"
                                    ? Finish("restore_failed", MaintenanceActionStates.Failed, MaintenanceActionReasonCodes.OperationFailed)
                                    : Finish("restore_outcome_unknown", MaintenanceActionStates.Unknown, MaintenanceActionReasonCodes.OutcomeUnverified);
                            }
                            try { WriteJournal(journal with { State = "Restored" }); }
                            catch (Exception error) when (MaintenanceSecurity.Expected(error))
                            { return Finish("restore_recovery_required", MaintenanceActionStates.Restored, "journal_recovery_required", 1, warnings: 1, readback: true); }
                            return Finish("restore_complete", MaintenanceActionStates.Restored, string.Empty, 1, readback: true);
                        }
                    }
                }
            }
        }
        finally { _gate.Release(); }
    }

    private async ValueTask<bool> ApprovedAsync(CancellationToken token)
    {
        var snapshot = await _stateReader(token);
        var now = _clock.GetUtcNow();
        return snapshot is { Mode: "Confirmation", ReadOnly: false, KillSwitch: false }
            && ViewFreshness.IsFresh(snapshot.GeneratedAtUtc, now);
    }

    private void EnsurePrivateRoot()
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFileName(_quarantine));
        var parent = Path.GetDirectoryName(_quarantine);
        while (parent is not null && !Directory.Exists(parent))
        {
            pending.Push(Path.GetFileName(parent));
            parent = Path.GetDirectoryName(parent);
        }
        if (parent is null) throw new UnauthorizedAccessException("maintenance_root_unavailable");
        var pins = new List<PinnedDirectory>();
        try
        {
            pins.Add(PinnedDirectory.Open(parent));
            var current = parent;
            while (pending.Count > 0)
            {
                var child = Path.Combine(current, pending.Pop());
                var created = !Directory.Exists(child);
                if (created)
                {
                    Directory.CreateDirectory(child);
                    _afterDirectoryCreate?.Invoke(child);
                }
                pins.Add(PinnedDirectory.Open(child, !created));
                if (created)
                {
                    SetPrivateAcl(child);
                    _afterPrivateAcl?.Invoke(child);
                    MaintenanceSecurity.DemandPrivate(new DirectoryInfo(child).GetAccessControl());
                }
                current = child;
            }
        }
        finally { for (var index = pins.Count - 1; index >= 0; index--) pins[index].Dispose(); }
    }

    private static void SetPrivateAcl(string path)
    {
        var security = new DirectorySecurity();
        security.SetOwner(new SecurityIdentifier(MaintenanceSecurity.UserSid));
        security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { MaintenanceSecurity.UserSid, "S-1-5-18", "S-1-5-32-544" })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private JournalInventory ReadJournals()
    {
        if (!Directory.Exists(_quarantine)) return new([], false);
        using var pinned = PinnedDirectory.Open(_quarantine, true);
        var paths = new List<string>();
        var invalid = false;
        foreach (var path in Directory.EnumerateFileSystemEntries(_quarantine, "*", SearchOption.TopDirectoryOnly).Take(770))
        {
            paths.Add(path);
        }
        if (paths.Count > 769) invalid = true;

        var journals = new Dictionary<string, Journal>(StringComparer.Ordinal);
        var payloadIds = new HashSet<string>(StringComparer.Ordinal);
        var temporaryJournals = new List<(string Id, Journal Journal)>();
        var journalCount = 0; var payloadCount = 0; var temporaryCount = 0; long payloadBytes = 0;
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            if (name == "maintenance.lock")
            {
                try
                {
                    var attributes = System.IO.File.GetAttributes(path);
                    if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0) invalid = true;
                }
                catch (Exception error) when (MaintenanceSecurity.Expected(error)) { invalid = true; }
                continue;
            }

            if (Directory.Exists(path)) { invalid = true; continue; }
            var isCanonical = Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _)
                && name.EndsWith(".json", StringComparison.Ordinal);
            var isPayload = name.EndsWith(".payload", StringComparison.Ordinal)
                && Guid.TryParseExact(name[..^".payload".Length], "N", out _);
            var legacyTempId = name.EndsWith(".json.tmp", StringComparison.Ordinal)
                ? name[..^".json.tmp".Length] : string.Empty;
            var tempStem = name.EndsWith(".tmp", StringComparison.Ordinal) ? name[..^".tmp".Length] : string.Empty;
            var uniqueTempMarker = tempStem.LastIndexOf(".json.", StringComparison.Ordinal);
            var uniqueTempId = uniqueTempMarker > 0 ? tempStem[..uniqueTempMarker] : string.Empty;
            var uniqueSuffix = uniqueTempMarker > 0 ? tempStem[(uniqueTempMarker + ".json.".Length)..] : string.Empty;
            var isLegacyTemp = Guid.TryParseExact(legacyTempId, "N", out _);
            var isUniqueTemp = Guid.TryParseExact(uniqueTempId, "N", out _) && Guid.TryParseExact(uniqueSuffix, "N", out _);
            if (!isCanonical && !isPayload && !isLegacyTemp && !isUniqueTemp) { invalid = true; continue; }

            Journal? journal;
            try
            {
                using var file = PinnedFile.Open(path);
                if (isPayload)
                {
                    payloadCount++;
                    if (payloadCount > 256) invalid = true;
                    payloadIds.Add(name[..^".payload".Length]);
                    payloadBytes = file.Info.Bytes > MaximumTotalBytes - payloadBytes
                        ? MaximumTotalBytes + 1 : payloadBytes + file.Info.Bytes;
                    continue;
                }
                journal = ParseJournal(file.ReadSmall(32768));
            }
            catch (Exception error) when (MaintenanceSecurity.Expected(error) || error is JsonException or NotSupportedException)
            { invalid = true; continue; }
            if (!ValidJournal(journal)) { invalid = true; continue; }
            if (isCanonical)
            {
                journalCount++;
                if (journalCount > 256 || !string.Equals(Path.GetFileNameWithoutExtension(name), journal!.Id, StringComparison.Ordinal)
                    || !journals.TryAdd(journal!.Id, journal)) invalid = true;
            }
            else
            {
                temporaryCount++;
                if (temporaryCount > 256) invalid = true;
                var id = isLegacyTemp ? legacyTempId : uniqueTempId;
                if (!string.Equals(id, journal!.Id, StringComparison.Ordinal)) invalid = true;
                temporaryJournals.Add((id, journal));
            }
        }

        foreach (var id in payloadIds)
            if (!journals.ContainsKey(id)) invalid = true;
        foreach (var (id, temporary) in temporaryJournals)
        {
            if (!journals.TryGetValue(id, out var canonical) || !SameJournalPayload(canonical, temporary)) invalid = true;
        }
        return new(journals.Values.ToArray(), invalid, payloadBytes);
    }

    private MaintenanceEligibilityLimits CurrentEligibilityLimits => new(
        TopLevelOnly: true,
        MaximumInspectedFiles: MaximumInspectedFiles,
        MaximumCandidates: MaximumCandidates,
        PreviewTimeoutSeconds: PreviewTimeoutSeconds,
        PreviewValidityMinutes: PreviewValidityMinutes,
        MinimumAgeDays: _selectedDirectory ? 0 : MinimumAgeDays,
        MaximumFileBytes: MaximumFileBytes,
        MaximumTotalBytes: MaximumTotalBytes,
        MaximumQuarantineEntries: MaximumQuarantineEntries);

    private long MaximumFileBytes => _selectedDirectory ? 1024L * 1024 * 1024 : 16L * 1024 * 1024;
    private long MaximumTotalBytes => _selectedDirectory ? 10L * 1024 * 1024 * 1024 : 128L * 1024 * 1024;

    private bool ValidJournal(Journal? journal) => journal is not null && !string.IsNullOrEmpty(journal.Id)
        && !string.IsNullOrEmpty(journal.Name) && !string.IsNullOrEmpty(journal.Sha256) && !string.IsNullOrEmpty(journal.Identity)
        && !string.IsNullOrEmpty(journal.State) && Guid.TryParseExact(journal.Id, "N", out _) && MaintenanceSecurity.SafeName(journal.Name)
        && journal.Bytes >= 0 && journal.Bytes <= MaximumFileBytes && journal.Sha256.Length == 64 && journal.Sha256.All(Uri.IsHexDigit)
        && journal.Identity.Length == 25 && journal.Identity[8] == ':'
        && journal.Identity.Where((_, index) => index != 8).All(Uri.IsHexDigit)
        && journal.State is "Intent" or "Quarantined" or "RestoreIntent" or "Restored" or "DeleteIntent" or "Deleted";

    private static bool SameJournalPayload(Journal left, Journal right) => left.Id == right.Id && left.Name == right.Name
        && left.Bytes == right.Bytes && left.ModifiedAtUtc == right.ModifiedAtUtc && left.Sha256 == right.Sha256
        && left.Identity == right.Identity && left.CreatedAtUtc == right.CreatedAtUtc;

    private async ValueTask<bool> MatchesPathAsync(string path, Journal journal, CancellationToken token)
    {
        try
        {
            using var file = PinnedFile.Open(path);
            return await MatchesJournalAsync(file, journal, token);
        }
        catch (Exception error) when (MaintenanceSecurity.Expected(error)) { return false; }
    }

    private static Journal? ParseJournal(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 3 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
        var allowed = new HashSet<string>(["Id", "Name", "Bytes", "ModifiedAtUtc", "Sha256", "Identity", "CreatedAtUtc", "State"], StringComparer.Ordinal);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!allowed.Contains(property.Name) || !observed.Add(property.Name)) return null;
        if (observed.Count != allowed.Count) return null;
        return document.RootElement.Deserialize<Journal>();
    }

    private void WriteJournal(Journal journal)
    {
        if (_rejectJournalState?.Invoke(journal.State) == true) throw new IOException("maintenance_journal_write_failed");
        var path = JournalPath(journal.Id); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var data = JsonSerializer.SerializeToUtf8Bytes(journal);
        if (data.Length > 32768) throw new IOException("maintenance_journal_limit");
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { stream.Write(data); stream.Flush(true); }
        System.IO.File.Move(temp, path, true);
    }

    private async ValueTask<bool> MatchesJournalAsync(PinnedFile file, Journal journal, CancellationToken token)
    {
        if (file.Info.Identity != journal.Identity || file.Info.Bytes != journal.Bytes
            || file.Info.ModifiedAtUtc != journal.ModifiedAtUtc) return false;
        _beforeJournalHash?.Invoke();
        var proof = await file.ProofAsync(token);
        return proof.Identity == journal.Identity && proof.Bytes == journal.Bytes && proof.ModifiedAtUtc == journal.ModifiedAtUtc
            && string.Equals(proof.Sha256, journal.Sha256, StringComparison.Ordinal);
    }
    private static bool Same(FileProof left, FileProof right) => left.Identity == right.Identity && left.Bytes == right.Bytes
        && left.ModifiedAtUtc == right.ModifiedAtUtc && string.Equals(left.Sha256, right.Sha256, StringComparison.Ordinal);
    private static string PreviewSkipReason(Exception error) => error switch
    {
        MaintenanceFileRejectedException rejected => rejected.ReasonCode,
        Win32Exception win32 when win32.NativeErrorCode is 32 or 33 => MaintenanceSkipReasonCodes.FileLocked,
        UnauthorizedAccessException => MaintenanceSkipReasonCodes.AccessDenied,
        _ => MaintenanceSkipReasonCodes.FileUnavailable
    };
    private async ValueTask<string> VerifyQuarantineOutcomeAsync(Journal journal, CancellationToken token)
    {
        var payload = PayloadPath(journal.Id);
        var source = Path.Combine(_source, journal.Name);
        var payloadMatches = await MatchesPathAsync(payload, journal, token);
        var sourceMatches = await MatchesPathAsync(source, journal, token);
        if (payloadMatches && !sourceMatches) return "moved";
        if (sourceMatches && !System.IO.File.Exists(payload)) return "not_moved";
        return "unknown";
    }
    private async ValueTask<string> VerifyRestoreOutcomeAsync(Journal journal, CancellationToken token)
    {
        var payloadMatches = await MatchesPathAsync(PayloadPath(journal.Id), journal, token);
        var sourceMatches = await MatchesPathAsync(Path.Combine(_source, journal.Name), journal, token);
        if (sourceMatches && !payloadMatches) return "restored";
        if (payloadMatches && !sourceMatches) return "not_restored";
        return "unknown";
    }
    private MaintenanceFreeSpaceReadback ReadFreeSpaceReadback()
    {
        try
        {
            var root = Path.GetPathRoot(_source);
            if (string.IsNullOrWhiteSpace(root)) return new(null, _clock.GetUtcNow());
            var availableBytes = _freeSpaceReader is null ? new DriveInfo(root).AvailableFreeSpace : _freeSpaceReader();
            if (availableBytes is null || availableBytes < 0) return new(null, _clock.GetUtcNow());
            return new(availableBytes, _clock.GetUtcNow());
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new(null, _clock.GetUtcNow());
        }
    }
    private static MaintenanceResult Result(string code) => new(code, 0, 0, 0);
    private string JournalPath(string id) => Path.Combine(_quarantine, id + ".json");
    private string PayloadPath(string id) => Path.Combine(_quarantine, id + ".payload");
}
