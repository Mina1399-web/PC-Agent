using System.Runtime.InteropServices;
using HostAgent.Core.Models.Telemetry;
using Microsoft.Win32.SafeHandles;

namespace HostAgent.Infrastructure.Collectors;

public interface IDiskHealthReader
{
    ValueTask<IReadOnlyList<DiskHealthTelemetry>> ReadAsync(CancellationToken cancellationToken);
}

// Read-only Windows failure prediction. Unsupported devices yield no claim of health.
// One native probe at a time; a stalled driver cannot accumulate worker threads.
public sealed partial class WindowsDiskHealthReader : IDiskHealthReader
{
    private readonly object _gate = new();
    private Task<IReadOnlyList<DiskHealthTelemetry>>? _pending;
    private DateTimeOffset _started;

    public async ValueTask<IReadOnlyList<DiskHealthTelemetry>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<IReadOnlyList<DiskHealthTelemetry>> pending;
        lock (_gate)
        {
            if (_pending is null || _pending.IsCompleted && DateTimeOffset.UtcNow - _started > TimeSpan.FromMinutes(5))
            {
                _started = DateTimeOffset.UtcNow;
                _pending = Task.Run(Probe);
            }
            pending = _pending;
        }
        try { return await pending.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken); }
        catch (TimeoutException) { return []; }
    }

    private static IReadOnlyList<DiskHealthTelemetry> Probe()
    {
        var results = new List<DiskHealthTelemetry>();
        for (var index = 0; index < 64; index++)
        {
            var device = "PhysicalDrive" + index;
            using var handle = CreateFile(@"\\.\" + device, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid) continue;
            var buffer = new byte[516]; // STORAGE_PREDICT_FAILURE: ULONG plus 512 vendor bytes.
            const uint predictFailure = 0x002D1100;
            if (!DeviceIoControl(handle, predictFailure, IntPtr.Zero, 0, buffer, (uint)buffer.Length, out var returned, IntPtr.Zero)
                || returned < buffer.Length) continue;
            var failed = BitConverter.ToUInt32(buffer, 0) != 0;
            results.Add(new(device, failed ? "FailurePredicted" : "NoFailurePredicted", failed ? "smart_failure_predicted" : null));
        }
        return results.AsReadOnly();
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, uint inputLength,
        [Out] byte[] output, uint outputLength, out uint returned, IntPtr overlapped);
}
