using System.Diagnostics.Eventing.Reader;
using Microsoft.Win32;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Infrastructure.Collectors;

public sealed class WindowsSystemCollector : ITelemetryCollector<SystemTelemetry>
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string LifecycleQuery =
        "*[System[(EventID=41 or EventID=1074 or EventID=6005 or EventID=6006 or EventID=6008)]]";

    public string CollectorId => "system";

    public ValueTask<CollectorResult<SystemTelemetry>> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capturedAt = DateTimeOffset.UtcNow;
        var osVersion = Environment.OSVersion.Version;
        var productName = "Microsoft Windows";
        var build = osVersion.Build.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var warnings = new List<string>();

        try
        {
            using var versionKey = Registry.LocalMachine.OpenSubKey(CurrentVersionKey, writable: false);
            productName = versionKey?.GetValue("ProductName") as string ?? productName;
            build = versionKey?.GetValue("CurrentBuildNumber") as string ?? build;
        }
        catch (UnauthorizedAccessException)
        {
            warnings.Add("windows_version_registry_unavailable");
        }
        catch (System.Security.SecurityException)
        {
            warnings.Add("windows_version_registry_unavailable");
        }

        var uptime = TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64));
        var bootTime = capturedAt - uptime;
        var lifecycle = ReadLastLifecycleEvent(cancellationToken, warnings);
        var telemetry = new SystemTelemetry(
            productName,
            osVersion.ToString(),
            build,
            bootTime,
            uptime,
            lifecycle);

        return ValueTask.FromResult(warnings.Count == 0
            ? CollectorResult<SystemTelemetry>.Success(CollectorId, capturedAt, telemetry)
            : CollectorResult<SystemTelemetry>.Partial(CollectorId, capturedAt, telemetry, warnings));
    }

    private static SystemLifecycleEvent? ReadLastLifecycleEvent(
        CancellationToken cancellationToken,
        ICollection<string> warnings)
    {
        try
        {
            var query = new EventLogQuery("System", PathType.LogName, LifecycleQuery)
            {
                ReverseDirection = true
            };
            using var reader = new EventLogReader(query);
            cancellationToken.ThrowIfCancellationRequested();
            using var record = reader.ReadEvent();
            if (record is not null)
            {
                return new SystemLifecycleEvent(
                    record.Id,
                    record.ProviderName ?? "unknown",
                    record.TimeCreated is { } created
                        ? new DateTimeOffset(created.ToUniversalTime(), TimeSpan.Zero)
                        : null);
            }
        }
        catch (UnauthorizedAccessException)
        {
            warnings.Add("shutdown_history_unavailable");
        }
        catch (EventLogException)
        {
            warnings.Add("shutdown_history_unavailable");
        }
        catch (InvalidOperationException)
        {
            warnings.Add("shutdown_history_unavailable");
        }

        return null;
    }
}
