using System.Text.Json;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Infrastructure.Collectors;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class WindowsEventLogCollectorTests
{
    [Fact]
    public void Wer_parser_extracts_only_structured_bugcheck_code_parameters_and_safe_dump_filename()
    {
        var parser = typeof(WindowsEventLogCollector).Assembly
            .GetType("HostAgent.Infrastructure.Collectors.WindowsEventLogReaderFactory")!
            .GetMethod("ReadBugCheckData", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        Assert.NotNull(parser);
        object? Parse(string xml) => parser!.Invoke(null, [xml]);
        const string xml = "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Microsoft-Windows-WER-SystemErrorReporting'/><EventID>1001</EventID></System><EventData><Data Name='param1'>0x0000000a (0xffff9280d9724024, 0xff, 0x26, 0xfffff802bd4b77e0)</Data><Data Name='param2'>C:\\WINDOWS\\Minidump\\101226-15000-01.dmp</Data><Data Name='param3'>not-retained</Data></EventData></Event>";

        var parsed = Parse(xml);
        Assert.NotNull(parsed);
        Assert.Equal((uint)10, parsed.GetType().GetProperty("Code")!.GetValue(parsed));
        Assert.Equal(new ulong[] { 0xffff9280d9724024, 0xff, 0x26, 0xfffff802bd4b77e0 },
            Assert.IsAssignableFrom<IEnumerable<ulong>>(parsed.GetType().GetProperty("Parameters")!.GetValue(parsed)).ToArray());
        Assert.Equal("101226-15000-01.dmp", parsed.GetType().GetProperty("DumpFileName")!.GetValue(parsed));
        Assert.Null(Parse(xml.Replace("Microsoft-Windows-WER-SystemErrorReporting", "OtherProvider", StringComparison.Ordinal)));
        Assert.Null(Parse(xml.Replace("0x0000000a (0xffff9280d9724024, 0xff, 0x26, 0xfffff802bd4b77e0)", "not-a-bugcheck", StringComparison.Ordinal)));
        var unsafePath = Parse(xml.Replace("C:\\WINDOWS\\Minidump\\101226-15000-01.dmp", "C:\\Secrets\\token.dmp", StringComparison.Ordinal));
        Assert.NotNull(unsafePath);
        Assert.Null(unsafePath!.GetType().GetProperty("DumpFileName")!.GetValue(unsafePath));
        var nestedPath = Parse(xml.Replace("C:\\WINDOWS\\Minidump\\101226-15000-01.dmp", "C:\\Untrusted\\Windows\\Minidump\\101226-15000-01.dmp", StringComparison.Ordinal));
        Assert.NotNull(nestedPath);
        Assert.Null(nestedPath!.GetType().GetProperty("DumpFileName")!.GetValue(nestedPath));
    }

    [Fact]
    public void Scm_metadata_extracts_only_bounded_affected_service_from_structured_xml()
    {
        var parser = typeof(WindowsEventLogCollector).Assembly
            .GetType("HostAgent.Infrastructure.Collectors.WindowsEventLogReaderFactory")!
            .GetMethod("ReadAffectedServiceName", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        Assert.NotNull(parser);
        string? Parse(string xml) => (string?)parser!.Invoke(null, [xml]);
        Assert.Equal("Print Spooler", Parse("<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Service Control Manager'/><EventID>7031</EventID></System><EventData><Data Name='param1'>Print Spooler</Data><Data Name='param2'>1</Data></EventData></Event>"));
        Assert.Null(Parse("<Event><System><Provider Name='Other'/><EventID>7031</EventID></System><EventData><Data Name='param1'>secret</Data></EventData></Event>"));
        Assert.Null(Parse("malformed"));
        Assert.Null(Parse("<!DOCTYPE Event [<!ENTITY x SYSTEM 'file:///not-read'>]><Event>&x;</Event>"));
        Assert.Null(Parse("<Event><System><Provider Name='Service Control Manager'/><EventID>7034</EventID></System><EventData><Data Name='param1'>" + new string('a', 257) + "</Data></EventData></Event>"));
    }

    [Fact]
    public async Task Collected_bugcheck_metadata_preserves_only_parsed_parameters_and_dump_leaf()
    {
        var record = new EventLogRecordData(42, 2, 1001, "Microsoft-Windows-WER-SystemErrorReporting",
            DateTime.UtcNow, "host", "bookmark")
        {
            BugCheck = new ParsedBugCheckData(10, [0xffff9280d9724024, 0xff, 0x26, 0xfffff802bd4b77e0], "101226-15000-01.dmp")
        };

        var result = await new WindowsEventLogCollector(new MemoryCheckpointStore(), new SubjectReaderFactory(record))
            .CollectAsync(CancellationToken.None);

        var metadata = Assert.Single(result.Value!.Events);
        Assert.Equal((uint)10, metadata.BugCheckCode);
        Assert.Equal(new ulong[] { 0xffff9280d9724024, 0xff, 0x26, 0xfffff802bd4b77e0 }, metadata.BugCheckParameters);
        Assert.Equal("101226-15000-01.dmp", metadata.CrashDumpFileName);
    }

    [Fact]
    public async Task Scm_subject_and_event_digest_survive_collection()
    {
        var record = new EventLogRecordData(42, 2, 7031, "Service Control Manager", DateTime.UtcNow, "host", "bookmark", EventFingerprint: "digest");
        var name = typeof(EventLogRecordData).GetProperty("AffectedServiceName");
        Assert.NotNull(name);
        name!.SetValue(record, "Spooler");
        var result = await new WindowsEventLogCollector(new MemoryCheckpointStore(), new SubjectReaderFactory(record)).CollectAsync(CancellationToken.None);
        var metadata = Assert.Single(result.Value!.Events);
        Assert.Equal("Spooler", metadata.AffectedServiceName);
        Assert.Equal("digest", metadata.EventFingerprint);
    }

    private sealed class SubjectReaderFactory(EventLogRecordData record) : IEventLogReaderFactory
    {
        public IEventLogReader Create(string logName, string? bookmarkXml, string queryText, bool reverseDirection = false) =>
            new FakeReader(logName == "System" ? [record] : []);
    }

    [Fact]
    public async Task Legacy_raw_bookmark_xml_is_used_to_resume_the_event_log_query()
    {
        const string legacyBookmark = "<BookmarkList><Bookmark Channel=\"System\" RecordId=\"40\" /></BookmarkList>";
        var store = new MemoryCheckpointStore();
        store.Set("System", legacyBookmark);
        var readerFactory = new LegacyBookmarkReaderFactory(legacyBookmark);
        var collector = new WindowsEventLogCollector(store, readerFactory);

        var result = await collector.CollectAsync(CancellationToken.None);

        Assert.NotNull(result.Value);
        Assert.Equal(legacyBookmark, readerFactory.UsedBookmarkXml);
        Assert.Equal(41, Assert.Single(result.Value.Events).RecordId);
        Assert.DoesNotContain("event_log_system_checkpoint_invalid", result.Warnings);
    }

    [Fact]
    public async Task Truncated_legacy_bookmark_resume_keeps_the_record_id_cursor_for_the_next_cycle()
    {
        const string legacyBookmark = "<BookmarkList><Bookmark Channel=\"System\" RecordId=\"0\" /></BookmarkList>";
        var store = new MemoryCheckpointStore();
        store.Set("System", legacyBookmark);
        var collector = new WindowsEventLogCollector(store, new LegacyTruncatedReaderFactory(legacyBookmark));

        var firstBatch = await collector.CollectAsync(CancellationToken.None);

        Assert.NotNull(firstBatch.Value);
        Assert.Equal(WindowsEventLogCollector.MaximumEventsPerLog, firstBatch.Value.Events.Count);
        Assert.Contains("event_log_system_truncated", firstBatch.Warnings);
        var firstCheckpointValue = Assert.Single(
            firstBatch.Value.Checkpoints,
            checkpoint => checkpoint.Key == "System").Value;
        using (var checkpointJson = JsonDocument.Parse(firstCheckpointValue))
        {
            Assert.Equal(DateTimeOffset.MinValue, checkpointJson.RootElement.GetProperty("LastScannedAtUtc").GetDateTimeOffset());
            Assert.Equal(100, checkpointJson.RootElement.GetProperty("LastRecordId").GetInt64());
        }

        store.Set("System", firstCheckpointValue);
        var secondBatch = await collector.CollectAsync(CancellationToken.None);

        Assert.NotNull(secondBatch.Value);
        Assert.Equal(101, Assert.Single(secondBatch.Value.Events).RecordId);
    }

    [Fact]
    public async Task Truncated_recovery_then_invalid_bookmark_resumes_after_the_last_processed_record()
    {
        var previousScanUtc = DateTimeOffset.UtcNow.AddHours(-1);
        var store = new MemoryCheckpointStore();
        store.Set("System", JsonSerializer.Serialize(new
        {
            Version = 1,
            BookmarkXml = "invalid bookmark",
            LastScannedAtUtc = previousScanUtc,
            LastRecordId = 10L,
            LastRecordFingerprint = WindowsEventLogCollector.CreateRecordFingerprint(new EventLogRecordData(
                10,
                2,
                1010,
                "TestProvider",
                DateTime.UtcNow,
                "test-host",
                "bookmark-10"))
        }));
        var readerFactory = new RecoveryReaderFactory();
        var collector = new WindowsEventLogCollector(store, readerFactory);

        var firstBatch = await collector.CollectAsync(CancellationToken.None);

        Assert.NotNull(firstBatch.Value);
        Assert.Equal(WindowsEventLogCollector.MaximumEventsPerLog, firstBatch.Value.Events.Count);
        Assert.Equal(Enumerable.Range(11, 100).Select(id => (long?)id), firstBatch.Value.Events.Select(item => item.RecordId));
        Assert.Contains("event_log_system_truncated", firstBatch.Warnings);
        var firstCheckpointValue = Assert.Single(
            firstBatch.Value.Checkpoints,
            checkpoint => checkpoint.Key == "System").Value;
        using (var checkpointJson = JsonDocument.Parse(firstCheckpointValue))
        {
            Assert.Equal(previousScanUtc, checkpointJson.RootElement.GetProperty("LastScannedAtUtc").GetDateTimeOffset());
            Assert.Equal(110, checkpointJson.RootElement.GetProperty("LastRecordId").GetInt64());
        }

        store.Set("System", firstCheckpointValue);
        var secondBatch = await collector.CollectAsync(CancellationToken.None);

        Assert.NotNull(secondBatch.Value);
        Assert.Single(secondBatch.Value.Events);
        Assert.Equal(111, Assert.Single(secondBatch.Value.Events).RecordId);
    }

    [Fact]
    public void Record_id_fallback_query_uses_an_invariant_integer_watermark()
    {
        Assert.Equal(
            "*[System[(EventRecordID > 123456789) and (Level=1 or Level=2 or Level=3)]]",
            WindowsEventLogCollector.CreateAfterRecordIdQuery(123456789));
    }

    [Fact]
    public async Task Cleared_log_falls_back_to_time_without_filtering_new_lower_record_ids()
    {
        var previousScanUtc = DateTimeOffset.UtcNow.AddHours(-1);
        var store = new MemoryCheckpointStore();
        store.Set("System", JsonSerializer.Serialize(new
        {
            Version = 1,
            BookmarkXml = "invalid bookmark",
            LastScannedAtUtc = previousScanUtc,
            LastRecordId = 110L
        }));
        var collector = new WindowsEventLogCollector(store, new ClearedLogReaderFactory());

        var result = await collector.CollectAsync(CancellationToken.None);

        Assert.NotNull(result.Value);
        Assert.Equal(new long?[] { 1, 2, 3 }, result.Value.Events.Select(item => item.RecordId));
        var systemCheckpointValue = Assert.Single(
            result.Value.Checkpoints,
            checkpoint => checkpoint.Key == "System").Value;
        using var checkpointJson = JsonDocument.Parse(systemCheckpointValue);
        Assert.Equal(3, checkpointJson.RootElement.GetProperty("LastRecordId").GetInt64());
    }

    [Fact]
    public async Task Cleared_log_repopulated_past_watermark_is_detected_by_record_identity()
    {
        var previousScanUtc = DateTimeOffset.UtcNow.AddHours(-1);
        var previousRecord = new EventLogRecordData(
            10,
            2,
            1000,
            "OldProvider",
            DateTime.UtcNow.AddDays(-1),
            "test-host",
            "old-bookmark",
            500,
            8);
        var store = new MemoryCheckpointStore();
        store.Set("System", JsonSerializer.Serialize(new
        {
            Version = 1,
            BookmarkXml = "invalid bookmark",
            LastScannedAtUtc = previousScanUtc,
            LastRecordId = 10L,
            LastRecordFingerprint = WindowsEventLogCollector.CreateRecordFingerprint(previousRecord)
        }));
        var collector = new WindowsEventLogCollector(store, new RepopulatedClearedLogReaderFactory());

        var result = await collector.CollectAsync(CancellationToken.None);

        Assert.NotNull(result.Value);
        Assert.Equal(Enumerable.Range(1, 12).Select(id => (long?)id), result.Value.Events.Select(item => item.RecordId));
    }

    [Fact]
    public async Task Initial_checkpoint_persists_fingerprint_for_later_log_reset_detection()
    {
        var store = new MemoryCheckpointStore();
        var readerFactory = new InitialToResetReaderFactory();
        var collector = new WindowsEventLogCollector(store, readerFactory);

        var initial = await collector.CollectAsync(CancellationToken.None);

        Assert.NotNull(initial.Value);
        var checkpoint = Assert.Single(initial.Value.Checkpoints, item => item.Key == "System").Value;
        using (var checkpointJson = JsonDocument.Parse(checkpoint))
        {
            Assert.True(checkpointJson.RootElement.GetProperty("LastRecordId").GetInt64() > 0);
            Assert.False(string.IsNullOrWhiteSpace(checkpointJson.RootElement.GetProperty("LastRecordFingerprint").GetString()));
        }

        store.Set("System", checkpoint);
        readerFactory.SwitchToRepopulatedLog();
        var recovered = await collector.CollectAsync(CancellationToken.None);

        Assert.NotNull(recovered.Value);
        Assert.Equal(Enumerable.Range(1, 15).Select(id => (long?)id), recovered.Value.Events.Select(item => item.RecordId));
    }

    private sealed class MemoryCheckpointStore : ICollectorCheckpointStore
    {
        private readonly Dictionary<string, string> _checkpoints = new(StringComparer.Ordinal);

        public ValueTask<string?> ReadCollectorCheckpointAsync(
            string collectorId,
            string checkpointKey,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(_checkpoints.GetValueOrDefault(checkpointKey));

        public ValueTask SaveCollectorCheckpointAsync(
            string collectorId,
            string checkpointKey,
            string checkpointValue,
            DateTimeOffset updatedAtUtc,
            CancellationToken cancellationToken)
        {
            _checkpoints[checkpointKey] = checkpointValue;
            return ValueTask.CompletedTask;
        }

        public void Set(string key, string value) => _checkpoints[key] = value;
    }

    private sealed class RecoveryReaderFactory : IEventLogReaderFactory
    {
        private static readonly DateTime EventTimeUtc = DateTime.UtcNow;
        private static readonly IReadOnlyList<EventLogRecordData> RecoveryEvents = Enumerable
            .Range(11, 101)
            .Select(id => new EventLogRecordData(
                id,
                2,
                1000 + id,
                "TestProvider",
                EventTimeUtc,
                "test-host",
                $"bookmark-{id}"))
            .ToArray();

        public IEventLogReader Create(string logName, string? bookmarkXml, string queryText, bool reverseDirection = false)
        {
            if (logName != "System")
            {
                return new FakeReader([]);
            }

            if (reverseDirection)
            {
                return new FakeReader([RecoveryEvents[^1]]);
            }

            if (bookmarkXml is not null)
            {
                throw new ArgumentException("The bookmark was invalid.");
            }

            var marker = "EventRecordID > ";
            var markerIndex = queryText.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return new FakeReader([]);
            }

            var valueStart = markerIndex + marker.Length;
            var valueEnd = queryText.IndexOf(')', valueStart);
            var recordId = long.Parse(queryText.AsSpan(valueStart, valueEnd - valueStart), System.Globalization.CultureInfo.InvariantCulture);
            return new FakeReader(RecoveryEvents.Where(record => record.RecordId > recordId).ToArray());
        }
    }

    private sealed class FakeReader(IReadOnlyList<EventLogRecordData> records) : IEventLogReader
    {
        private int _index;

        public EventLogRecordData? ReadEvent() => _index < records.Count ? records[_index++] : null;

        public void Dispose()
        {
        }
    }

    private sealed class LegacyBookmarkReaderFactory(string legacyBookmark) : IEventLogReaderFactory
    {
        public string? UsedBookmarkXml { get; private set; }

        public IEventLogReader Create(string logName, string? bookmarkXml, string queryText, bool reverseDirection = false)
        {
            if (logName != "System")
            {
                return new FakeReader([]);
            }

            if (bookmarkXml is not null)
            {
                UsedBookmarkXml = bookmarkXml;
                if (bookmarkXml == legacyBookmark)
                {
                    return new FakeReader([
                        new EventLogRecordData(41, 2, 2041, "TestProvider", DateTime.UtcNow, "test-host", "bookmark-41")]);
                }

                throw new ArgumentException("The bookmark is not valid.");
            }

            return new FakeReader([]);
        }
    }

    private sealed class LegacyTruncatedReaderFactory(string legacyBookmark) : IEventLogReaderFactory
    {
        private static readonly IReadOnlyList<EventLogRecordData> Records = Enumerable
            .Range(1, 101)
            .Select(id => new EventLogRecordData(
                id,
                2,
                3000 + id,
                "TestProvider",
                DateTime.UtcNow,
                "test-host",
                $"legacy-bookmark-{id}"))
            .ToArray();

        public IEventLogReader Create(string logName, string? bookmarkXml, string queryText, bool reverseDirection = false)
        {
            if (logName != "System")
            {
                return new FakeReader([]);
            }

            if (reverseDirection)
            {
                return new FakeReader([Records[^1]]);
            }

            if (bookmarkXml is not null)
            {
                if (bookmarkXml == legacyBookmark)
                {
                    return new FakeReader(Records);
                }

                throw new ArgumentException("The upgraded checkpoint bookmark is invalidated for fallback coverage.");
            }

            const string marker = "EventRecordID > ";
            var markerIndex = queryText.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return new FakeReader([]);
            }

            var valueStart = markerIndex + marker.Length;
            var valueEnd = queryText.IndexOf(')', valueStart);
            var recordId = long.Parse(queryText.AsSpan(valueStart, valueEnd - valueStart), System.Globalization.CultureInfo.InvariantCulture);
            return new FakeReader(Records.Where(record => record.RecordId > recordId).ToArray());
        }
    }

    private sealed class ClearedLogReaderFactory : IEventLogReaderFactory
    {
        private static readonly IReadOnlyList<EventLogRecordData> CurrentRecords = Enumerable
            .Range(1, 3)
            .Select(id => new EventLogRecordData(
                id,
                2,
                2000 + id,
                "TestProvider",
                DateTime.UtcNow,
                "test-host",
                $"cleared-bookmark-{id}"))
            .ToArray();

        public IEventLogReader Create(string logName, string? bookmarkXml, string queryText, bool reverseDirection = false)
        {
            if (logName != "System")
            {
                return new FakeReader([]);
            }

            if (reverseDirection)
            {
                return new FakeReader([CurrentRecords[^1]]);
            }

            if (bookmarkXml is not null)
            {
                throw new ArgumentException("The bookmark was invalid after the log was cleared.");
            }

            return queryText.Contains("TimeCreated", StringComparison.Ordinal)
                ? new FakeReader(CurrentRecords)
                : new FakeReader([]);
        }
    }

    private sealed class RepopulatedClearedLogReaderFactory : IEventLogReaderFactory
    {
        private static readonly IReadOnlyList<EventLogRecordData> CurrentRecords = Enumerable
            .Range(1, 12)
            .Select(id => new EventLogRecordData(
                id,
                2,
                4000 + id,
                "NewProvider",
                DateTime.UtcNow,
                "test-host",
                $"new-bookmark-{id}",
                600,
                9))
            .ToArray();

        public IEventLogReader Create(string logName, string? bookmarkXml, string queryText, bool reverseDirection = false)
        {
            if (logName != "System")
            {
                return new FakeReader([]);
            }

            if (reverseDirection)
            {
                return new FakeReader([CurrentRecords[^1]]);
            }

            if (bookmarkXml is not null)
            {
                throw new ArgumentException("The bookmark was invalid after the channel was cleared.");
            }

            if (queryText.Contains("EventRecordID = 10", StringComparison.Ordinal))
            {
                return new FakeReader([CurrentRecords[9]]);
            }

            return queryText.Contains("TimeCreated", StringComparison.Ordinal)
                ? new FakeReader(CurrentRecords)
                : new FakeReader([]);
        }
    }

    private sealed class InitialToResetReaderFactory : IEventLogReaderFactory
    {
        private static readonly IReadOnlyList<EventLogRecordData> InitialRecords = Enumerable
            .Range(1, 12)
            .Select(id => new EventLogRecordData(
                id,
                2,
                5000 + id,
                "OriginalProvider",
                DateTime.UtcNow.AddMinutes(-1),
                "test-host",
                $"original-bookmark-{id}",
                700,
                10))
            .ToArray();
        private bool _repopulatedLog;

        public void SwitchToRepopulatedLog() => _repopulatedLog = true;

        public IEventLogReader Create(string logName, string? bookmarkXml, string queryText, bool reverseDirection = false)
        {
            if (logName != "System")
            {
                return new FakeReader([]);
            }

            if (!_repopulatedLog)
            {
                return new FakeReader(InitialRecords);
            }

            var currentRecords = Enumerable
                .Range(1, 15)
                .Select(id => new EventLogRecordData(
                    id,
                    2,
                    6000 + id,
                    "NewProvider",
                    DateTime.UtcNow.AddSeconds(1),
                    "test-host",
                    $"reset-bookmark-{id}",
                    800,
                    11))
                .ToArray();

            if (reverseDirection)
            {
                return new FakeReader([currentRecords[^1]]);
            }

            if (bookmarkXml is not null)
            {
                return new FakeReader(currentRecords.Where(record => record.RecordId > 12).ToArray());
            }

            if (queryText.Contains("EventRecordID = 12", StringComparison.Ordinal))
            {
                return new FakeReader([currentRecords[11]]);
            }

            return queryText.Contains("TimeCreated", StringComparison.Ordinal)
                ? new FakeReader(currentRecords)
                : new FakeReader([]);
        }
    }
}
