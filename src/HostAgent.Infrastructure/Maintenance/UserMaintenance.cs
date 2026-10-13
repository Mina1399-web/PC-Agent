using HostAgent.Core.Dashboard;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace HostAgent.Infrastructure.Maintenance;

public sealed record MaintenanceItem(string Id, string Name, long Bytes, DateTimeOffset ModifiedAtUtc, string Sha256);
public sealed record MaintenancePreview(string Id, DateTimeOffset ExpiresAtUtc, IReadOnlyList<MaintenanceItem> Items, int Skipped, bool Truncated)
{ public string Code { get; init; } = "preview_ready"; }
public sealed record QuarantineItem(string Id, string Name, long Bytes, DateTimeOffset CreatedAtUtc, string State);
public sealed record MaintenanceResult(string Code, int Moved, int Skipped, long Bytes, int JournalWarnings = 0);

public sealed class UserMaintenance
{
    private readonly string _source, _quarantine;
    private readonly TimeProvider _clock;
    private readonly Func<CancellationToken, ValueTask<DashboardSnapshot?>> _stateReader;
    private readonly Action<string>? _afterDirectoryCreate;
    private readonly Action<string>? _afterPrivateAcl;
    private readonly Action? _beforeJournalHash;
    private readonly Action? _beforeCandidateHash;
    private readonly Func<string, bool>? _rejectJournalState;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Plan? _plan;
    private sealed record Candidate(MaintenanceItem Item, FileProof Proof);
    private sealed record Plan(MaintenancePreview Preview, DateTimeOffset CreatedAtUtc, Candidate[] Candidates);
    private sealed record Journal(string Id, string Name, long Bytes, DateTimeOffset ModifiedAtUtc,
        string Sha256, string Identity, DateTimeOffset CreatedAtUtc, string State);
    private sealed record JournalInventory(Journal[] Entries, bool HasInvalidEntries, long PayloadBytes = 0);

    internal UserMaintenance(string source, string quarantine, TimeProvider clock,
        Func<CancellationToken, ValueTask<DashboardSnapshot?>> stateReader, Action<string>? afterDirectoryCreate = null,
        Func<string, bool>? rejectJournalState = null, Action<string>? afterPrivateAcl = null, Action? beforeJournalHash = null,
        Action? beforeCandidateHash = null)
    {
        _source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        _quarantine = Path.GetFullPath(quarantine).TrimEnd(Path.DirectorySeparatorChar);
        _clock = clock; _stateReader = stateReader; _afterDirectoryCreate = afterDirectoryCreate;
        _rejectJournalState = rejectJournalState; _afterPrivateAcl = afterPrivateAcl; _beforeJournalHash = beforeJournalHash;
        _beforeCandidateHash = beforeCandidateHash;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var temp = Path.Combine(local, "Temp");
        if (!MaintenanceSecurity.NormalUser || !Within(_source, temp) || !Within(_quarantine, local)
            || Within(_quarantine, _source) || Within(_source, _quarantine)) throw new UnauthorizedAccessException("maintenance_scope");
    }

    public static UserMaintenance CreateForCurrentUser(Func<CancellationToken, ValueTask<DashboardSnapshot?>> stateReader)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new UserMaintenance(Path.Combine(local, "Temp"), Path.Combine(local, "HostAgent", "Quarantine"), TimeProvider.System, stateReader);
    }

    private static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public async ValueTask<MaintenancePreview> PreviewAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            _plan = null;
            var now = _clock.GetUtcNow();
            var candidates = new List<Candidate>(); var skipped = 0; var inspected = 0; long bytes = 0; var truncated = false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                using var source = PinnedDirectory.Open(_source, true);
                foreach (var path in Directory.EnumerateFiles(_source, "*", SearchOption.TopDirectoryOnly))
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    if (++inspected > 500 || candidates.Count == 100) { truncated = true; break; }
                    var name = Path.GetFileName(path);
                    try
                    {
                        if (!MaintenanceSecurity.SafeName(name)) { skipped++; continue; }
                        using var file = PinnedFile.Open(path);
                        if (now - file.Info.ModifiedAtUtc < TimeSpan.FromDays(7) || file.Info.Bytes > 16 * 1024 * 1024 || bytes + file.Info.Bytes > 128 * 1024 * 1024) { skipped++; continue; }
                        var proof = await file.ProofAsync(deadline.Token);
                        var item = new MaintenanceItem(Guid.NewGuid().ToString("N"), name, proof.Bytes, proof.ModifiedAtUtc, proof.Sha256);
                        candidates.Add(new Candidate(item, proof)); bytes += proof.Bytes;
                    }
                    catch (Exception error) when (MaintenanceSecurity.Expected(error)) { skipped++; }
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { truncated = true; }
            catch (Exception error) when (MaintenanceSecurity.Expected(error))
            {
                return new MaintenancePreview(Guid.NewGuid().ToString("D"), now, [], skipped, false) { Code = "maintenance_root_unavailable" };
            }
            var preview = new MaintenancePreview(Guid.NewGuid().ToString("D"), now.AddMinutes(10), Array.AsReadOnly(candidates.Select(x => x.Item).ToArray()), skipped, truncated);
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
            if (_clock.GetUtcNow() > plan.Preview.ExpiresAtUtc) return Result("maintenance_preview_expired");
            if (!await ApprovedAsync(token)) return Result("maintenance_not_approved");
            if (plan.Candidates.Length == 0) return Result("quarantine_empty");

            try { EnsurePrivateRoot(); }
            catch (Exception error) when (MaintenanceSecurity.Expected(error)) { return Result("maintenance_root_unavailable"); }
            using var source = PinnedDirectory.Open(_source, true);
            using var quarantine = PinnedDirectory.Open(_quarantine, true);
            using var mutationLock = MaintenanceMutationLock.TryAcquire(_quarantine);
            if (mutationLock is null) return Result("maintenance_busy");
            if (source.Volume != quarantine.Volume) return Result("maintenance_volume_mismatch");
            var inventory = ReadJournals();
            if (inventory.HasInvalidEntries) return Result("maintenance_journal_integrity");
            var existing = inventory.Entries;
            if (existing.Length >= 256 || inventory.PayloadBytes > 128L * 1024 * 1024
                || existing.Sum(item => item.State is "Quarantined" or "Intent" or "RestoreIntent" ? item.Bytes : 0) > 128L * 1024 * 1024)
                return Result("maintenance_quota_reached");

            var moved = 0; var skipped = 0; var journalWarnings = 0; long bytes = 0;
            var reservedJournals = existing.Length;
            var reservedBytes = Math.Max(inventory.PayloadBytes,
                existing.Sum(item => item.State is "Quarantined" or "Intent" or "RestoreIntent" ? item.Bytes : 0));
            foreach (var candidate in plan.Candidates)
            {
                token.ThrowIfCancellationRequested();
                if (!await ApprovedAsync(token)) { skipped += plan.Candidates.Length - moved - skipped; return new("maintenance_not_approved", moved, skipped, bytes, journalWarnings); }
                var journal = new Journal(candidate.Item.Id, candidate.Item.Name, candidate.Proof.Bytes,
                    candidate.Proof.ModifiedAtUtc, candidate.Proof.Sha256, candidate.Proof.Identity,
                    _clock.GetUtcNow(), "Intent");
                var payloadPath = PayloadPath(journal.Id);
                try
                {
                    using var file = PinnedFile.Open(Path.Combine(_source, candidate.Item.Name), true);
                    if (file.Info.Identity != candidate.Proof.Identity || file.Info.Bytes != candidate.Proof.Bytes
                        || file.Info.ModifiedAtUtc != candidate.Proof.ModifiedAtUtc) { skipped++; continue; }
                    _beforeCandidateHash?.Invoke();
                    var proof = await file.ProofAsync(token);
                    if (!Same(proof, candidate.Proof)) { skipped++; continue; }
                    if (System.IO.File.Exists(payloadPath) || System.IO.File.Exists(JournalPath(journal.Id))) { skipped++; continue; }
                    if (reservedJournals >= 256 || journal.Bytes > 128L * 1024 * 1024 - reservedBytes) { skipped++; continue; }
                    WriteJournal(journal); // Durable intent precedes the same-volume handle rename.
                    reservedJournals++; reservedBytes += journal.Bytes;
                    MaintenanceNative.Rename(file.Handle, payloadPath);
                    moved++; bytes += journal.Bytes;
                    journal = journal with { State = "Quarantined" };
                    try { WriteJournal(journal); }
                    catch (Exception error) when (MaintenanceSecurity.Expected(error)) { journalWarnings++; }
                }
                catch (Exception error) when (MaintenanceSecurity.Expected(error)) { skipped++; }
            }
            var code = journalWarnings > 0 ? "quarantine_recovery_required"
                : moved > 0 && skipped == 0 ? "quarantine_complete"
                : moved > 0 ? "quarantine_partial" : "quarantine_no_files_moved";
            return new(code, moved, skipped, bytes, journalWarnings);
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
            var inventory = ReadJournals();
            if (inventory.HasInvalidEntries) return Result("maintenance_journal_integrity");
            var journal = inventory.Entries.FirstOrDefault(item => item.Id == entryId);
            if (journal is null || journal.State is not ("Quarantined" or "Intent" or "RestoreIntent")) return Result("maintenance_entry_unavailable");
            if (!await ApprovedAsync(token)) return Result("maintenance_not_approved");
            using var source = PinnedDirectory.Open(_source, true);
            using var quarantine = PinnedDirectory.Open(_quarantine, true);
            using var mutationLock = MaintenanceMutationLock.TryAcquire(_quarantine);
            if (mutationLock is null) return Result("maintenance_busy");
            if (source.Volume != quarantine.Volume) return Result("maintenance_volume_mismatch");
            var original = Path.Combine(_source, journal.Name);
            if (System.IO.File.Exists(original) || Directory.Exists(original)) return Result("restore_conflict");
            PinnedFile payload;
            try { payload = PinnedFile.Open(PayloadPath(journal.Id), true); }
            catch (Exception error) when (MaintenanceSecurity.Expected(error)) { return Result("maintenance_payload_changed"); }
            using (payload)
            {
            if (!await MatchesJournalAsync(payload, journal, token)) return Result("maintenance_payload_changed");
            if (!await ApprovedAsync(token)) return Result("maintenance_not_approved");
            try { WriteJournal(journal with { State = "RestoreIntent" }); }
            catch (Exception error) when (MaintenanceSecurity.Expected(error)) { return Result("maintenance_journal_write_failed"); }
            try { MaintenanceNative.Rename(payload.Handle, original); }
            catch (Exception error) when (MaintenanceSecurity.Expected(error)) { return Result("restore_failed"); }
            try { WriteJournal(journal with { State = "Restored" }); }
            catch (Exception error) when (MaintenanceSecurity.Expected(error))
            { return new("restore_recovery_required", 1, 0, journal.Bytes, 1); }
            return new("restore_complete", 1, 0, journal.Bytes);
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
                    payloadBytes = file.Info.Bytes > 128L * 1024 * 1024 - payloadBytes
                        ? 128L * 1024 * 1024 + 1 : payloadBytes + file.Info.Bytes;
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

    private static bool ValidJournal(Journal? journal) => journal is not null && !string.IsNullOrEmpty(journal.Id)
        && !string.IsNullOrEmpty(journal.Name) && !string.IsNullOrEmpty(journal.Sha256) && !string.IsNullOrEmpty(journal.Identity)
        && !string.IsNullOrEmpty(journal.State) && Guid.TryParseExact(journal.Id, "N", out _) && MaintenanceSecurity.SafeName(journal.Name)
        && journal.Bytes is >= 0 and <= 16 * 1024 * 1024 && journal.Sha256.Length == 64 && journal.Sha256.All(Uri.IsHexDigit)
        && journal.Identity.Length == 25 && journal.Identity[8] == ':'
        && journal.Identity.Where((_, index) => index != 8).All(Uri.IsHexDigit)
        && journal.State is "Intent" or "Quarantined" or "RestoreIntent" or "Restored";

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
    private static MaintenanceResult Result(string code) => new(code, 0, 0, 0);
    private string JournalPath(string id) => Path.Combine(_quarantine, id + ".json");
    private string PayloadPath(string id) => Path.Combine(_quarantine, id + ".payload");
}
