using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace HostAgent.Infrastructure.Collectors;

internal sealed record ProcessCounterSample(
    int ProcessId,
    TimeSpan CpuTime,
    long MemoryBytes,
    long LifetimeMarker)
{
    internal string InstanceName { get; init; } = string.Empty;
    internal DateTimeOffset CapturedAtUtc { get; init; }
}

internal enum ProcessCounterKind
{
    ProcessId,
    CpuTime,
    Lifetime,
    WorkingSet
}

internal readonly record struct ProcessCounterRawRow(string InstanceName, uint CStatus, long FirstValue);

internal interface IProcessCounterNativeApi
{
    uint OpenQuery(out IntPtr query);
    uint AddCounter(IntPtr query, ProcessCounterKind counter, out IntPtr handle);
    uint CollectQueryData(IntPtr query);
    uint GetCounterType(IntPtr counter, CancellationToken cancellationToken, out uint counterType);
    uint GetCounterTimeBase(IntPtr counter, out long timeBase);
    uint GetRawCounterArray(IntPtr counter, IntPtr buffer, ref uint bufferSize, out uint itemCount);
    uint CloseQuery(IntPtr query);
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct ProcessCounterNativeRawValue
{
    public uint CStatus;
    public uint TimeStampLow;
    public uint TimeStampHigh;
    public long FirstValue;
    public long SecondValue;
    public uint MultiCount;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct ProcessCounterRawCounterItem
{
    public IntPtr InstanceName;
    public ProcessCounterNativeRawValue RawValue;
}

internal sealed partial class WindowsProcessCounterReader
{
    private const uint PdhSuccess = 0;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhValidData = 0;
    private const uint PdhNewData = 1;
    private const uint PdhCounterRawCount = 0x00010000;
    private const uint PdhCounterLargeRawCount = 0x00010100;
    private const uint PdhCounter100NanosecondTimer = 0x20510500;
    private const uint PdhCounterElapsedTime = 0x30240500;
    private const uint PdhInvalidData = 0xC0000BC6;
    private const long TimeBase100Nanoseconds = 10_000_000;
    private const uint MaximumArrayBytes = 4 * 1024 * 1024;
    private const uint MaximumCounterInfoBytes = 16 * 1024;
    private const uint MaximumArrayItems = 4096;
    private const int MaximumInstanceNameCharacters = 256;
    private const int MaximumNativeAttempts = 3;

    private static readonly ProcessCounterKind[] RequiredCounters =
    [
        ProcessCounterKind.ProcessId,
        ProcessCounterKind.CpuTime,
        ProcessCounterKind.Lifetime,
        ProcessCounterKind.WorkingSet
    ];

    private static readonly IReadOnlyDictionary<int, ProcessCounterSample> EmptySamples =
        new ReadOnlyDictionary<int, ProcessCounterSample>(new Dictionary<int, ProcessCounterSample>());

    private readonly IProcessCounterNativeApi _native;

    internal WindowsProcessCounterReader() : this(new NativeApi()) { }

    internal WindowsProcessCounterReader(IProcessCounterNativeApi native) =>
        _native = native ?? throw new ArgumentNullException(nameof(native));

    internal IReadOnlyDictionary<int, ProcessCounterSample> Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var samples = EmptySamples;
        var query = IntPtr.Zero;

        try
        {
            var status = _native.OpenQuery(out query);
            if (status == PdhSuccess && query != IntPtr.Zero)
            {
                var handles = new Dictionary<ProcessCounterKind, IntPtr>(RequiredCounters.Length);
                var ready = true;
                foreach (var kind in RequiredCounters)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    status = _native.AddCounter(query, kind, out var handle);
                    if (status != PdhSuccess || handle == IntPtr.Zero)
                    {
                        ready = false;
                        break;
                    }

                    handles.Add(kind, handle);
                }

                if (ready)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    status = _native.CollectQueryData(query);
                    if (status == PdhSuccess)
                    {
                        var capturedAtUtc = DateTimeOffset.UtcNow;
                        ready = CounterTypesAreSupported(handles, cancellationToken);
                        var arrays = new Dictionary<ProcessCounterKind, ProcessCounterRawRow[]>(RequiredCounters.Length);
                        foreach (var kind in RequiredCounters)
                        {
                            if (!ready) break;
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!TryReadArray(handles[kind], cancellationToken, out var rows))
                            {
                                ready = false;
                                break;
                            }

                            arrays.Add(kind, rows);
                        }

                        if (ready)
                        {
                            samples = JoinArrays(arrays, capturedAtUtc, cancellationToken);
                        }
                    }
                }
            }
        }
        catch (DllNotFoundException)
        {
            samples = EmptySamples;
        }
        catch (EntryPointNotFoundException)
        {
            samples = EmptySamples;
        }
        catch (BadImageFormatException)
        {
            samples = EmptySamples;
        }
        finally
        {
            if (query != IntPtr.Zero && !TryCloseQuery(query))
            {
                samples = EmptySamples;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return samples;
    }

    private bool CounterTypesAreSupported(
        IReadOnlyDictionary<ProcessCounterKind, IntPtr> handles,
        CancellationToken cancellationToken)
    {
        foreach (var kind in RequiredCounters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = _native.GetCounterType(handles[kind], cancellationToken, out var counterType);
            if (status != PdhSuccess || !IsSupportedCounterType(kind, counterType))
            {
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var cpuStatus = _native.GetCounterTimeBase(handles[ProcessCounterKind.CpuTime], out var timeBase);
        return cpuStatus == PdhSuccess && timeBase == TimeBase100Nanoseconds;
    }

    private static bool IsSupportedCounterType(ProcessCounterKind kind, uint counterType) => kind switch
    {
        ProcessCounterKind.ProcessId =>
            counterType is PdhCounterRawCount or PdhCounterLargeRawCount,
        ProcessCounterKind.CpuTime => counterType == PdhCounter100NanosecondTimer,
        ProcessCounterKind.Lifetime => counterType == PdhCounterElapsedTime,
        ProcessCounterKind.WorkingSet => counterType == PdhCounterLargeRawCount,
        _ => false
    };

    private bool TryReadArray(
        IntPtr counter,
        CancellationToken cancellationToken,
        out ProcessCounterRawRow[] rows)
    {
        rows = Array.Empty<ProcessCounterRawRow>();
        var itemSize = Marshal.SizeOf<ProcessCounterRawCounterItem>();

        for (var attempt = 0; attempt < MaximumNativeAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint bufferSize = 0;
            var status = _native.GetRawCounterArray(counter, IntPtr.Zero, ref bufferSize, out var itemCount);
            cancellationToken.ThrowIfCancellationRequested();

            if (status == PdhSuccess && itemCount == 0 && bufferSize == 0)
            {
                return true;
            }

            if ((status != PdhMoreData && status != PdhSuccess)
                || !HasBoundedArraySize(bufferSize, itemCount, itemSize))
            {
                return false;
            }

            IntPtr buffer;
            try
            {
                buffer = Marshal.AllocHGlobal(checked((int)bufferSize));
            }
            catch (OutOfMemoryException)
            {
                return false;
            }

            try
            {
                var allocatedBytes = bufferSize;
                status = _native.GetRawCounterArray(counter, buffer, ref bufferSize, out itemCount);
                cancellationToken.ThrowIfCancellationRequested();
                if (status == PdhMoreData)
                {
                    continue;
                }

                if (status != PdhSuccess
                    || bufferSize > allocatedBytes
                    || !HasBoundedArraySize(bufferSize, itemCount, itemSize))
                {
                    return false;
                }

                return TryParseArray(buffer, bufferSize, itemCount, itemSize, cancellationToken, out rows);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return false;
    }

    private static bool HasBoundedArraySize(uint bufferSize, uint itemCount, int itemSize) =>
        bufferSize > 0
        && bufferSize <= MaximumArrayBytes
        && itemCount > 0
        && itemCount <= MaximumArrayItems
        && (ulong)itemCount * (uint)itemSize <= bufferSize;

    private static bool TryParseArray(
        IntPtr buffer,
        uint bufferSize,
        uint itemCount,
        int itemSize,
        CancellationToken cancellationToken,
        out ProcessCounterRawRow[] rows)
    {
        var parsed = new List<ProcessCounterRawRow>(checked((int)itemCount));
        for (var index = 0; index < itemCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var itemPointer = IntPtr.Add(buffer, checked(index * itemSize));
            var item = Marshal.PtrToStructure<ProcessCounterRawCounterItem>(itemPointer);
            if (!TryReadBoundedInstanceName(item.InstanceName, buffer, bufferSize, out var instanceName))
            {
                continue;
            }

            parsed.Add(new ProcessCounterRawRow(
                instanceName,
                item.RawValue.CStatus,
                item.RawValue.FirstValue));
        }

        rows = parsed.ToArray();
        return true;
    }

    private static bool TryReadBoundedInstanceName(
        IntPtr namePointer,
        IntPtr buffer,
        uint bufferSize,
        out string instanceName)
    {
        instanceName = string.Empty;
        if (namePointer == IntPtr.Zero)
        {
            return false;
        }

        var offset = namePointer.ToInt64() - buffer.ToInt64();
        if (offset < 0 || (ulong)offset >= bufferSize)
        {
            return false;
        }

        var characters = new char[MaximumInstanceNameCharacters];
        var remainingBytes = bufferSize - (uint)offset;
        for (var index = 0; index <= MaximumInstanceNameCharacters; index++)
        {
            if ((uint)((index + 1) * sizeof(char)) > remainingBytes)
            {
                return false;
            }

            var character = (char)Marshal.ReadInt16(namePointer, index * sizeof(char));
            if (character == '\0')
            {
                if (index == 0) return false;
                instanceName = new string(characters, 0, index);
                return true;
            }

            if (index == MaximumInstanceNameCharacters)
            {
                return false;
            }

            characters[index] = character;
        }

        return false;
    }

    private static IReadOnlyDictionary<int, ProcessCounterSample> JoinArrays(
        IReadOnlyDictionary<ProcessCounterKind, ProcessCounterRawRow[]> arrays,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken)
    {
        var byCounter = new Dictionary<ProcessCounterKind, Dictionary<string, ProcessCounterRawRow>>(4);
        foreach (var kind in RequiredCounters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byCounter.Add(kind, UniqueValidRows(arrays[kind], cancellationToken));
        }

        var idRows = byCounter[ProcessCounterKind.ProcessId];
        var cpuRows = byCounter[ProcessCounterKind.CpuTime];
        var lifetimeRows = byCounter[ProcessCounterKind.Lifetime];
        var memoryRows = byCounter[ProcessCounterKind.WorkingSet];
        var pidCounts = new Dictionary<int, int>();
        foreach (var row in idRows.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetProcessId(row, out var processId))
            {
                pidCounts[processId] = pidCounts.GetValueOrDefault(processId) + 1;
            }
        }

        var result = new Dictionary<int, ProcessCounterSample>();
        foreach (var (instanceName, idRow) in idRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetProcessId(idRow, out var processId)
                || pidCounts[processId] != 1
                || !cpuRows.TryGetValue(instanceName, out var cpuRow)
                || !lifetimeRows.TryGetValue(instanceName, out var lifetimeRow)
                || !memoryRows.TryGetValue(instanceName, out var memoryRow)
                || cpuRow.FirstValue < 0
                || lifetimeRow.FirstValue < 0
                || memoryRow.FirstValue < 0)
            {
                continue;
            }

            result.Add(processId, new ProcessCounterSample(
                processId,
                TimeSpan.FromTicks(cpuRow.FirstValue),
                memoryRow.FirstValue,
                lifetimeRow.FirstValue)
            {
                InstanceName = instanceName,
                CapturedAtUtc = capturedAtUtc
            });
        }

        return result.Count == 0
            ? EmptySamples
            : new ReadOnlyDictionary<int, ProcessCounterSample>(result);
    }

    private static Dictionary<string, ProcessCounterRawRow> UniqueValidRows(
        IEnumerable<ProcessCounterRawRow> rows,
        CancellationToken cancellationToken)
    {
        var unique = new Dictionary<string, ProcessCounterRawRow>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasValidCStatus(row.CStatus)
                || string.IsNullOrEmpty(row.InstanceName)
                || row.InstanceName.Length > MaximumInstanceNameCharacters
                || duplicates.Contains(row.InstanceName))
            {
                continue;
            }

            if (!unique.TryAdd(row.InstanceName, row))
            {
                unique.Remove(row.InstanceName);
                duplicates.Add(row.InstanceName);
            }
        }

        return unique;
    }

    private static bool TryGetProcessId(ProcessCounterRawRow row, out int processId)
    {
        if (row.FirstValue is > 0 and <= int.MaxValue)
        {
            processId = (int)row.FirstValue;
            return true;
        }

        processId = 0;
        return false;
    }

    private static bool HasValidCStatus(uint status) => status is PdhValidData or PdhNewData;

    private bool TryCloseQuery(IntPtr query)
    {
        try
        {
            return _native.CloseQuery(query) == PdhSuccess;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private sealed class NativeApi : IProcessCounterNativeApi
    {
        public uint OpenQuery(out IntPtr query) => PdhMethods.PdhOpenQuery(null, 0, out query);

        public uint AddCounter(IntPtr query, ProcessCounterKind counter, out IntPtr handle)
        {
            var path = counter switch
            {
                ProcessCounterKind.ProcessId => @"\Process V2(*)\Process ID",
                ProcessCounterKind.CpuTime => @"\Process V2(*)\% Processor Time",
                ProcessCounterKind.Lifetime => @"\Process V2(*)\Elapsed Time",
                ProcessCounterKind.WorkingSet => @"\Process V2(*)\Working Set",
                _ => throw new ArgumentOutOfRangeException(nameof(counter))
            };
            return PdhMethods.PdhAddEnglishCounter(query, path, 0, out handle);
        }

        public uint CollectQueryData(IntPtr query) => PdhMethods.PdhCollectQueryData(query);

        public uint GetCounterType(IntPtr counter, CancellationToken cancellationToken, out uint counterType)
        {
            counterType = 0;
            for (var attempt = 0; attempt < MaximumNativeAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uint bufferSize = 0;
                var status = PdhMethods.PdhGetCounterInfo(counter, 0, ref bufferSize, IntPtr.Zero);
                cancellationToken.ThrowIfCancellationRequested();
                if (status != PdhMoreData
                    || bufferSize < sizeof(uint) * 2
                    || bufferSize > MaximumCounterInfoBytes)
                {
                    return status == PdhSuccess ? PdhInvalidData : status;
                }

                IntPtr buffer;
                try
                {
                    buffer = Marshal.AllocHGlobal(checked((int)bufferSize));
                }
                catch (OutOfMemoryException)
                {
                    return PdhInvalidData;
                }

                try
                {
                    var allocatedBytes = bufferSize;
                    status = PdhMethods.PdhGetCounterInfo(counter, 0, ref bufferSize, buffer);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (status == PdhMoreData)
                    {
                        continue;
                    }

                    if (status != PdhSuccess || bufferSize < sizeof(uint) * 2 || bufferSize > allocatedBytes)
                    {
                        return status == PdhSuccess ? PdhInvalidData : status;
                    }

                    counterType = unchecked((uint)Marshal.ReadInt32(buffer, sizeof(uint)));
                    return PdhSuccess;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            return PdhMoreData;
        }

        public uint GetCounterTimeBase(IntPtr counter, out long timeBase) =>
            PdhMethods.PdhGetCounterTimeBase(counter, out timeBase);

        public uint GetRawCounterArray(IntPtr counter, IntPtr buffer, ref uint bufferSize, out uint itemCount) =>
            PdhMethods.PdhGetRawCounterArray(counter, ref bufferSize, out itemCount, buffer);

        public uint CloseQuery(IntPtr query) => PdhMethods.PdhCloseQuery(query);
    }

    private static partial class PdhMethods
    {
        [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial uint PdhOpenQuery(string? dataSource, nuint userData, out IntPtr query);

        [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial uint PdhAddEnglishCounter(
            IntPtr query,
            string fullCounterPath,
            nuint userData,
            out IntPtr counter);

        [LibraryImport("pdh.dll", EntryPoint = "PdhCollectQueryData")]
        internal static partial uint PdhCollectQueryData(IntPtr query);

        [LibraryImport("pdh.dll", EntryPoint = "PdhGetCounterInfoW")]
        internal static partial uint PdhGetCounterInfo(
            IntPtr counter,
            byte retrieveExplainText,
            ref uint bufferSize,
            IntPtr buffer);

        [LibraryImport("pdh.dll", EntryPoint = "PdhGetCounterTimeBase")]
        internal static partial uint PdhGetCounterTimeBase(IntPtr counter, out long timeBase);

        [LibraryImport("pdh.dll", EntryPoint = "PdhGetRawCounterArrayW")]
        internal static partial uint PdhGetRawCounterArray(
            IntPtr counter,
            ref uint bufferSize,
            out uint itemCount,
            IntPtr buffer);

        [LibraryImport("pdh.dll", EntryPoint = "PdhCloseQuery")]
        internal static partial uint PdhCloseQuery(IntPtr query);
    }
}
