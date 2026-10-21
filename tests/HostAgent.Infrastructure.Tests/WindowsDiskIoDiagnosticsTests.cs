using System.Text.Json;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Infrastructure.Collectors;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class WindowsDiskIoDiagnosticsTests
{
    // Successful native status alone must not imply a usable finite value.
    // Record a boolean only where API status and CStatus make the value defined.
    [Theory]
    [InlineData(0u, 0u, 12d, true)]
    [InlineData(0u, 1u, double.NaN, false)]
    [InlineData(0u, 0u, double.PositiveInfinity, false)]
    [InlineData(5u, 0u, 12d, null)]
    [InlineData(0xC0000BC6u, 0x800007D1u, 12d, null)]
    [InlineData(0u, 0xC0000BBAu, 12d, null)]
    public async Task Diagnostic_finiteness_flag_never_interprets_undefined_native_values(
        uint apiStatus, uint dataStatus, double value, bool? expectedFinite)
    {
        var native = new FakeNative();
        native.FormatStatus[1] = apiStatus;
        native.DataStatus[1] = dataStatus;
        native.Values[1] = value;

        var result = await new WindowsDiskIoCounterReader(native).ReadAsync(default);

        var diagnostic = Assert.Single(result.Diagnostics, row => row.Stage == DiskIoDiagnosticStage.FormatValue
            && row.Counter == DiskIoDiagnosticCounter.Read);
        var serialized = JsonSerializer.SerializeToElement(diagnostic);
        Assert.True(serialized.TryGetProperty("ValueFinite", out var finite),
            "The diagnostic discarded whether a defined native value was finite.");
        if (expectedFinite.HasValue) Assert.Equal(expectedFinite.Value, finite.GetBoolean());
        else Assert.Equal(JsonValueKind.Null, finite.ValueKind);
    }

    // Finite seconds can overflow during conversion to milliseconds. Such a
    // value must not turn an otherwise valid collection into unserializable JSON.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Latency_unit_conversion_never_emits_infinity(bool onlyLatencyCounter)
    {
        var native = new FakeNative();
        native.Values[3] = double.MaxValue;
        if (onlyLatencyCounter) native.AddStatus[1] = native.AddStatus[2] = 5;

        var result = await new WindowsDiskIoCounterReader(native).ReadAsync(default);

        Assert.Null(result.AverageLatencyMilliseconds);
        Assert.Equal(!onlyLatencyCounter, result.IsAvailable);
        Assert.Equal(onlyLatencyCounter ? "disk_io_values_unavailable" : "some_disk_io_counters_unavailable", result.UnavailableReason);
        _ = JsonSerializer.Serialize(result);
        Assert.Equal(1, native.CloseCalls);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    public async Task Successful_native_reads_preserve_valid_and_new_data_and_all_ten_fixed_stages(uint dataStatus)
    {
        var native = new FakeNative();
        for (var counter = 1; counter <= 3; counter++) native.DataStatus[counter] = dataStatus;
        var result = await new WindowsDiskIoCounterReader(native).ReadAsync(default);

        Assert.True(result.IsAvailable);
        Assert.Null(result.UnavailableReason);
        Assert.Equal(12, result.ReadBytesPerSecond);
        Assert.Equal(0, result.WriteBytesPerSecond); // Existing negative-value clamp.
        Assert.Equal(2, result.AverageLatencyMilliseconds);
        Assert.Equal(new[] { DiskIoDiagnosticStage.OpenQuery, DiskIoDiagnosticStage.AddCounter,
            DiskIoDiagnosticStage.AddCounter, DiskIoDiagnosticStage.AddCounter, DiskIoDiagnosticStage.FirstSample,
            DiskIoDiagnosticStage.SecondSample, DiskIoDiagnosticStage.FormatValue, DiskIoDiagnosticStage.FormatValue,
            DiskIoDiagnosticStage.FormatValue, DiskIoDiagnosticStage.CloseQuery }, result.Diagnostics.Select(row => row.Stage));
        Assert.Equal(new[] { DiskIoDiagnosticCounter.Read, DiskIoDiagnosticCounter.Write, DiskIoDiagnosticCounter.Latency },
            result.Diagnostics.Where(row => row.Stage == DiskIoDiagnosticStage.FormatValue).Select(row => row.Counter));
        Assert.All(result.Diagnostics, row => Assert.Equal(0u, row.Status));
        Assert.All(result.Diagnostics.Where(row => row.Stage == DiskIoDiagnosticStage.FormatValue),
            row => Assert.Equal(dataStatus, row.DataStatus));
        Assert.All(result.Diagnostics.Where(row => row.Stage != DiskIoDiagnosticStage.FormatValue),
            row => Assert.Null(row.DataStatus));
        Assert.Equal(1, native.CloseCalls);
        Assert.Equal(2, native.CollectCalls);
        Assert.Equal(3, native.FormatCalls);
    }

    // PDH_INVALID_DATA is an API failure with a documented meaningful CStatus;
    // other failing return codes must not expose undefined output-buffer bits.
    [Theory]
    [InlineData(5u, 0u, null)]
    [InlineData(0xC0000BC6u, 0x800007D1u, 0x800007D1u)]
    [InlineData(0u, 0xC0000BBAu, 0xC0000BBAu)]
    public async Task Formatted_API_status_and_data_status_are_independent_and_do_not_make_invalid_values_usable(
        uint apiStatus, uint dataStatus, uint? expectedDataStatus)
    {
        var native = new FakeNative();
        native.FormatStatus[1] = apiStatus;
        native.DataStatus[1] = dataStatus;

        var result = await new WindowsDiskIoCounterReader(native).ReadAsync(default);

        Assert.True(result.IsAvailable);
        Assert.Equal("some_disk_io_counters_unavailable", result.UnavailableReason);
        Assert.Null(result.ReadBytesPerSecond);
        Assert.Equal(0, result.WriteBytesPerSecond);
        var read = Assert.Single(result.Diagnostics, row => row.Stage == DiskIoDiagnosticStage.FormatValue
            && row.Counter == DiskIoDiagnosticCounter.Read);
        Assert.Equal(apiStatus, read.Status);
        Assert.Equal(expectedDataStatus, read.DataStatus);
        Assert.Equal(1, native.CloseCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Sampling_failure_records_which_sample_failed_and_keeps_original_unavailable_reason(int sample)
    {
        var native = new FakeNative { FailedSample = sample };

        var result = await new WindowsDiskIoCounterReader(native).ReadAsync(default);

        Assert.False(result.IsAvailable);
        Assert.Equal("disk_io_sample_unavailable", result.UnavailableReason);
        var failed = Assert.Single(result.Diagnostics, row => row.Status == 0x800007D5u);
        Assert.Equal(sample == 1 ? DiskIoDiagnosticStage.FirstSample : DiskIoDiagnosticStage.SecondSample, failed.Stage);
        Assert.Equal(DiskIoDiagnosticCounter.None, failed.Counter);
        Assert.Null(failed.DataStatus);
        Assert.Equal(sample, native.CollectCalls);
        Assert.Equal(0, native.FormatCalls);
        Assert.Equal(1, native.CloseCalls);
        Assert.Equal(DiskIoDiagnosticStage.CloseQuery, result.Diagnostics[^1].Stage);
    }

    [Fact]
    public async Task All_add_counter_failures_preserve_each_fixed_counter_status_and_close_the_open_query()
    {
        var native = new FakeNative();
        for (var counter = 1; counter <= 3; counter++) native.AddStatus[counter] = 5;

        var result = await new WindowsDiskIoCounterReader(native).ReadAsync(default);

        Assert.False(result.IsAvailable);
        Assert.Equal("disk_io_counters_unavailable", result.UnavailableReason);
        Assert.Equal(5, result.Diagnostics.Count);
        Assert.Equal(new[] { DiskIoDiagnosticCounter.Read, DiskIoDiagnosticCounter.Write, DiskIoDiagnosticCounter.Latency },
            result.Diagnostics.Where(row => row.Stage == DiskIoDiagnosticStage.AddCounter).Select(row => row.Counter));
        Assert.All(result.Diagnostics.Where(row => row.Stage == DiskIoDiagnosticStage.AddCounter), row => Assert.Equal(5u, row.Status));
        Assert.Equal(0, native.CollectCalls);
        Assert.Equal(0, native.FormatCalls);
        Assert.Equal(1, native.CloseCalls);
    }

    [Fact]
    public async Task Failed_open_has_one_precise_status_and_does_not_close_an_unopened_query()
    {
        var native = new FakeNative { OpenStatus = 5 };

        var result = await new WindowsDiskIoCounterReader(native).ReadAsync(default);

        Assert.False(result.IsAvailable);
        Assert.Equal("disk_io_query_unavailable", result.UnavailableReason);
        Assert.Equal(new(DiskIoDiagnosticStage.OpenQuery, DiskIoDiagnosticCounter.None, 5, null), Assert.Single(result.Diagnostics));
        Assert.Equal(0, native.AddCalls);
        Assert.Equal(0, native.CloseCalls);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task Nonfinite_native_value_remains_partial_and_never_enters_persisted_diagnostic_metadata(double value)
    {
        var native = new FakeNative();
        native.Values[1] = value;

        var result = await new WindowsDiskIoCounterReader(native).ReadAsync(default);

        Assert.True(result.IsAvailable);
        Assert.Equal("some_disk_io_counters_unavailable", result.UnavailableReason);
        Assert.Null(result.ReadBytesPerSecond);
        var diagnostic = Assert.Single(result.Diagnostics, row => row.Stage == DiskIoDiagnosticStage.FormatValue
            && row.Counter == DiskIoDiagnosticCounter.Read);
        Assert.Equal(0u, diagnostic.Status);
        Assert.Equal(0u, diagnostic.DataStatus);
        _ = JsonSerializer.Serialize(result); // Non-finite values would fail default serialization.
        Assert.Equal(1, native.CloseCalls);
    }

    [Fact]
    public async Task All_invalid_values_remain_unavailable_with_each_counter_data_status_recorded()
    {
        var native = new FakeNative();
        for (var counter = 1; counter <= 3; counter++) native.DataStatus[counter] = 0xC0000BBA;

        var result = await new WindowsDiskIoCounterReader(native).ReadAsync(default);

        Assert.False(result.IsAvailable);
        Assert.Equal("disk_io_values_unavailable", result.UnavailableReason);
        Assert.Null(result.ReadBytesPerSecond);
        Assert.Null(result.WriteBytesPerSecond);
        Assert.Null(result.AverageLatencyMilliseconds);
        Assert.Equal(3, result.Diagnostics.Count(row => row.Stage == DiskIoDiagnosticStage.FormatValue
            && row.Status == 0 && row.DataStatus == 0xC0000BBA));
        Assert.Equal(1, native.CloseCalls);
    }

    [Fact]
    public async Task Cancellation_during_sampling_closes_only_the_open_query_and_never_formats()
    {
        using var cancellation = new CancellationTokenSource();
        var native = new FakeNative { CancelAfterFirstSample = cancellation };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WindowsDiskIoCounterReader(native).ReadAsync(cancellation.Token).AsTask());

        Assert.Equal(1, native.CollectCalls);
        Assert.Equal(0, native.FormatCalls);
        Assert.Equal(1, native.CloseCalls);
    }

    [Fact]
    public async Task Already_cancelled_read_does_not_call_native_API()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var native = new FakeNative();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WindowsDiskIoCounterReader(native).ReadAsync(cancellation.Token).AsTask());

        Assert.Equal(0, native.OpenCalls);
        Assert.Equal(0, native.CloseCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_native_API_preserves_original_reason_without_inventing_a_native_status(bool missingLibrary)
    {
        var native = new FakeNative { OpenException = missingLibrary ? new DllNotFoundException() : new EntryPointNotFoundException() };

        var result = await new WindowsDiskIoCounterReader(native).ReadAsync(default);

        Assert.False(result.IsAvailable);
        Assert.Equal("disk_io_api_unavailable", result.UnavailableReason);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, native.CloseCalls);
    }

    [Fact]
    public void Earlier_schema_one_disk_telemetry_without_diagnostics_remains_readable()
    {
        var earlier = """{"IsAvailable":false,"ReadBytesPerSecond":null,"WriteBytesPerSecond":null,"AverageLatencyMilliseconds":null,"UnavailableReason":"disk_io_counters_unavailable"}""";

        var value = JsonSerializer.Deserialize<DiskIoTelemetry>(earlier);

        Assert.NotNull(value);
        Assert.False(value.IsAvailable);
        Assert.Equal("disk_io_counters_unavailable", value.UnavailableReason);
        Assert.Empty(value.Diagnostics);
    }

    // A Running service currently loses the PDH stage/status that distinguishes
    // denied counters, sampling failure and invalid formatted data. Exercise the
    // real reader and its persisted result, without relying on new API symbols.
    [Fact]
    public async Task Actual_disk_read_retains_only_bounded_native_stage_counter_and_status_evidence()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await new WindowsDiskIoCounterReader().ReadAsync(deadline.Token);
        var persisted = JsonSerializer.SerializeToElement(result);

        Assert.True(persisted.TryGetProperty("Diagnostics", out var diagnostics),
            "The real disk read discarded its bounded native PDH diagnostics.");
        Assert.Equal(JsonValueKind.Array, diagnostics.ValueKind);
        Assert.InRange(diagnostics.GetArrayLength(), 1, 10);
        var records = diagnostics.EnumerateArray().ToArray();
        Assert.Equal(0, records[0].GetProperty("Stage").GetInt32()); // OpenQuery
        Assert.Equal(0, records[0].GetProperty("Counter").GetInt32()); // None
        Assert.All(records, record =>
        {
            Assert.Equal(new[] { "Counter", "DataStatus", "Stage", "Status", "ValueFinite" },
                record.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
            Assert.InRange(record.GetProperty("Stage").GetInt32(), 0, 5);
            Assert.InRange(record.GetProperty("Counter").GetInt32(), 0, 3);
            Assert.True(record.GetProperty("Status").TryGetUInt32(out _));
            var dataStatus = record.GetProperty("DataStatus");
            Assert.True(dataStatus.ValueKind == JsonValueKind.Null || dataStatus.TryGetUInt32(out _));
        });
        if (records[0].GetProperty("Status").GetUInt32() == 0)
        {
            Assert.Equal(5, records[^1].GetProperty("Stage").GetInt32()); // CloseQuery
            Assert.Equal(0, records[^1].GetProperty("Counter").GetInt32());
            Assert.Equal(new[] { 1, 2, 3 }, records.Where(record => record.GetProperty("Stage").GetInt32() == 1)
                .Select(record => record.GetProperty("Counter").GetInt32()));
        }
    }

    private sealed class FakeNative : IDiskIoNativeApi
    {
        internal uint OpenStatus { get; init; }
        internal Exception? OpenException { get; init; }
        internal int FailedSample { get; init; }
        internal CancellationTokenSource? CancelAfterFirstSample { get; init; }
        internal uint[] AddStatus { get; } = new uint[4];
        internal uint[] FormatStatus { get; } = new uint[4];
        internal uint[] DataStatus { get; } = new uint[4];
        internal double[] Values { get; } = [0, 12, -1, 0.002];
        internal int OpenCalls { get; private set; }
        internal int AddCalls { get; private set; }
        internal int CollectCalls { get; private set; }
        internal int FormatCalls { get; private set; }
        internal int CloseCalls { get; private set; }
        public uint OpenQuery(out IntPtr query)
        {
            OpenCalls++;
            if (OpenException is not null) throw OpenException;
            query = OpenStatus == 0 ? new IntPtr(77) : IntPtr.Zero;
            return OpenStatus;
        }
        public uint AddCounter(IntPtr query, DiskIoDiagnosticCounter counter, out IntPtr handle)
        {
            Assert.Equal(new IntPtr(77), query);
            Assert.InRange((int)counter, 1, 3);
            AddCalls++;
            var code = AddStatus[(int)counter];
            handle = code == 0 ? new IntPtr((int)counter) : IntPtr.Zero;
            return code;
        }
        public uint CollectQueryData(IntPtr query)
        {
            Assert.Equal(new IntPtr(77), query);
            CollectCalls++;
            if (CollectCalls == 1) CancelAfterFirstSample?.Cancel();
            return CollectCalls == FailedSample ? 0x800007D5u : 0;
        }
        public uint ReadFormattedValue(IntPtr counter, out uint dataStatus, out double value)
        {
            var id = counter.ToInt32();
            Assert.InRange(id, 1, 3);
            FormatCalls++;
            dataStatus = DataStatus[id];
            value = Values[id];
            return FormatStatus[id];
        }
        public uint CloseQuery(IntPtr query)
        {
            Assert.Equal(new IntPtr(77), query);
            CloseCalls++;
            return 0;
        }
    }
}
