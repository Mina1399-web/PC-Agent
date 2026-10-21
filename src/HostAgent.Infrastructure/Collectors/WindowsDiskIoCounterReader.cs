using System.Runtime.InteropServices;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Infrastructure.Collectors;

public interface IDiskIoCounterReader
{
    ValueTask<DiskIoTelemetry> ReadAsync(CancellationToken cancellationToken);
}

// A closed native boundary for the three fixed counters; it cannot launch an
// operation, accept caller code or select arbitrary counter paths.
internal interface IDiskIoNativeApi
{
    uint OpenQuery(out IntPtr query);
    uint AddCounter(IntPtr query, DiskIoDiagnosticCounter counter, out IntPtr handle);
    uint CollectQueryData(IntPtr query);
    uint ReadFormattedValue(IntPtr counter, out uint dataStatus, out double value);
    uint CloseQuery(IntPtr query);
}

public sealed partial class WindowsDiskIoCounterReader : IDiskIoCounterReader
{
    private const uint PdhFormatDouble = 0x00000200;
    private const uint PdhValidData = 0;
    private const uint PdhNewData = 1;
    private const uint PdhInvalidData = 0xC0000BC6;
    private const string ReadCounterPath = @"\PhysicalDisk(_Total)\Disk Read Bytes/sec";
    private const string WriteCounterPath = @"\PhysicalDisk(_Total)\Disk Write Bytes/sec";
    private const string LatencyCounterPath = @"\PhysicalDisk(_Total)\Avg. Disk sec/Transfer";
    private readonly IDiskIoNativeApi _native;

    public WindowsDiskIoCounterReader() : this(new NativeApi()) { }

    internal WindowsDiskIoCounterReader(IDiskIoNativeApi native) =>
        _native = native ?? throw new ArgumentNullException(nameof(native));

    public async ValueTask<DiskIoTelemetry> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<DiskIoCounterDiagnostic>(10);
        var result = await ReadCoreAsync(diagnostics, cancellationToken);
        return result with { Diagnostics = diagnostics.AsReadOnly() };
    }

    private async ValueTask<DiskIoTelemetry> ReadCoreAsync(
        List<DiskIoCounterDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IntPtr query = IntPtr.Zero;
        try
        {
            var status = _native.OpenQuery(out query);
            diagnostics.Add(new(DiskIoDiagnosticStage.OpenQuery, DiskIoDiagnosticCounter.None, status, null));
            if (!Succeeded(status))
            {
                return Unavailable("disk_io_query_unavailable");
            }

            var readCounter = TryAddCounter(query, DiskIoDiagnosticCounter.Read, diagnostics);
            var writeCounter = TryAddCounter(query, DiskIoDiagnosticCounter.Write, diagnostics);
            var latencyCounter = TryAddCounter(query, DiskIoDiagnosticCounter.Latency, diagnostics);
            if (readCounter == IntPtr.Zero && writeCounter == IntPtr.Zero && latencyCounter == IntPtr.Zero)
            {
                return Unavailable("disk_io_counters_unavailable");
            }

            status = _native.CollectQueryData(query);
            diagnostics.Add(new(DiskIoDiagnosticStage.FirstSample, DiskIoDiagnosticCounter.None, status, null));
            if (!Succeeded(status))
            {
                return Unavailable("disk_io_sample_unavailable");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            status = _native.CollectQueryData(query);
            diagnostics.Add(new(DiskIoDiagnosticStage.SecondSample, DiskIoDiagnosticCounter.None, status, null));
            if (!Succeeded(status))
            {
                return Unavailable("disk_io_sample_unavailable");
            }

            var readBytes = TryReadValue(readCounter, DiskIoDiagnosticCounter.Read, diagnostics);
            var writeBytes = TryReadValue(writeCounter, DiskIoDiagnosticCounter.Write, diagnostics);
            var latencySeconds = TryReadValue(latencyCounter, DiskIoDiagnosticCounter.Latency, diagnostics);
            double? latencyMilliseconds = latencySeconds is { } latency ? Math.Max(0, latency * 1000) : null;
            if (latencyMilliseconds is { } milliseconds && !double.IsFinite(milliseconds))
                latencyMilliseconds = null;
            var available = readBytes.HasValue || writeBytes.HasValue || latencyMilliseconds.HasValue;
            if (!available)
            {
                return Unavailable("disk_io_values_unavailable");
            }

            var complete = readBytes.HasValue && writeBytes.HasValue && latencyMilliseconds.HasValue;
            return new DiskIoTelemetry(
                IsAvailable: true,
                ReadBytesPerSecond: readBytes is { } read ? Math.Max(0, read) : null,
                WriteBytesPerSecond: writeBytes is { } written ? Math.Max(0, written) : null,
                AverageLatencyMilliseconds: latencyMilliseconds,
                UnavailableReason: complete ? null : "some_disk_io_counters_unavailable");
        }
        catch (DllNotFoundException)
        {
            return Unavailable("disk_io_api_unavailable");
        }
        catch (EntryPointNotFoundException)
        {
            return Unavailable("disk_io_api_unavailable");
        }
        finally
        {
            if (query != IntPtr.Zero)
            {
                var status = _native.CloseQuery(query);
                diagnostics.Add(new(DiskIoDiagnosticStage.CloseQuery, DiskIoDiagnosticCounter.None, status, null));
            }
        }
    }

    private IntPtr TryAddCounter(IntPtr query, DiskIoDiagnosticCounter counterId,
        List<DiskIoCounterDiagnostic> diagnostics)
    {
        var status = _native.AddCounter(query, counterId, out var counter);
        diagnostics.Add(new(DiskIoDiagnosticStage.AddCounter, counterId, status, null));
        return Succeeded(status) ? counter : IntPtr.Zero;
    }

    private double? TryReadValue(IntPtr counter, DiskIoDiagnosticCounter counterId,
        List<DiskIoCounterDiagnostic> diagnostics)
    {
        if (counter == IntPtr.Zero) return null;
        var status = _native.ReadFormattedValue(counter, out var dataStatus, out var value);
        // PDH_INVALID_DATA may carry PDH_CSTATUS_NO_INSTANCE. Other failures do
        // not guarantee that the native output buffer contains a meaningful CStatus.
        diagnostics.Add(new(DiskIoDiagnosticStage.FormatValue, counterId, status,
            Succeeded(status) || status == PdhInvalidData ? dataStatus : null)
        {
            ValueFinite = Succeeded(status) && (dataStatus == PdhValidData || dataStatus == PdhNewData)
                ? double.IsFinite(value) : null
        });
        if (!Succeeded(status)
            || (dataStatus != PdhValidData && dataStatus != PdhNewData)
            || !double.IsFinite(value))
        {
            return null;
        }

        return value;
    }

    private static bool Succeeded(uint status) => status == 0;

    private static DiskIoTelemetry Unavailable(string reason) =>
        new(false, null, null, null, reason);

    private sealed class NativeApi : IDiskIoNativeApi
    {
        public uint OpenQuery(out IntPtr query) => PdhMethods.PdhOpenQuery(null, 0, out query);
        public uint AddCounter(IntPtr query, DiskIoDiagnosticCounter counter, out IntPtr handle)
        {
            var path = counter switch
            {
                DiskIoDiagnosticCounter.Read => ReadCounterPath,
                DiskIoDiagnosticCounter.Write => WriteCounterPath,
                DiskIoDiagnosticCounter.Latency => LatencyCounterPath,
                _ => throw new ArgumentOutOfRangeException(nameof(counter))
            };
            return PdhMethods.PdhAddEnglishCounter(query, path, 0, out handle);
        }
        public uint CollectQueryData(IntPtr query) => PdhMethods.PdhCollectQueryData(query);
        public uint ReadFormattedValue(IntPtr counter, out uint dataStatus, out double value)
        {
            var status = PdhMethods.PdhGetFormattedCounterValue(counter, PdhFormatDouble, out _, out var formatted);
            dataStatus = formatted.Status;
            value = formatted.DoubleValue;
            return status;
        }
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

        [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterValue")]
        internal static partial uint PdhGetFormattedCounterValue(
            IntPtr counter,
            uint format,
            out uint counterType,
            out PdhFormattedCounterValue value);

        [LibraryImport("pdh.dll", EntryPoint = "PdhCloseQuery")]
        internal static partial uint PdhCloseQuery(IntPtr query);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct PdhFormattedCounterValue
    {
        public uint Status;
        public double DoubleValue;
    }
}
