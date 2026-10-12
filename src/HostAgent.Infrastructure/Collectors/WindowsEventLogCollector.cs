using System.Globalization;
using System.Diagnostics.Eventing.Reader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Infrastructure.Collectors;

public sealed class WindowsEventLogCollector : ITelemetryCollector<EventLogTelemetry>
{
    public const int MaximumEvents = 200;
    public const int MaximumEventsPerLog = 100;
    private static readonly TimeSpan InitialScanWindow = TimeSpan.FromMinutes(10);
    private static readonly string InitialEventQuery =
        $"*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= {(long)InitialScanWindow.TotalMilliseconds}]]]";
    private const string CatchUpEventQuery = "*[System[(Level=1 or Level=2 or Level=3)]]";
    private readonly ICollectorCheckpointStore _checkpointStore;
    private readonly IEventLogReaderFactory _readerFactory;

    public WindowsEventLogCollector(ICollectorCheckpointStore checkpointStore)
        : this(checkpointStore, new WindowsEventLogReaderFactory())
    {
    }

    internal WindowsEventLogCollector(
        ICollectorCheckpointStore checkpointStore,
        IEventLogReaderFactory readerFactory)
    {
        ArgumentNullException.ThrowIfNull(checkpointStore);
        ArgumentNullException.ThrowIfNull(readerFactory);
        _checkpointStore = checkpointStore;
        _readerFactory = readerFactory;
    }

    public string CollectorId => "event-log";

    public async ValueTask<CollectorResult<EventLogTelemetry>> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var events = new List<EventLogEntryMetadata>(MaximumEvents);
        var checkpoints = new List<CollectorCheckpoint>(2);
        var warnings = new List<string>();
        var logsRead = 0;

        foreach (var logName in new[] { "System", "Application" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (events.Count >= MaximumEvents)
            {
                break;
            }

            try
            {
                var checkpoint = await _checkpointStore.ReadCollectorCheckpointAsync(
                    CollectorId,
                    logName,
                    cancellationToken);
                var nextCheckpoint = ReadLog(logName, checkpoint, now, events, warnings, cancellationToken);
                if (nextCheckpoint is not null)
                {
                    checkpoints.Add(new CollectorCheckpoint(logName, nextCheckpoint));
                }

                logsRead++;
            }
            catch (UnauthorizedAccessException)
            {
                warnings.Add($"event_log_{logName.ToLowerInvariant()}_access_denied");
            }
            catch (EventLogException)
            {
                warnings.Add($"event_log_{logName.ToLowerInvariant()}_unavailable");
            }
            catch (InvalidOperationException)
            {
                warnings.Add($"event_log_{logName.ToLowerInvariant()}_unavailable");
            }
        }

        if (logsRead == 0)
        {
            return CollectorResult<EventLogTelemetry>.Unavailable(CollectorId, now, "event_logs_unavailable");
        }

        var telemetry = new EventLogTelemetry(now, events.AsReadOnly(), checkpoints.AsReadOnly());
        return warnings.Count == 0
            ? CollectorResult<EventLogTelemetry>.Success(CollectorId, now, telemetry)
            : CollectorResult<EventLogTelemetry>.Partial(CollectorId, now, telemetry, warnings);
    }

    private string? ReadLog(
        string logName,
        string? checkpointValue,
        DateTimeOffset scannedAtUtc,
        ICollection<EventLogEntryMetadata> events,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        EventLogCheckpoint? checkpoint = null;
        if (checkpointValue is not null)
        {
            try
            {
                checkpoint = JsonSerializer.Deserialize<EventLogCheckpoint>(checkpointValue);
                if (checkpoint is null || checkpoint.Version != EventLogCheckpoint.CurrentVersion
                    || (checkpoint.LastScannedAtUtc == default
                        && checkpoint.LastRecordId is null
                        && string.IsNullOrWhiteSpace(checkpoint.BookmarkXml))
                    || checkpoint.LastRecordId is <= 0)
                {
                    checkpoint = null;
                    warnings.Add($"event_log_{logName.ToLowerInvariant()}_checkpoint_invalid");
                }
            }
            catch (JsonException)
            {
                if (IsLegacyBookmark(checkpointValue))
                {
                    checkpoint = new EventLogCheckpoint(
                        EventLogCheckpoint.CurrentVersion,
                        checkpointValue,
                        DateTimeOffset.MinValue);
                }
                else
                {
                    warnings.Add($"event_log_{logName.ToLowerInvariant()}_checkpoint_invalid");
                }
            }
        }

        var logResetDetected = checkpoint?.LastRecordId is not null
            && checkpoint.LastRecordFingerprint is { Length: > 0 }
            && WasLogCleared(logName, checkpoint, cancellationToken);
        if (logResetDetected && checkpoint is not null)
        {
            warnings.Add($"event_log_{logName.ToLowerInvariant()}_reset_detected");
            return ReadSinceCheckpoint(
                logName,
                checkpoint,
                scannedAtUtc,
                events,
                warnings,
                cancellationToken,
                logStateChecked: true,
                logResetDetected: true);
        }

        if (checkpoint?.BookmarkXml is { Length: > 0 } bookmarkXml)
        {
            var bookmarkEvents = new List<EventLogEntryMetadata>();
            var bookmarkWarnings = new List<string>();
            try
            {
                var read = ReadFromReader(
                    logName,
                    bookmarkXml,
                    CatchUpEventQuery,
                    bookmarkEvents,
                    bookmarkWarnings,
                    cancellationToken,
                    checkpoint.LastRecordId);
                AddResults(bookmarkEvents, bookmarkWarnings, events, warnings);
                return SerializeCheckpoint(new EventLogCheckpoint(
                    EventLogCheckpoint.CurrentVersion,
                    read.BookmarkXml ?? bookmarkXml,
                    read.Truncated ? checkpoint.LastScannedAtUtc : scannedAtUtc,
                    read.LastRecordId ?? checkpoint.LastRecordId,
                    read.LastRecordFingerprint ?? checkpoint.LastRecordFingerprint));
            }
            catch (EventLogException)
            {
                warnings.Add($"event_log_{logName.ToLowerInvariant()}_bookmark_invalid");
            }
            catch (ArgumentException)
            {
                warnings.Add($"event_log_{logName.ToLowerInvariant()}_bookmark_invalid");
            }

            return ReadSinceCheckpoint(
                logName,
                checkpoint,
                scannedAtUtc,
                events,
                warnings,
                cancellationToken,
                logStateChecked: true);
        }

        if (checkpoint is not null)
        {
            return ReadSinceCheckpoint(
                logName,
                checkpoint,
                scannedAtUtc,
                events,
                warnings,
                cancellationToken,
                logStateChecked: true);
        }

        var initialEvents = new List<EventLogEntryMetadata>();
        var initialWarnings = new List<string>();
        var initialScanStart = scannedAtUtc - InitialScanWindow;
        var initialRead = ReadFromReader(
            logName,
            null,
            InitialEventQuery,
            initialEvents,
            initialWarnings,
            cancellationToken);
        AddResults(initialEvents, initialWarnings, events, warnings);
        return SerializeCheckpoint(new EventLogCheckpoint(
            EventLogCheckpoint.CurrentVersion,
            initialRead.BookmarkXml,
            initialRead.Truncated ? initialScanStart : scannedAtUtc,
            initialRead.LastRecordId,
            initialRead.LastRecordFingerprint));
    }

    private string ReadSinceCheckpoint(
        string logName,
        EventLogCheckpoint checkpoint,
        DateTimeOffset scannedAtUtc,
        ICollection<EventLogEntryMetadata> events,
        ICollection<string> warnings,
        CancellationToken cancellationToken,
        bool logStateChecked = false,
        bool logResetDetected = false)
    {
        var cursorRecordId = checkpoint.LastRecordFingerprint is { Length: > 0 }
            ? checkpoint.LastRecordId
            : null;
        if (cursorRecordId is not null
            && (logResetDetected || (!logStateChecked && WasLogCleared(logName, checkpoint, cancellationToken))))
        {
            cursorRecordId = null;
        }

        var cursorQuery = cursorRecordId is { } recordId
            ? CreateAfterRecordIdQuery(recordId)
            : checkpoint.LastScannedAtUtc == DateTimeOffset.MinValue
                ? CatchUpEventQuery
                : CreateSinceQuery(checkpoint.LastScannedAtUtc);

        var recoveryEvents = new List<EventLogEntryMetadata>();
        var recoveryWarnings = new List<string>();
        var recoveryRead = ReadFromReader(
            logName,
            null,
            cursorQuery,
            recoveryEvents,
            recoveryWarnings,
            cancellationToken,
            cursorRecordId,
            cursorRecordId is null ? null : checkpoint.LastRecordFingerprint);
        AddResults(recoveryEvents, recoveryWarnings, events, warnings);
        return SerializeCheckpoint(new EventLogCheckpoint(
            EventLogCheckpoint.CurrentVersion,
            recoveryRead.BookmarkXml,
            recoveryRead.Truncated ? checkpoint.LastScannedAtUtc : scannedAtUtc,
            recoveryRead.LastRecordId ?? cursorRecordId,
            recoveryRead.LastRecordFingerprint ?? (cursorRecordId is null ? null : checkpoint.LastRecordFingerprint)));
    }

    private bool WasLogCleared(
        string logName,
        EventLogCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var lastRecordId = checkpoint.LastRecordId!.Value;
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = _readerFactory.Create(logName, null, "*", reverseDirection: true);
        var newestRecord = reader.ReadEvent();
        if (newestRecord?.RecordId is not { } newestRecordId || newestRecordId < lastRecordId)
        {
            return true;
        }

        if (checkpoint.LastRecordFingerprint is not { Length: > 0 } lastRecordFingerprint)
        {
            return false;
        }

        using var watermarkReader = _readerFactory.Create(
            logName,
            null,
            CreateRecordIdQuery(lastRecordId));
        var watermarkRecord = watermarkReader.ReadEvent();
        return watermarkRecord is { RecordId: { } watermarkRecordId }
            && watermarkRecordId == lastRecordId
            && !string.Equals(CreateRecordFingerprint(watermarkRecord), lastRecordFingerprint, StringComparison.Ordinal);
    }

    internal static string CreateRecordIdQuery(long recordId) =>
        $"*[System[EventRecordID = {recordId.ToString(CultureInfo.InvariantCulture)}]]";

    internal static string CreateAfterRecordIdQuery(long recordId) =>
        $"*[System[(EventRecordID > {recordId.ToString(CultureInfo.InvariantCulture)}) and (Level=1 or Level=2 or Level=3)]]";

    internal static string CreateRecordFingerprint(EventLogRecordData record)
    {
        if (record.EventFingerprint is { Length: > 0 } eventFingerprint)
        {
            return eventFingerprint;
        }

        var occurredAtUtcTicks = record.TimeCreated is { } timeCreated
            ? timeCreated.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        var identity = string.Join('\n',
            record.Level?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record.EventId.ToString(CultureInfo.InvariantCulture),
            record.ProviderName ?? string.Empty,
            occurredAtUtcTicks,
            record.MachineName ?? string.Empty,
            record.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record.ThreadId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private static string CreateSinceQuery(DateTimeOffset timestampUtc)
    {
        var timestamp = timestampUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        return $"*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[@SystemTime >= '{timestamp}']]]";
    }

    private static bool IsLegacyBookmark(string bookmarkXml)
    {
        const long MaximumBookmarkCharacters = 16_384;
        if (bookmarkXml.Length == 0 || bookmarkXml.Length > MaximumBookmarkCharacters)
        {
            return false;
        }

        try
        {
            using var textReader = new StringReader(bookmarkXml);
            using var xmlReader = XmlReader.Create(
                textReader,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaximumBookmarkCharacters
                });
            return xmlReader.MoveToContent() == XmlNodeType.Element
                && xmlReader.LocalName == "BookmarkList";
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static string SerializeCheckpoint(EventLogCheckpoint checkpoint) =>
        JsonSerializer.Serialize(checkpoint);

    private static void AddResults(
        IEnumerable<EventLogEntryMetadata> pendingEvents,
        IEnumerable<string> pendingWarnings,
        ICollection<EventLogEntryMetadata> events,
        ICollection<string> warnings)
    {
        foreach (var entry in pendingEvents)
        {
            events.Add(entry);
        }

        foreach (var warning in pendingWarnings)
        {
            warnings.Add(warning);
        }
    }

    private EventLogReadResult ReadFromReader(
        string logName,
        string? checkpoint,
        string queryText,
        ICollection<EventLogEntryMetadata> events,
        ICollection<string> warnings,
        CancellationToken cancellationToken,
        long? previousRecordId = null,
        string? previousRecordFingerprint = null)
    {
        using var reader = _readerFactory.Create(logName, checkpoint, queryText);
        string? nextCheckpoint = null;
        var latestRecordId = previousRecordId;
        var latestRecordFingerprint = previousRecordFingerprint;
        var readCount = 0;
        var truncated = false;

        while (readCount < MaximumEventsPerLog && events.Count < MaximumEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = reader.ReadEvent();
            if (record is null)
            {
                break;
            }

            readCount++;
            nextCheckpoint = record.BookmarkXml ?? nextCheckpoint;
            if (record.RecordId is { } recordId)
            {
                var priorRecordId = latestRecordId;
                latestRecordId = priorRecordId is { } previous
                    ? Math.Max(previous, recordId)
                    : recordId;
                if (priorRecordId is { } watermark && recordId <= watermark)
                {
                    continue;
                }

                latestRecordFingerprint = CreateRecordFingerprint(record);
            }

            var level = record.Level switch
            {
                1 => "Critical",
                2 => "Error",
                3 => "Warning",
                _ => null
            };
            if (level is null)
            {
                warnings.Add("unexpected_event_log_level");
                continue;
            }

            try
            {
                events.Add(new EventLogEntryMetadata(
                    logName,
                    record.ProviderName ?? "unknown",
                    record.RecordId,
                    record.EventId,
                    level,
                    record.TimeCreated is { } created
                        ? new DateTimeOffset(created.ToUniversalTime(), TimeSpan.Zero)
                        : null,
                    record.MachineName ?? Environment.MachineName)
                {
                    AffectedServiceName = record.AffectedServiceName,
                    EventFingerprint = record.EventFingerprint,
                    BugCheckCode = record.BugCheck?.Code,
                    BugCheckParameters = record.BugCheck?.Parameters ?? [],
                    CrashDumpFileName = record.BugCheck?.DumpFileName
                });
            }
            catch (InvalidOperationException)
            {
                warnings.Add("malformed_event_log_record");
            }
        }

        if (readCount == MaximumEventsPerLog || events.Count >= MaximumEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            truncated = reader.ReadEvent() is not null;
            if (truncated)
            {
                warnings.Add($"event_log_{logName.ToLowerInvariant()}_truncated");
            }
        }

        return new EventLogReadResult(nextCheckpoint, latestRecordId, latestRecordFingerprint, truncated);
    }

    private sealed record EventLogCheckpoint(
        int Version,
        string? BookmarkXml,
        DateTimeOffset LastScannedAtUtc,
        long? LastRecordId = null,
        string? LastRecordFingerprint = null)
    {
        public const int CurrentVersion = 1;
    }

    private sealed record EventLogReadResult(
        string? BookmarkXml,
        long? LastRecordId,
        string? LastRecordFingerprint,
        bool Truncated);
}
