using System.IO;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Infrastructure.Collectors;

public sealed class WindowsDiskCollector : ITelemetryCollector<DiskTelemetry>
{
    private readonly IDiskIoCounterReader _ioCounterReader;

    public WindowsDiskCollector(IDiskIoCounterReader? ioCounterReader = null)
    {
        _ioCounterReader = ioCounterReader ?? new WindowsDiskIoCounterReader();
    }

    public string CollectorId => "disk";

    public async ValueTask<CollectorResult<DiskTelemetry>> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var warnings = new List<string>();
        var volumes = new List<DiskVolumeTelemetry>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (drive.DriveType != DriveType.Fixed)
                {
                    continue;
                }

                try
                {
                    if (!drive.IsReady)
                    {
                        warnings.Add("volume_not_ready");
                        continue;
                    }

                    var capacity = drive.TotalSize;
                    var free = Math.Clamp(drive.TotalFreeSpace, 0, capacity);
                    var used = Math.Max(0, capacity - free);
                    volumes.Add(new DiskVolumeTelemetry(
                        drive.RootDirectory.FullName,
                        drive.VolumeLabel,
                        drive.DriveFormat,
                        capacity,
                        used,
                        free,
                        capacity == 0 ? 0 : Math.Clamp(100d * used / capacity, 0, 100)));
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or DriveNotFoundException)
                {
                    warnings.Add("volume_metrics_unavailable");
                }
            }
        }
        catch (IOException)
        {
            return CollectorResult<DiskTelemetry>.Unavailable(CollectorId, now, "volume_enumeration_unavailable");
        }
        catch (UnauthorizedAccessException)
        {
            return CollectorResult<DiskTelemetry>.Unavailable(CollectorId, now, "volume_enumeration_denied");
        }

        if (volumes.Count == 0)
        {
            return CollectorResult<DiskTelemetry>.Unavailable(CollectorId, now, "no_fixed_volumes_available");
        }

        var ioCounters = await _ioCounterReader.ReadAsync(cancellationToken);
        if (!ioCounters.IsAvailable)
        {
            warnings.Add("disk_io_counters_unavailable");
        }
        else if (!string.IsNullOrWhiteSpace(ioCounters.UnavailableReason))
        {
            warnings.Add("some_disk_io_counters_unavailable");
        }

        // V1 does not issue vendor-specific SMART commands; they are not required for safe volume monitoring.
        warnings.Add("smart_health_unavailable");
        var telemetry = new DiskTelemetry(volumes.AsReadOnly(), ioCounters, Array.Empty<DiskHealthTelemetry>());
        return warnings.Count == 0
            ? CollectorResult<DiskTelemetry>.Success(CollectorId, now, telemetry)
            : CollectorResult<DiskTelemetry>.Partial(CollectorId, now, telemetry, warnings);
    }
}
