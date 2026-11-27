using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using HostAgent.Core.Dashboard;
using Microsoft.Win32.SafeHandles;

namespace HostAgent.Infrastructure.Collectors;

/// <summary>
/// Read-only mapping from Windows volume GUID paths to the OS-visible disk numbers
/// returned by IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS. Disk numbers are valid only
/// for this observation; underlying physical-media identity is intentionally unknown.
/// </summary>
public sealed class WindowsVolumeDiskTopologyReader
{
    internal const int MaximumVolumes = 32;
    internal const int MaximumExtents = 128;
    internal static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(3);
    internal const string Source = "Windows IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS";

    private readonly object _gate = new();
    private readonly IWindowsVolumeDiskTopologyNativeApi _native;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _maximumWait;
    private Task<VolumeDiskTopologyReport>? _pending;
    private Guid _pendingObservationId;
    private DateTimeOffset _pendingObservedAtUtc;

    public WindowsVolumeDiskTopologyReader(TimeProvider? clock = null)
        : this(new WindowsVolumeDiskTopologyNativeApi(), clock ?? TimeProvider.System, MaximumWait)
    {
    }

    internal WindowsVolumeDiskTopologyReader(
        IWindowsVolumeDiskTopologyNativeApi native,
        TimeProvider clock,
        TimeSpan maximumWait)
    {
        _native = native;
        _clock = clock;
        _maximumWait = maximumWait;
    }

    public async ValueTask<VolumeDiskTopologyReport> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<VolumeDiskTopologyReport> pending;
        Guid observationId;
        DateTimeOffset observedAt;
        lock (_gate)
        {
            if (_pending is null || _pending.IsCompleted)
            {
                observedAt = _clock.GetUtcNow();
                observationId = Guid.NewGuid();
                _pendingObservedAtUtc = observedAt;
                _pendingObservationId = observationId;
                _pending = Task.Run(() => ReadCore(observationId, observedAt));
            }
            else
            {
                observedAt = _pendingObservedAtUtc;
                observationId = _pendingObservationId;
            }
            pending = _pending!;
        }

        try
        {
            return await pending.WaitAsync(_maximumWait, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return EmptyReport(observationId, observedAt, "unavailable", "timeout", 0, 0);
        }
    }

    private VolumeDiskTopologyReport ReadCore(Guid observationId, DateTimeOffset observedAt)
    {
        var volumes = new List<VolumeDiskTopologyEntry>(MaximumVolumes);
        var extentsRead = 0;
        var reason = (string?)null;
        var enumerationComplete = false;

        try
        {
            var enumeration = _native.EnumerateVolumes(MaximumVolumes);
            var paths = enumeration.VolumeGuidPaths ?? Array.Empty<string>();
            var volumeLimitReached = paths.Count > MaximumVolumes ||
                                     paths.Count == MaximumVolumes && !enumeration.Complete;
            if (volumeLimitReached)
                reason = "volume_limit";
            else if (!enumeration.Complete)
                reason = enumeration.Reason ?? "volume_enumeration_incomplete";
            enumerationComplete = enumeration.Complete && !volumeLimitReached;

            foreach (var path in paths.Take(MaximumVolumes))
            {
                if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("\\\\?\\Volume{", StringComparison.OrdinalIgnoreCase) || !path.EndsWith('\\'))
                {
                    reason ??= "invalid_volume_identity";
                    volumes.Add(CreateEntry(path ?? string.Empty, Array.Empty<uint>(), "unknown", "invalid_volume_identity", observedAt));
                    continue;
                }

                var remainingExtents = MaximumExtents - extentsRead;
                if (remainingExtents <= 0)
                {
                    reason ??= "extent_limit";
                    volumes.Add(CreateEntry(path, Array.Empty<uint>(), "unknown", "extent_limit", observedAt));
                    continue;
                }

                var first = _native.QueryExtents(path, Math.Min(1, remainingExtents));
                var final = first;
                if (first.Status == VolumeExtentQueryStatus.MoreData)
                {
                    if (first.ExtentCount == 0)
                    {
                        final = VolumeExtentQuery.Invalid("incomplete_extent_buffer");
                    }
                    else if (first.ExtentCount > remainingExtents || first.ExtentCount > MaximumExtents)
                    {
                        final = VolumeExtentQuery.Invalid("extent_limit");
                    }
                    else
                    {
                        // Exactly one bounded retry is allowed after ERROR_MORE_DATA.
                        final = _native.QueryExtents(path, checked((int)first.ExtentCount));
                        if (final.Status == VolumeExtentQueryStatus.MoreData)
                            final = VolumeExtentQuery.Invalid("incomplete_extent_buffer");
                    }
                }

                if (final.Status == VolumeExtentQueryStatus.Success)
                {
                    if (final.ExtentCount > remainingExtents || final.DiskNumbers.Count > final.ExtentCount)
                    {
                        final = VolumeExtentQuery.Invalid("invalid_extent_buffer");
                    }
                    else
                    {
                        extentsRead += checked((int)final.ExtentCount);
                        var diskNumbers = final.DiskNumbers.Distinct().Order().ToArray();
                        var volumeStatus = diskNumbers.Length == 0 ? "unknown" : "available";
                        var entryReason = diskNumbers.Length == 0 ? "no_disk_extents" : null;
                        volumes.Add(CreateEntry(path, diskNumbers, volumeStatus, entryReason, observedAt));
                        if (entryReason is not null)
                            reason ??= entryReason;
                        continue;
                    }
                }

                var failure = MapFailure(final);
                reason ??= failure.Reason;
                volumes.Add(CreateEntry(path, Array.Empty<uint>(), failure.Status, failure.Reason, observedAt));
            }
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            reason ??= error is Win32Exception { NativeErrorCode: 5 } ? "access_denied" : "volume_enumeration_failed";
        }

        var status = volumes.Count == 0 && !enumerationComplete
            ? "unavailable"
            : reason is null && enumerationComplete ? "available" : "partial";
        return new(observationId, new(Source, observedAt, status, reason), volumes.Count, extentsRead,
            Array.AsReadOnly(volumes.ToArray()));
    }

    private static (string Status, string Reason) MapFailure(VolumeExtentQuery query) => query.Status switch
    {
        VolumeExtentQueryStatus.AccessDenied => ("unavailable", "access_denied"),
        VolumeExtentQueryStatus.Unsupported => ("unavailable", "unsupported_source"),
        VolumeExtentQueryStatus.Disappeared => ("unavailable", "volume_disappeared"),
        VolumeExtentQueryStatus.InvalidBuffer => ("unknown", query.Reason ?? "invalid_extent_buffer"),
        _ => ("unavailable", query.Reason ?? "volume_query_failed")
    };

    private static VolumeDiskTopologyEntry CreateEntry(string path, IReadOnlyList<uint> disks, string status,
        string? reason, DateTimeOffset observedAt) => new(path, disks, new(Source, observedAt, status, reason));

    private static VolumeDiskTopologyReport EmptyReport(Guid observationId, DateTimeOffset atUtc, string status,
        string reason, int enumerated, int extents) =>
        new(observationId, new(Source, atUtc, status, reason), enumerated, extents, Array.Empty<VolumeDiskTopologyEntry>());

    internal static bool TryParseExtents(byte[] buffer, int bytesReturned, int requestedCapacity,
        out uint extentCount, out IReadOnlyList<uint> diskNumbers, out bool moreData)
    {
        extentCount = 0;
        diskNumbers = Array.Empty<uint>();
        moreData = false;
        if (requestedCapacity < 1 || buffer.Length < 4 || bytesReturned < 4)
            return false;

        extentCount = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(0, 4));
        if (extentCount > requestedCapacity)
        {
            moreData = true;
            return true;
        }

        if (extentCount > MaximumExtents)
            return false;

        // VOLUME_DISK_EXTENTS aligns its first DISK_EXTENT at byte 8; each extent is 24 bytes.
        var requiredLength = 8L + (24L * extentCount);
        if (requiredLength > buffer.Length || requiredLength > bytesReturned)
            return false;

        var result = new uint[checked((int)extentCount)];
        for (var index = 0; index < result.Length; index++)
        {
            var offset = checked(8 + (index * 24));
            result[index] = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4));
        }
        diskNumbers = Array.AsReadOnly(result);
        return true;
    }
}

public sealed record VolumeDiskTopologyEntry(string VolumeGuidPath, IReadOnlyList<uint> DiskNumbers, ViewDataState State)
{
    /// <summary>These numbers identify only the OS-visible disk layer for this observation.</summary>
    public string DiskNumberScope => "os_visible_observation";

    /// <summary>The provider does not resolve RAID, Storage Spaces, VHD, or physical media beneath the OS-visible layer.</summary>
    public string UnderlyingMediaStatus => "unknown";
    public string UnderlyingMediaReason => "underlying_media_not_resolved";
}

public sealed record VolumeDiskTopologyReport(Guid ObservationId, ViewDataState State, int VolumesEnumerated,
    int ExtentsRead, IReadOnlyList<VolumeDiskTopologyEntry> Volumes);

internal interface IWindowsVolumeDiskTopologyNativeApi
{
    NativeVolumeEnumeration EnumerateVolumes(int maximumVolumes);
    VolumeExtentQuery QueryExtents(string volumeGuidPath, int extentCapacity);
}

internal sealed record NativeVolumeEnumeration(IReadOnlyList<string> VolumeGuidPaths, bool Complete, string? Reason = null);

internal enum VolumeExtentQueryStatus
{
    Success,
    MoreData,
    AccessDenied,
    Unsupported,
    Disappeared,
    InvalidBuffer,
    Failed
}

internal sealed record VolumeExtentQuery(VolumeExtentQueryStatus Status, uint ExtentCount,
    IReadOnlyList<uint> DiskNumbers, string? Reason = null)
{
    internal static VolumeExtentQuery Invalid(string reason) =>
        new(VolumeExtentQueryStatus.InvalidBuffer, 0, Array.Empty<uint>(), reason);
}

internal sealed class WindowsVolumeDiskTopologyNativeApi : IWindowsVolumeDiskTopologyNativeApi
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorInvalidFunction = 1;
    private const int ErrorNotSupported = 50;
    private const int ErrorNoMoreFiles = 18;
    private const int ErrorMoreData = 234;
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    private const uint ShareRead = 0x1;
    private const uint ShareWrite = 0x2;
    private const uint ShareDelete = 0x4;
    private const uint OpenExisting = 3;

    public NativeVolumeEnumeration EnumerateVolumes(int maximumVolumes)
    {
        var paths = new List<string>(maximumVolumes);
        var buffer = new char[128];
        using var handle = FindFirstVolumeW(buffer, (uint)buffer.Length);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            return error == ErrorNoMoreFiles
                ? new(paths, true)
                : new(paths, false, MapError(error));
        }

        do
        {
            var end = Array.IndexOf(buffer, '\0');
            paths.Add(new string(buffer, 0, end >= 0 ? end : buffer.Length));
            if (paths.Count >= maximumVolumes)
                return new(paths, false, "volume_limit");

            Array.Clear(buffer);
            if (FindNextVolumeW(handle, buffer, (uint)buffer.Length))
                continue;

            var error = Marshal.GetLastWin32Error();
            return error == ErrorNoMoreFiles
                ? new(paths, true)
                : new(paths, false, MapError(error));
        } while (true);
    }

    public VolumeExtentQuery QueryExtents(string volumeGuidPath, int extentCapacity)
    {
        var openPath = volumeGuidPath.TrimEnd('\\');
        using var handle = CreateFileW(openPath, 0, ShareRead | ShareWrite | ShareDelete,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            return Failure(Marshal.GetLastWin32Error());

        var buffer = new byte[checked(8 + (24 * extentCapacity))];
        var succeeded = DeviceIoControl(handle, IoctlVolumeGetVolumeDiskExtents, IntPtr.Zero, 0,
            buffer, (uint)buffer.Length, out var bytesReturned, IntPtr.Zero);
        var error = succeeded ? 0 : Marshal.GetLastWin32Error();
        if (!succeeded && error != ErrorMoreData)
            return Failure(error);

        // Even on ERROR_MORE_DATA, parse only bytes the API says it returned.
        // The extent count header is sufficient to decide whether one bounded retry fits.
        var returned = checked((int)Math.Min(bytesReturned, (uint)buffer.Length));
        if (!WindowsVolumeDiskTopologyReader.TryParseExtents(buffer, returned, extentCapacity,
                out var extentCount, out var disks, out var moreData))
            return VolumeExtentQuery.Invalid("invalid_extent_buffer");
        if (moreData)
            return new(VolumeExtentQueryStatus.MoreData, extentCount, Array.Empty<uint>());
        if (error == ErrorMoreData)
            return VolumeExtentQuery.Invalid("incomplete_extent_buffer");

        return new(VolumeExtentQueryStatus.Success, extentCount, disks);
    }

    private static VolumeExtentQuery Failure(int error) => error switch
    {
        ErrorAccessDenied => new(VolumeExtentQueryStatus.AccessDenied, 0, Array.Empty<uint>(), "access_denied"),
        ErrorInvalidFunction or ErrorNotSupported => new(VolumeExtentQueryStatus.Unsupported, 0, Array.Empty<uint>(), "unsupported_source"),
        ErrorFileNotFound or ErrorPathNotFound => new(VolumeExtentQueryStatus.Disappeared, 0, Array.Empty<uint>(), "volume_disappeared"),
        _ => new(VolumeExtentQueryStatus.Failed, 0, Array.Empty<uint>(), MapError(error))
    };

    private static string MapError(int error) => error switch
    {
        ErrorAccessDenied => "access_denied",
        ErrorInvalidFunction or ErrorNotSupported => "unsupported_source",
        ErrorFileNotFound or ErrorPathNotFound => "volume_disappeared",
        _ => "volume_enumeration_failed"
    };

    private sealed class FindVolumeHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => FindVolumeClose(handle);
    }

    [DllImport("kernel32.dll", EntryPoint = "FindFirstVolumeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern FindVolumeHandle FindFirstVolumeW([Out] char[] volumeName, uint bufferLength);

    [DllImport("kernel32.dll", EntryPoint = "FindNextVolumeW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextVolumeW(FindVolumeHandle findVolume, [Out] char[] volumeName, uint bufferLength);

    [DllImport("kernel32.dll", EntryPoint = "FindVolumeClose", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindVolumeClose(IntPtr findVolume);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, IntPtr input,
        uint inputLength, [Out] byte[] output, uint outputLength, out uint bytesReturned, IntPtr overlapped);
}
