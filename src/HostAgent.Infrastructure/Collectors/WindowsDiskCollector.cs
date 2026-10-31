using System.IO;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Infrastructure.Collectors;

public sealed class WindowsDiskCollector : ITelemetryCollector<DiskTelemetry>
{
    private readonly IDiskIoCounterReader _ioCounterReader;
    private readonly IDiskHealthReader _healthReader;

    public WindowsDiskCollector(IDiskIoCounterReader? ioCounterReader = null, IDiskHealthReader? healthReader = null)
    {
        _ioCounterReader = ioCounterReader ?? new WindowsDiskIoCounterReader();
        _healthReader = healthReader ?? new WindowsDiskHealthReader();
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

        var health = await _healthReader.ReadAsync(cancellationToken);
        if (health.Count == 0) warnings.Add("smart_health_unavailable");
        if (health.Any(item => item.FailureCode == "smart_failure_predicted")) warnings.Add("smart_failure_predicted");
        var telemetry = new DiskTelemetry(volumes.AsReadOnly(), ioCounters, health);
        return warnings.Count == 0
            ? CollectorResult<DiskTelemetry>.Success(CollectorId, now, telemetry)
            : CollectorResult<DiskTelemetry>.Partial(CollectorId, now, telemetry, warnings);
    }
}
