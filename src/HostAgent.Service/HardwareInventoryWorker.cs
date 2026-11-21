using System.Collections.Concurrent;
using HostAgent.Core.Models.Inventory;
using HostAgent.Infrastructure.Collectors;
using Microsoft.Extensions.Hosting;

namespace HostAgent.Service;

public sealed class HardwareInventoryCache
{
    private static readonly TimeSpan MaximumAge = TimeSpan.FromHours(2);
    private readonly ConcurrentDictionary<string, HardwareInventoryCategory> samples = new(StringComparer.Ordinal);

    public HardwareInventoryCategory Read(string category, DateTimeOffset now)
    {
        if (!samples.TryGetValue(category, out var sample))
            return Unavailable(category, null, "unknown", "waiting_for_sample");
        if (sample.Evidence.Status is not ("available" or "partial")
            || sample.Evidence.AtUtc is { } at && at <= now && now - at <= MaximumAge)
            return sample;

        return sample with
        {
            Evidence = sample.Evidence with
            {
                Status = "stale",
                Reason = sample.Evidence.AtUtc > now ? "invalid_sample_time" : "sample_expired"
            },
            Current = [],
            Prior = Retain(sample)
        };
    }

    public void Publish(HardwareInventoryCategory observation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        samples.AddOrUpdate(observation.Category, observation, (_, previous) =>
        {
            token.ThrowIfCancellationRequested();
            return observation.Evidence.Status is not "available"
                ? observation with { Prior = Retain(previous) }
                : observation;
        });
    }

    internal static HardwareInventoryCategory Unavailable(
        string category, DateTimeOffset? at, string status, string reason) =>
        new(category, new(HardwareInventorySources.ForCategory(category)!, at, status, reason), [], []);

    private static IReadOnlyList<InventoryItem> Retain(HardwareInventoryCategory sample)
    {
        var slots = new HashSet<string>(StringComparer.Ordinal);
        // Preserve earlier bounded evidence during incomplete refreshes. A
        // new current row can describe that slot without rewriting history.
        // Unidentified entries are not merged by an invented identity.
        return Array.AsReadOnly(sample.Prior.Concat(sample.Current)
            .Where(item => item.SlotId is null || slots.Add(item.SlotId))
            .Take(WindowsHardwareInventory.MaximumItems).ToArray());
    }
}

internal sealed class HardwareInventoryWorker(
    HardwareInventoryCache cache, WindowsHardwareInventory reader, TimeProvider clock) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(new[] { "cpu", "topology", "board", "bios", "firmware", "memory" }
            .Select(category => RunCategory(category, stoppingToken)));

    private async Task RunCategory(string category, CancellationToken stoppingToken)
    {
        // Isolate each provider call from the live telemetry loop and from
        // other inventory providers. ResilientWorker never overlaps a source
        // whose preceding native call has not returned after a timeout.
        using var worker = new ResilientWorker(
            token =>
            {
                var observation = reader.Read(category, token);
                cache.Publish(observation, token);
                return Task.CompletedTask;
            },
            TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(5), clock, code =>
            {
                if (stoppingToken.IsCancellationRequested) return;
                if (code is "cycle_timeout" or "cycle_failed")
                    cache.Publish(HardwareInventoryCache.Unavailable(category, clock.GetUtcNow(), "unavailable",
                        code == "cycle_timeout" ? "read_timeout" : "collector_unavailable"), CancellationToken.None);
            });
        await worker.StartAsync(stoppingToken);
        await worker.ExecuteTask!;
    }
}
