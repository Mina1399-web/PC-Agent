using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Infrastructure.Collectors;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class EventLogBootstrapTests
{
    [Fact]
    public async Task First_scan_uses_valid_Windows_query_and_reads_available_Wer_metadata()
    {
        var result = await new WindowsEventLogCollector(new Checkpoints()).CollectAsync(default);
        Assert.NotNull(result.Value);
        Assert.DoesNotContain("event_log_system_unavailable", result.Warnings);
        Assert.DoesNotContain("event_log_system_access_denied", result.Warnings);
        var lower = result.SampledAtUtc.AddHours(-24).UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        var upper = result.SampledAtUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        using var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(
            new System.Diagnostics.Eventing.Reader.EventLogQuery("System", System.Diagnostics.Eventing.Reader.PathType.LogName,
                $"*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001 and (Level=1 or Level=2) and TimeCreated[@SystemTime >= '{lower}' and @SystemTime <= '{upper}']]]"));
        var expected = new List<long?>();
        while (true)
        {
            using var item = reader.ReadEvent();
            if (item is null) break;
            expected.Add(item.RecordId);
        }
        if (!result.Warnings.Contains("event_log_system_truncated"))
            Assert.All(expected, id => Assert.Contains(result.Value.Events, item => item.LogName == "System" && item.RecordId == id));
        Assert.InRange(result.Value.Events.Count, 0, WindowsEventLogCollector.MaximumEvents);
    }

    [Theory]
    [InlineData(6600000000L, true)] // 11 minutes
    [InlineData(864000000000L, true)] // exactly 24 hours
    [InlineData(864000000001L, false)]
    [InlineData(-1L, false)] // future
    public async Task First_scan_includes_only_timestamped_Wer_inside_24_hours(long ageTicks, bool expected)
    {
        var factory = new QueryReaderFactory(now => [Wer(42, now.AddTicks(-ageTicks))]);
        var result = await new WindowsEventLogCollector(new Checkpoints(), factory).CollectAsync(default);

        Assert.Equal(expected ? 1 : 0, result.Value!.Events.Count);
        if (expected)
        {
            var item = Assert.Single(result.Value.Events);
            Assert.Equal(42, item.RecordId);
            Assert.Equal((uint)10, item.BugCheckCode);
            Assert.Equal(4, item.BugCheckParameters.Count);
            Assert.DoesNotContain("raw-sensitive-payload", JsonSerializer.Serialize(result));
        }
    }

    [Fact]
    public async Task First_scan_does_not_backfill_unrelated_old_events_or_undated_Wer()
    {
        var factory = new QueryReaderFactory(now =>
        [
            Wer(1, now.AddMinutes(-11)),
            Wer(2, null),
            new(3, 2, 999, "OtherProvider", now.AddMinutes(-11).UtcDateTime, "host", "bm-3"),
            new(4, 2, 999, "OtherProvider", now.AddMinutes(-1).UtcDateTime, "host", "bm-4")
        ]);
        var result = await new WindowsEventLogCollector(new Checkpoints(), factory).CollectAsync(default);
        Assert.Equal(new long?[] { 1, 4 }, result.Value!.Events.Select(item => item.RecordId));
    }

    [Fact]
    public async Task Saved_checkpoint_does_not_repeat_backfilled_Wer_and_admits_new_event()
    {
        var checkpoints = new Checkpoints();
        var factory = new QueryReaderFactory(now => [Wer(42, now.AddMinutes(-11)),
            new(43, 2, 999, "OtherProvider", now.AddMinutes(-10.5).UtcDateTime, "host", "bm-43")]);
        var collector = new WindowsEventLogCollector(checkpoints, factory);
        var first = await collector.CollectAsync(default);
        Assert.Equal(42, Assert.Single(first.Value!.Events).RecordId);
        checkpoints.Save(first.Value);

        Assert.Empty((await collector.CollectAsync(default)).Value!.Events);
        factory.Records.Add(Wer(44, DateTimeOffset.UtcNow));
        Assert.Equal(44, Assert.Single((await collector.CollectAsync(default)).Value!.Events).RecordId);
    }

    [Fact]
    public async Task Truncated_bootstrap_resumes_its_filter_after_restart_without_bulk_old_logs()
    {
        var checkpoints = new Checkpoints();
        var factory = new QueryReaderFactory(now => Enumerable.Range(1, 202).Select(id =>
            id % 2 == 1 ? Wer(id, now.AddHours(-1))
                : new EventLogRecordData(id, 2, 999, "OtherProvider", now.AddHours(-1).UtcDateTime, "host", $"bm-{id}")).ToArray());
        var first = await new WindowsEventLogCollector(checkpoints, factory).CollectAsync(default);
        Assert.Equal(100, first.Value!.Events.Count);
        checkpoints.Save(first.Value);

        var second = await new WindowsEventLogCollector(checkpoints, factory).CollectAsync(default);
        Assert.Equal(201, Assert.Single(second.Value!.Events).RecordId);
        Assert.All(first.Value.Events.Concat(second.Value.Events), item => Assert.Equal(1001, item.EventId));
        checkpoints.Save(second.Value);
        Assert.Empty((await new WindowsEventLogCollector(checkpoints, factory).CollectAsync(default)).Value!.Events);
    }

    // A stale bootstrap RecordID must not discard the new log's lower IDs after a reset.
    [Fact]
    public async Task Reset_during_truncated_bootstrap_reanchors_and_reads_new_lower_record_ids()
    {
        var checkpoints = new Checkpoints();
        var factory = new QueryReaderFactory(now => Enumerable.Range(1, 101)
            .Select(id => Wer(id, now.AddHours(-1))).ToArray());
        var first = await new WindowsEventLogCollector(checkpoints, factory).CollectAsync(default);
        Assert.Equal(100, first.Value!.Events.Count);
        Assert.Contains("event_log_system_truncated", first.Warnings);
        // Model a 30-hour pause with a durable unfinished bootstrap. Retaining its
        // old anchor after reset would wrongly include the unrelated hour-old record.
        var pending = JsonNode.Parse(Assert.Single(first.Value.Checkpoints, c => c.Key == "System").Value)!;
        var oldAnchor = DateTimeOffset.UtcNow.AddHours(-30);
        pending["InitialScanAtUtc"] = JsonValue.Create(oldAnchor);
        pending["LastScannedAtUtc"] = JsonValue.Create(oldAnchor.AddHours(-24));
        await checkpoints.SaveCollectorCheckpointAsync("event-log", "System", pending.ToJsonString(), oldAnchor, default);

        factory.Records.Clear();
        factory.Records.Add(Wer(1, DateTimeOffset.UtcNow.AddMinutes(-11)));
        factory.Records.Add(new(2, 2, 999, "OtherProvider", DateTime.UtcNow.AddHours(-1), "host", "bm-2"));
        factory.Records.Add(Wer(3, DateTimeOffset.UtcNow.AddHours(-25)));
        var reset = await new WindowsEventLogCollector(checkpoints, factory).CollectAsync(default);
        Assert.Equal(1, Assert.Single(reset.Value!.Events).RecordId);
        checkpoints.Save(reset.Value);
        Assert.Empty((await new WindowsEventLogCollector(checkpoints, factory).CollectAsync(default)).Value!.Events);
        factory.Records.Add(Wer(4, DateTimeOffset.UtcNow));
        Assert.Equal(4, Assert.Single((await new WindowsEventLogCollector(checkpoints, factory).CollectAsync(default)).Value!.Events).RecordId);
    }

    private static EventLogRecordData Wer(long id, DateTimeOffset? time) =>
        new(id, 2, 1001, "Microsoft-Windows-WER-SystemErrorReporting", time?.UtcDateTime, "host", $"bm-{id}")
        { BugCheck = new(10, [1, 2, 3, 4], "101226-15000-01.dmp") };

    private sealed class Checkpoints : ICollectorCheckpointStore
    {
        private readonly Dictionary<string, string> _values = new();
        public ValueTask<string?> ReadCollectorCheckpointAsync(string collector, string key, CancellationToken token) =>
            ValueTask.FromResult(_values.GetValueOrDefault(key));
        public ValueTask SaveCollectorCheckpointAsync(string collector, string key, string value, DateTimeOffset now, CancellationToken token)
        { _values[key] = value; return ValueTask.CompletedTask; }
        public void Save(EventLogTelemetry telemetry)
        { foreach (var item in telemetry.Checkpoints) _values[item.Key] = item.Value; }
    }

    // External Event Log boundary: use XPath for provider/ID/level predicates and
    // translate only Windows-specific timestamp comparisons before evaluating it.
    private sealed class QueryReaderFactory(Func<DateTimeOffset, IReadOnlyList<EventLogRecordData>> create) : IEventLogReaderFactory
    {
        private DateTimeOffset _now;
        private bool _initialized;
        public List<EventLogRecordData> Records { get; } = [];
        public IEventLogReader Create(string log, string? bookmark, string query, bool reverseDirection = false)
        {
            if (log != "System") return new Reader([]);
            if (!_initialized)
            {
                var upper = Regex.Match(query, "@SystemTime <= '([^']+)'");
                _now = upper.Success ? DateTimeOffset.Parse(upper.Groups[1].Value, CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow;
                Records.AddRange(create(_now));
                _initialized = true;
            }
            var cursor = bookmark is null ? 0 : long.Parse(bookmark.AsSpan(3), CultureInfo.InvariantCulture);
            var selected = Records.Where(item => item.RecordId >= cursor && Matches(item, query));
            if (reverseDirection) selected = selected.Reverse();
            return new Reader(selected.ToArray());
        }
        private bool Matches(EventLogRecordData item, string query)
        {
            var translated = Regex.Replace(query, "timediff\\(@SystemTime\\) <= ([0-9]+)", match =>
                item.TimeCreated is { } time && (_now - new DateTimeOffset(time)).TotalMilliseconds <= long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                    ? "true()" : "false()");
            translated = Regex.Replace(translated, "@SystemTime (>=|<=) '([^']+)'", match =>
            {
                if (item.TimeCreated is not { } time) return "false()";
                var boundary = DateTimeOffset.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                var value = new DateTimeOffset(time);
                return (match.Groups[1].Value == ">=" ? value >= boundary : value <= boundary) ? "true()" : "false()";
            });
            var document = new XDocument(new XElement("Event", new XElement("System",
                new XElement("Provider", new XAttribute("Name", item.ProviderName!)),
                new XElement("EventID", item.EventId), new XElement("Level", item.Level),
                new XElement("EventRecordID", item.RecordId),
                item.TimeCreated is null ? null : new XElement("TimeCreated", new XAttribute("SystemTime", item.TimeCreated.Value.ToString("o"))))));
            return document.XPathSelectElement(translated) is not null;
        }
    }
    private sealed class Reader(IReadOnlyList<EventLogRecordData> records) : IEventLogReader
    {
        private int _index;
        public EventLogRecordData? ReadEvent() => _index < records.Count ? records[_index++] : null;
        public void Dispose() { }
    }
}
