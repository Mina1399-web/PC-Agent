using HostAgent.Core.Models.Inventory;
using HostAgent.Infrastructure.Collectors;
using Microsoft.Extensions.Hosting;

namespace HostAgent.Service;

public sealed class DeviceHealthCache
{
    private DeviceHealthReport? latest;

    public DeviceHealthReport Read(DateTimeOffset now) => Volatile.Read(ref latest)?.At(now)
        ?? Missing(null, "unknown", "waiting_for_sample");

    internal void Publish(DeviceHealthReport report, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref latest, report);
    }

    internal static DeviceHealthReport Missing(DateTimeOffset? at, string status, string reason) =>
        new(status, new(DeviceHealthReport.Source, at, status, reason), 0, 0, []);
}

internal sealed class DeviceHealthWorker(
    DeviceHealthCache cache, WindowsDeviceHealthReader reader, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var worker = new ResilientWorker(token =>
        {
            var report = reader.Read(token);
            cache.Publish(report, token);
            return Task.CompletedTask;
        }, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(3), clock, code =>
        {
            if (!stoppingToken.IsCancellationRequested && code is "cycle_timeout" or "cycle_failed")
                cache.Publish(DeviceHealthCache.Missing(clock.GetUtcNow(), "unavailable",
                    code == "cycle_timeout" ? "read_timeout" : "collector_unavailable"), CancellationToken.None);
        });
        await worker.StartAsync(stoppingToken);
        await worker.ExecuteTask!;
    }
}
