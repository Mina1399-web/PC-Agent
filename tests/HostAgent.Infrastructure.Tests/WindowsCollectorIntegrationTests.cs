using System.Net;
using System.Text.Json;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Infrastructure.Collectors;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class WindowsCollectorIntegrationTests
{
    [Fact]
    public async Task System_collector_returns_version_and_boot_facts()
    {
        var result = await new WindowsSystemCollector().CollectAsync(CancellationToken.None);

        Assert.Equal("system", result.CollectorId);
        Assert.NotEqual(CollectorStatus.Failed, result.Status);
        Assert.NotNull(result.Value);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.ProductName));
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Version));
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Build));
        Assert.InRange(result.Value.Uptime, TimeSpan.Zero, TimeSpan.FromDays(3650));
        Assert.NotNull(result.Value.BootTimeUtc);
        Assert.True(result.Value.BootTimeUtc <= DateTimeOffset.UtcNow);
        if (result.Value.LastLifecycleEvent is { } lifecycle)
        {
            Assert.False(string.IsNullOrWhiteSpace(lifecycle.Provider));
            Assert.True(lifecycle.EventId > 0);
            Assert.True(lifecycle.OccurredAtUtc is null || lifecycle.OccurredAtUtc.Value.Offset == TimeSpan.Zero);
        }
    }

    [Fact]
    public async Task Cpu_memory_collector_returns_bounded_host_memory_and_process_values()
    {
        var result = await new WindowsCpuMemoryCollector().CollectAsync(CancellationToken.None);

        Assert.Equal("cpu-memory", result.CollectorId);
        Assert.NotEqual(CollectorStatus.Failed, result.Status);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.TotalPhysicalMemoryBytes > 0);
        Assert.InRange(
            result.Value.AvailablePhysicalMemoryBytes,
            0,
            result.Value.TotalPhysicalMemoryBytes);
        Assert.InRange(result.Value.MemoryPressurePercent, 0, 100);
        Assert.True(result.Value.CommitLimitBytes is null or > 0);
        Assert.True(result.Value.CommitAvailableBytes is null or >= 0);
        if (result.Value.CpuUsagePercent is { } cpu)
        {
            Assert.True(double.IsFinite(cpu));
            Assert.InRange(cpu, 0, 100);
        }

        Assert.True(result.Value.TopProcessesByMemory.Count <= WindowsCpuMemoryCollector.TopProcessLimit);
        Assert.All(
            result.Value.TopProcessesByMemory,
            process =>
            {
                Assert.True(process.ProcessId > 0);
                Assert.True(process.MemoryBytes >= 0);
            });
        Assert.All(
            result.Value.TopProcessesByCpu,
            process =>
            {
                Assert.True(process.ProcessId > 0);
                Assert.True(process.CpuUsagePercent is null or >= 0 and <= 100);
            });
    }

    [Fact]
    public async Task Disk_collector_reports_capacity_and_treats_optional_io_as_partial()
    {
        var result = await new WindowsDiskCollector().CollectAsync(CancellationToken.None);

        Assert.Equal("disk", result.CollectorId);
        Assert.NotEqual(CollectorStatus.Failed, result.Status);
        Assert.NotNull(result.Value);
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        var systemVolume = Assert.Single(
            result.Value.Volumes,
            volume => string.Equals(volume.RootPath, systemRoot, StringComparison.OrdinalIgnoreCase));
        Assert.True(systemVolume.CapacityBytes > 0);
        Assert.InRange(systemVolume.FreeBytes, 0, systemVolume.CapacityBytes);
        Assert.Equal(systemVolume.CapacityBytes, systemVolume.UsedBytes + systemVolume.FreeBytes);
        Assert.InRange(systemVolume.UsedPercent, 0, 100);
        Assert.Contains("smart_health_unavailable", result.Warnings);
        Assert.Empty(result.Value.Health);
        Assert.True(result.Value.IoCounters is null
            || result.Value.IoCounters.IsAvailable
            || (!string.IsNullOrWhiteSpace(result.Value.IoCounters.UnavailableReason)
                && result.Status == CollectorStatus.Partial));
    }

    [Fact]
    public async Task Process_service_collector_includes_the_calling_process_and_stable_service_states()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var result = await new WindowsProcessServicesCollector().CollectAsync(CancellationToken.None);

        Assert.Equal("process-services", result.CollectorId);
        Assert.NotEqual(CollectorStatus.Failed, result.Status);
        Assert.NotNull(result.Value);
        var process = Assert.Single(result.Value.Processes, item => item.ProcessId == current.Id);
        Assert.False(string.IsNullOrWhiteSpace(process.ProcessName));
        Assert.True(process.MemoryBytes > 0);
        Assert.All(
            result.Value.Processes,
            item =>
            {
                Assert.True(item.ProcessId > 0);
                Assert.True(item.MemoryBytes >= 0);
                Assert.True(item.CpuUsagePercent is null or >= 0 and <= 100);
                Assert.True(item.ExecutablePath is null or { Length: > 0 });
            });
        Assert.All(result.Value.Services, service => Assert.False(string.IsNullOrWhiteSpace(service.ServiceName)));
        Assert.All(result.Value.ServiceChanges, change => Assert.Equal(TimeSpan.Zero, change.ObservedAtUtc.Offset));

        var manualService = Assert.Single(result.Value.Services, service => service.ServiceName == "FrameServer");
        var startModeProperty = typeof(ServiceTelemetry).GetProperty("StartMode");
        Assert.NotNull(startModeProperty);
        Assert.Equal("Manual", startModeProperty!.GetValue(manualService)?.ToString());
        // Read-only SCM queries in this Windows fixture confirm W32Time has
        // triggers and EventLog has none. Unknown must not be serialized as false.
        Assert.True(Assert.Single(result.Value.Services, service => service.ServiceName == "W32Time").HasTriggers);
        Assert.False(Assert.Single(result.Value.Services, service => service.ServiceName == "EventLog").HasTriggers);
    }

    // Defect: a fresh installation contacts fixed Internet/DNS targets before the operator opts in.
    [Fact]
    public async Task Default_network_collection_reports_disabled_probes_without_target_or_latency()
    {
        var result=await new WindowsNetworkCollector().CollectAsync(default);
        Assert.NotNull(result.Value);
        Assert.Equal(3,result.Value.Probes.Count);
        Assert.All(result.Value.Probes,probe=>
        {
            Assert.Equal(NetworkProbeStatus.Unavailable,probe.Status);
            Assert.Equal("network_probes_disabled",probe.FailureCode);
            Assert.Empty(probe.Target);
            Assert.Equal(TimeSpan.Zero,probe.Duration);
            Assert.Null(probe.LatencyMilliseconds);
            Assert.Null(probe.PacketLossPercent);
        });
    }
    [Fact]
    public async Task Network_collector_returns_adapter_and_bounded_probe_records_with_live_probes_opt_in()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        // Public connectivity probes are a separate, explicit test-run choice.
        var enableProbes = Environment.GetEnvironmentVariable("HOSTAGENT_TEST_LIVE_NETWORK") == "1";
        var result = await new WindowsNetworkCollector(enableProbes).CollectAsync(timeout.Token);

        Assert.Equal("network", result.CollectorId);
        Assert.NotEqual(CollectorStatus.Failed, result.Status);
        Assert.NotNull(result.Value);
        Assert.All(
            result.Value.Adapters,
            adapter =>
            {
                Assert.False(string.IsNullOrWhiteSpace(adapter.AdapterId));
                Assert.All(adapter.IpAddresses, address => Assert.True(IPAddress.TryParse(address, out _)));
                Assert.All(adapter.Gateways, address => Assert.True(IPAddress.TryParse(address, out _)));
                Assert.All(adapter.DnsServers, address => Assert.True(IPAddress.TryParse(address, out _)));
            });
        Assert.Equal(3, result.Value.Probes.Count);
        Assert.Contains(result.Value.Probes, probe => probe.Kind == NetworkProbeKind.Gateway);
        Assert.Contains(result.Value.Probes, probe => probe.Kind == NetworkProbeKind.InternetIp);
        Assert.Contains(result.Value.Probes, probe => probe.Kind == NetworkProbeKind.DnsResolution);
        if (!enableProbes)
        {
            Assert.All(result.Value.Probes, probe =>
            {
                Assert.Equal(NetworkProbeStatus.Unavailable, probe.Status);
                Assert.Equal("network_probes_disabled", probe.FailureCode);
                Assert.Empty(probe.Target);
            });
        }
        Assert.All(
            result.Value.Probes,
            probe =>
            {
                Assert.True(probe.Duration <= TimeSpan.FromSeconds(5));
                Assert.True(probe.LatencyMilliseconds is null or >= 0);
                Assert.True(probe.PacketLossPercent is null or >= 0 and <= 100);
            });
    }

    [Fact]
    public async Task Event_log_collector_returns_only_bounded_diagnostic_metadata_or_explicit_unavailability()
    {
        var result = await new WindowsEventLogCollector(new EmptyCheckpointStore()).CollectAsync(CancellationToken.None);

        Assert.Equal("event-log", result.CollectorId);
        Assert.NotEqual(CollectorStatus.Failed, result.Status);
        if (result.Value is { } value)
        {
            Assert.Equal(TimeSpan.Zero, value.CollectedAtUtc.Offset);
            Assert.True(value.Events.Count <= WindowsEventLogCollector.MaximumEvents);
            Assert.All(value.Checkpoints, checkpoint =>
            {
                Assert.Contains(checkpoint.Key, new[] { "System", "Application" });
                Assert.False(string.IsNullOrWhiteSpace(checkpoint.Value));
            });
            Assert.All(value.Events, entry =>
            {
                Assert.Contains(entry.LogName, new[] { "System", "Application" });
                Assert.Contains(entry.Level, new[] { "Critical", "Error", "Warning" });
                Assert.False(string.IsNullOrWhiteSpace(entry.Provider));
                Assert.True(entry.RecordId is null or > 0);
                Assert.True(entry.EventId >= 0);
                Assert.False(string.IsNullOrWhiteSpace(entry.MachineName));
                Assert.True(entry.OccurredAtUtc is null || entry.OccurredAtUtc.Value.Offset == TimeSpan.Zero);
            });
        }
        else
        {
            Assert.Equal(CollectorStatus.Unavailable, result.Status);
            Assert.NotNull(result.ErrorCode);
        }
    }

    [Fact]
    public async Task Invalid_event_log_bookmark_recovers_after_the_last_record_id()
    {
        var readerFactory = new WindowsEventLogReaderFactory();
        using var eventReader = readerFactory.Create("System", null, "*", reverseDirection: true);
        var latestRecord = eventReader.ReadEvent();
        Assert.NotNull(latestRecord);
        Assert.NotNull(latestRecord.RecordId);
        var previousScanUtc = DateTimeOffset.UtcNow.AddHours(-1);
        var lastRecordId = latestRecord.RecordId.Value;
        var lastRecordFingerprint = WindowsEventLogCollector.CreateRecordFingerprint(latestRecord);
        var persistedCheckpoint = JsonSerializer.Serialize(new
        {
            Version = 1,
            BookmarkXml = "not valid XML <",
            LastScannedAtUtc = previousScanUtc,
            LastRecordId = lastRecordId,
            LastRecordFingerprint = lastRecordFingerprint
        });
        var result = await new WindowsEventLogCollector(new EmptyCheckpointStore(persistedCheckpoint))
            .CollectAsync(CancellationToken.None);

        Assert.NotEqual(CollectorStatus.Unavailable, result.Status);
        Assert.NotNull(result.Value);
        Assert.Contains("event_log_system_bookmark_invalid", result.Warnings);

        var systemCheckpoint = Assert.Single(result.Value.Checkpoints, checkpoint => checkpoint.Key == "System");
        using var checkpointJson = JsonDocument.Parse(systemCheckpoint.Value);
        var lastScannedAtUtc = checkpointJson.RootElement.GetProperty("LastScannedAtUtc").GetDateTimeOffset();
        if (result.Warnings.Contains("event_log_system_truncated"))
        {
            Assert.Equal(previousScanUtc, lastScannedAtUtc);
        }
        else
        {
            Assert.True(lastScannedAtUtc > previousScanUtc);
        }

        Assert.True(checkpointJson.RootElement.GetProperty("LastRecordId").GetInt64() >= 1);
        Assert.All(
            result.Value.Events.Where(entry => entry.LogName == "System"),
            entry => Assert.True(entry.RecordId is null || entry.RecordId > lastRecordId));
    }

    [Fact]
    public async Task Every_collector_honors_cancellation_before_reading_host_state()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var token = cancellation.Token;
        var collectors = new Func<CancellationToken, Task>[]
        {
            async cancellationToken => await new WindowsSystemCollector().CollectAsync(cancellationToken),
            async cancellationToken => await new WindowsCpuMemoryCollector().CollectAsync(cancellationToken),
            async cancellationToken => await new WindowsDiskCollector().CollectAsync(cancellationToken),
            async cancellationToken => await new WindowsProcessServicesCollector().CollectAsync(cancellationToken),
            async cancellationToken => await new WindowsNetworkCollector().CollectAsync(cancellationToken),
            async cancellationToken => await new WindowsEventLogCollector(new EmptyCheckpointStore())
                .CollectAsync(cancellationToken)
        };

        foreach (var collect in collectors)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collect(token));
        }
    }

    private sealed class EmptyCheckpointStore(string? checkpointValue = null) : ICollectorCheckpointStore
    {
        public ValueTask<string?> ReadCollectorCheckpointAsync(
            string collectorId,
            string checkpointKey,
            CancellationToken cancellationToken) => ValueTask.FromResult(checkpointValue);

        public ValueTask SaveCollectorCheckpointAsync(
            string collectorId,
            string checkpointKey,
            string checkpointValue,
            DateTimeOffset updatedAtUtc,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
