using System.Runtime.InteropServices;
using HostAgent.Infrastructure.Collectors;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class ProcessCounterReaderTests
{
    // Defect: four wildcard arrays can enumerate instances in different orders.
    // A positional join assigns one process another process's CPU or memory.
    [Fact]
    public void Read_joins_raw_arrays_by_exact_instance_name()
    {
        var native = FakeNative.WithTwoProcesses();
        var before = DateTimeOffset.UtcNow;

        var samples = new WindowsProcessCounterReader(native).Read(default);
        var after = DateTimeOffset.UtcNow;

        Assert.True(samples.ContainsKey(123), "The joined PID 123 counter row was missing.");
        Assert.True(samples.ContainsKey(456), "The joined PID 456 counter row was missing.");
        var first = samples[123];
        Assert.Equal(123, first.ProcessId);
        Assert.Equal(TimeSpan.FromTicks(123_456_789), first.CpuTime);
        Assert.Equal(1_572_864, first.MemoryBytes);
        Assert.Equal(900_000_001, first.LifetimeMarker);
        Assert.Equal("service_123", first.InstanceName);
        Assert.InRange(first.CapturedAtUtc, before, after);
        var second = samples[456];
        Assert.Equal(456, second.ProcessId);
        Assert.Equal(TimeSpan.FromTicks(987_654_321), second.CpuTime);
        Assert.Equal(8_388_608, second.MemoryBytes);
        Assert.Equal(1_234_567_890, second.LifetimeMarker);
        Assert.Equal("worker_456", second.InstanceName);
        Assert.Equal(first.CapturedAtUtc, second.CapturedAtUtc);
        Assert.Equal(new[]
        {
            ProcessCounterKind.ProcessId,
            ProcessCounterKind.CpuTime,
            ProcessCounterKind.Lifetime,
            ProcessCounterKind.WorkingSet
        }, native.AddedCounters);
        Assert.Equal(1, native.CollectCalls);
        Assert.Equal(4, native.ReadKinds.Distinct().Count());
        Assert.Equal(1, native.CloseCalls);
    }

    // Defect: one malformed or overlong instance name must not discard other
    // complete counter rows in the same bounded native array.
    [Fact]
    public void Read_omits_an_overlong_instance_and_keeps_other_complete_rows()
    {
        var longName = new string('x', 257);
        var native = FakeNative.WithRows(new()
        {
            [ProcessCounterKind.ProcessId] = [new(longName, 0, 999), new("good_123", 1, 123)],
            [ProcessCounterKind.CpuTime] = [new(longName, 0, 9999), new("good_123", 0, 432_100)],
            [ProcessCounterKind.Lifetime] = [new(longName, 0, 888), new("good_123", 1, 765_432)],
            [ProcessCounterKind.WorkingSet] = [new(longName, 0, 1234), new("good_123", 0, 65_536)]
        });

        var samples = new WindowsProcessCounterReader(native).Read(default);

        Assert.Single(samples);
        Assert.Equal(123, samples[123].ProcessId);
        Assert.Equal("good_123", samples[123].InstanceName);
        Assert.Equal(TimeSpan.FromTicks(432_100), samples[123].CpuTime);
        Assert.Equal(65_536, samples[123].MemoryBytes);
    }

    [Fact]
    public void Read_omits_rows_with_invalid_status_missing_counters_or_duplicate_process_ids()
    {
        var native = FakeNative.WithRows(new()
        {
            [ProcessCounterKind.ProcessId] = [new("ok_1", 0, 1), new("bad_status", 7, 2), new("dup_a", 0, 3), new("dup_b", 0, 3), new("missing", 0, 4)],
            [ProcessCounterKind.CpuTime] = [new("ok_1", 0, 10), new("bad_status", 0, 20), new("dup_a", 0, 30), new("dup_b", 0, 40)],
            [ProcessCounterKind.Lifetime] = [new("ok_1", 0, 11), new("bad_status", 0, 21), new("dup_a", 0, 31), new("dup_b", 0, 41), new("missing", 0, 51)],
            [ProcessCounterKind.WorkingSet] = [new("ok_1", 0, 12), new("bad_status", 0, 22), new("dup_a", 0, 32), new("dup_b", 0, 42), new("missing", 0, 52)]
        });

        var samples = new WindowsProcessCounterReader(native).Read(default);

        Assert.Equal([1], samples.Keys);
        Assert.Equal(1, native.CloseCalls);
    }

    [Theory]
    [InlineData((int)ProcessCounterKind.CpuTime, 0x00010000)]
    [InlineData((int)ProcessCounterKind.Lifetime, 0x20510500)]
    [InlineData((int)ProcessCounterKind.WorkingSet, 0x00010000)]
    public void Read_returns_empty_without_arrays_when_counter_type_is_unsupported(int counter, uint type)
    {
        var native = FakeNative.WithTwoProcesses();
        native.CounterTypeOverrides[(ProcessCounterKind)counter] = type;

        var samples = new WindowsProcessCounterReader(native).Read(default);

        Assert.Empty(samples);
        Assert.Empty(native.ReadKinds);
        Assert.Equal(1, native.CloseCalls);
    }

    [Fact]
    public void Read_returns_empty_when_cpu_timebase_is_unsupported()
    {
        var native = FakeNative.WithTwoProcesses();
        native.CpuTimeBase = 1_000_000;

        var samples = new WindowsProcessCounterReader(native).Read(default);

        Assert.Empty(samples);
        Assert.Empty(native.ReadKinds);
        Assert.Equal(1, native.CloseCalls);
    }

    [Fact]
    public void Read_bounds_more_data_retries_and_closes_query_when_cancelled()
    {
        var native = FakeNative.WithTwoProcesses();
        native.MoreDataFillResponses = 3;
        var samples = new WindowsProcessCounterReader(native).Read(default);
        Assert.Empty(samples);
        Assert.Equal(3, native.FillCalls);
        Assert.Equal(1, native.CloseCalls);

        native = FakeNative.WithTwoProcesses();
        using var cancellation = new CancellationTokenSource();
        native.CancelDuringArrayRead = cancellation;
        Assert.Throws<OperationCanceledException>(() => new WindowsProcessCounterReader(native).Read(cancellation.Token));
        Assert.Equal(1, native.CloseCalls);
    }

    [Fact]
    public void Read_returns_empty_when_query_close_fails()
    {
        var native = FakeNative.WithTwoProcesses();
        native.CloseStatus = 5;

        var samples = new WindowsProcessCounterReader(native).Read(default);

        Assert.Empty(samples);
        Assert.Equal(1, native.CloseCalls);
    }

    [Fact]
    public void Read_uses_ordinal_instance_matching_and_discards_duplicate_instance_rows()
    {
        var native = FakeNative.WithRows(new()
        {
            [ProcessCounterKind.ProcessId] = [new("service_1", 0, 1), new("dup_2", 0, 2), new("dup_2", 0, 22)],
            [ProcessCounterKind.CpuTime] = [new("SERVICE_1", 0, 10), new("dup_2", 0, 20)],
            [ProcessCounterKind.Lifetime] = [new("service_1", 0, 11), new("dup_2", 0, 21)],
            [ProcessCounterKind.WorkingSet] = [new("service_1", 0, 12), new("dup_2", 0, 22)]
        });

        var samples = new WindowsProcessCounterReader(native).Read(default);

        Assert.Empty(samples);
        Assert.Equal(1, native.CloseCalls);
    }

    [Fact]
    public void Read_returns_empty_when_collection_fails()
    {
        var native = FakeNative.WithTwoProcesses();
        native.CollectStatus = 5;

        var samples = new WindowsProcessCounterReader(native).Read(default);

        Assert.Empty(samples);
        Assert.Equal(1, native.CollectCalls);
        Assert.Empty(native.ReadKinds);
        Assert.Equal(1, native.CloseCalls);
    }

    [Fact]
    public void Read_rejects_native_array_size_above_limit_before_allocating()
    {
        var native = FakeNative.WithTwoProcesses();
        native.ReportOversizedArray = true;

        var samples = new WindowsProcessCounterReader(native).Read(default);

        Assert.Empty(samples);
        Assert.Equal(ProcessCounterKind.ProcessId, Assert.Single(native.ReadKinds));
        Assert.Equal(0, native.FillCalls);
        Assert.Equal(1, native.CloseCalls);
    }

    private sealed class FakeNative : IProcessCounterNativeApi
    {
        private const uint MoreData = 0x800007D2;
        private static readonly IntPtr QueryHandle = new(77);
        private readonly Dictionary<ProcessCounterKind, ProcessCounterRawRow[]> _rows;
        private readonly Dictionary<IntPtr, ProcessCounterKind> _counterKinds = [];

        private FakeNative(Dictionary<ProcessCounterKind, ProcessCounterRawRow[]> rows) => _rows = rows;

        internal static FakeNative WithRows(Dictionary<ProcessCounterKind, ProcessCounterRawRow[]> rows) => new(rows);

        internal List<ProcessCounterKind> AddedCounters { get; } = [];
        internal List<ProcessCounterKind> ReadKinds { get; } = [];
        internal int CollectCalls { get; private set; }
        internal int CloseCalls { get; private set; }
        internal Dictionary<ProcessCounterKind, uint> CounterTypeOverrides { get; } = [];
        internal long CpuTimeBase { get; set; } = 10_000_000;
        internal uint CloseStatus { get; set; }
        internal uint CollectStatus { get; set; }
        internal bool ReportOversizedArray { get; set; }
        internal int MoreDataFillResponses { get; set; }
        internal int FillCalls { get; private set; }
        internal CancellationTokenSource? CancelDuringArrayRead { get; set; }

        internal static FakeNative WithTwoProcesses() => new(new()
        {
            [ProcessCounterKind.ProcessId] =
            [
                new("service_123", 0, 123),
                new("worker_456", 1, 456)
            ],
            [ProcessCounterKind.CpuTime] =
            [
                new("worker_456", 0, 987_654_321),
                new("service_123", 1, 123_456_789)
            ],
            [ProcessCounterKind.Lifetime] =
            [
                new("service_123", 1, 900_000_001),
                new("worker_456", 0, 1_234_567_890)
            ],
            [ProcessCounterKind.WorkingSet] =
            [
                new("worker_456", 0, 8_388_608),
                new("service_123", 1, 1_572_864)
            ]
        });

        public uint OpenQuery(out IntPtr query)
        {
            query = QueryHandle;
            return 0;
        }

        public uint AddCounter(IntPtr query, ProcessCounterKind counter, out IntPtr handle)
        {
            Assert.Equal(QueryHandle, query);
            AddedCounters.Add(counter);
            handle = new((int)counter + 1);
            _counterKinds.Add(handle, counter);
            return 0;
        }

        public uint CollectQueryData(IntPtr query)
        {
            Assert.Equal(QueryHandle, query);
            CollectCalls++;
            return CollectStatus;
        }

        public uint GetCounterType(IntPtr counter, CancellationToken cancellationToken, out uint counterType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            counterType = CounterTypeOverrides.GetValueOrDefault(_counterKinds[counter], _counterKinds[counter] switch
            {
                ProcessCounterKind.ProcessId => 0x00010000U,
                ProcessCounterKind.CpuTime => 0x20510500U,
                ProcessCounterKind.Lifetime => 0x30240500U,
                ProcessCounterKind.WorkingSet => 0x00010100U,
                _ => 0U
            });
            return 0;
        }

        public uint GetCounterTimeBase(IntPtr counter, out long timeBase)
        {
            Assert.Equal(ProcessCounterKind.CpuTime, _counterKinds[counter]);
            timeBase = CpuTimeBase;
            return 0;
        }

        public uint GetRawCounterArray(IntPtr counter, IntPtr buffer, ref uint bufferSize, out uint itemCount)
        {
            var kind = _counterKinds[counter];
            ReadKinds.Add(kind);
            var rows = _rows[kind];
            itemCount = (uint)rows.Length;
            var itemSize = Marshal.SizeOf<ProcessCounterRawCounterItem>();
            var stringBytes = rows.Sum(row => (row.InstanceName.Length + 1) * sizeof(char));
            var requiredSize = checked((uint)(itemSize * rows.Length + stringBytes));
            if (buffer == IntPtr.Zero)
            {
                bufferSize = ReportOversizedArray ? 4 * 1024 * 1024 + 1 : requiredSize;
                return MoreData;
            }

            FillCalls++;
            if (MoreDataFillResponses > 0)
            {
                MoreDataFillResponses--;
                bufferSize = requiredSize;
                return MoreData;
            }

            CancelDuringArrayRead?.Cancel();

            if (bufferSize < requiredSize)
            {
                bufferSize = requiredSize;
                return MoreData;
            }

            var nextNameOffset = itemSize * rows.Length;
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                var itemPointer = IntPtr.Add(buffer, itemSize * index);
                var namePointer = IntPtr.Add(buffer, nextNameOffset);
                var name = (row.InstanceName + '\0').ToCharArray();
                Marshal.Copy(name, 0, namePointer, name.Length);
                nextNameOffset += name.Length * sizeof(char);
                Marshal.StructureToPtr(new ProcessCounterRawCounterItem
                {
                    InstanceName = namePointer,
                    RawValue = new ProcessCounterNativeRawValue
                    {
                        CStatus = row.CStatus,
                        FirstValue = row.FirstValue
                    }
                }, itemPointer, false);
            }

            bufferSize = requiredSize;
            return 0;
        }

        public uint CloseQuery(IntPtr query)
        {
            Assert.Equal(QueryHandle, query);
            CloseCalls++;
            return CloseStatus;
        }
    }
}
