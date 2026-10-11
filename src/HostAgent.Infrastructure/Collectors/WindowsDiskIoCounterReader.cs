using System.Runtime.InteropServices;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Infrastructure.Collectors;

public interface IDiskIoCounterReader
{
    ValueTask<DiskIoTelemetry> ReadAsync(CancellationToken cancellationToken);
}

public sealed partial class WindowsDiskIoCounterReader : IDiskIoCounterReader
{
    private const uint PdhFormatDouble = 0x00000200;
    private const uint PdhValidData = 0;
    private const uint PdhNewData = 1;
    private const string ReadCounterPath = @"\PhysicalDisk(_Total)\Disk Read Bytes/sec";
    private const string WriteCounterPath = @"\PhysicalDisk(_Total)\Disk Write Bytes/sec";
    private const string LatencyCounterPath = @"\PhysicalDisk(_Total)\Avg. Disk sec/Transfer";

    public async ValueTask<DiskIoTelemetry> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IntPtr query = IntPtr.Zero;
        try
        {
            var status = PdhMethods.PdhOpenQuery(null, 0, out query);
            if (!Succeeded(status))
            {
                return Unavailable("disk_io_query_unavailable");
            }

            var readCounter = TryAddCounter(query, ReadCounterPath);
            var writeCounter = TryAddCounter(query, WriteCounterPath);
            var latencyCounter = TryAddCounter(query, LatencyCounterPath);
            if (readCounter == IntPtr.Zero && writeCounter == IntPtr.Zero && latencyCounter == IntPtr.Zero)
            {
                return Unavailable("disk_io_counters_unavailable");
            }

            if (!Succeeded(PdhMethods.PdhCollectQueryData(query)))
            {
                return Unavailable("disk_io_sample_unavailable");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            if (!Succeeded(PdhMethods.PdhCollectQueryData(query)))
            {
                return Unavailable("disk_io_sample_unavailable");
            }

            var readBytes = TryReadValue(readCounter);
            var writeBytes = TryReadValue(writeCounter);
            var latencySeconds = TryReadValue(latencyCounter);
            var available = readBytes.HasValue || writeBytes.HasValue || latencySeconds.HasValue;
            if (!available)
            {
                return Unavailable("disk_io_values_unavailable");
            }

            var complete = readBytes.HasValue && writeBytes.HasValue && latencySeconds.HasValue;
            return new DiskIoTelemetry(
                IsAvailable: true,
                ReadBytesPerSecond: readBytes is { } read ? Math.Max(0, read) : null,
                WriteBytesPerSecond: writeBytes is { } written ? Math.Max(0, written) : null,
                AverageLatencyMilliseconds: latencySeconds is { } latency
                    ? Math.Max(0, latency * 1000)
                    : null,
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
                _ = PdhMethods.PdhCloseQuery(query);
            }
        }
    }

    private static IntPtr TryAddCounter(IntPtr query, string path)
    {
        var status = PdhMethods.PdhAddEnglishCounter(query, path, 0, out var counter);
        return Succeeded(status) ? counter : IntPtr.Zero;
    }

    private static double? TryReadValue(IntPtr counter)
    {
        if (counter == IntPtr.Zero
            || !Succeeded(PdhMethods.PdhGetFormattedCounterValue(
                counter,
                PdhFormatDouble,
                out _,
                out var value))
            || (value.Status != PdhValidData && value.Status != PdhNewData)
            || !double.IsFinite(value.DoubleValue))
        {
            return null;
        }

        return value.DoubleValue;
    }

    private static bool Succeeded(uint status) => status == 0;

    private static DiskIoTelemetry Unavailable(string reason) =>
        new(false, null, null, null, reason);

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
