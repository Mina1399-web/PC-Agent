using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;
using HostAgent.Infrastructure.Collectors;
using Xunit;
using AdapterRow = HostAgent.Infrastructure.Collectors.WindowsGpuInventory.AdapterRow;

namespace HostAgent.Infrastructure.Tests;

public sealed class WindowsGpuInventoryTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Two_controllers_with_the_same_name_keep_distinct_windows_locators()
    {
        const string first = @"PCI\VEN_FAKE&DEV_1000\CONTROLLER_INSTANCE_1";
        var result = await ReadAsync(new AdapterRow("Same name", first),
            new AdapterRow("Same name", @"PCI\VEN_FAKE&DEV_1000\CONTROLLER_INSTANCE_2"));

        Assert.Equal("gpu", result.Category);
        Assert.Equal("available", result.Evidence.Status);
        Assert.Equal("Win32_VideoController", result.Evidence.Source);
        Assert.Equal(ObservedAt, result.Evidence.AtUtc);
        Assert.Equal(2, result.Current.Count);
        Assert.Empty(result.Prior);
        Assert.NotEqual(result.Current[0].SlotId, result.Current[1].SlotId);
        Assert.All(result.Current, item =>
        {
            Assert.Matches("^[0-9a-f]{64}$", item.SlotId!);
            Assert.Equal("physical_identity_not_verified", item.DeviceIdentity.Reason);
        });
        Assert.DoesNotContain("CONTROLLER_INSTANCE_1", JsonSerializer.Serialize(result));

        var repeated = await ReadAsync(new AdapterRow("Renamed", first.ToLowerInvariant()));
        Assert.Equal(result.Current[0].SlotId, Assert.Single(repeated.Current).SlotId);
    }

    [Fact]
    public async Task Colliding_or_missing_instance_ids_do_not_create_locators()
    {
        var result = await ReadAsync(new AdapterRow("First", @"PCI\DEV_1\Instance"),
            new AdapterRow("Second", @"pci\dev_1\INSTANCE"),
            new AdapterRow("Device class only", @"PCI\DEV_2"),
            new AdapterRow("No ID", null));

        Assert.Equal("incomplete_sample", result.Evidence.Reason);
        Assert.All(result.Current, item => Assert.Null(item.SlotId));
        Assert.Equal("instance_id_collision", result.Current[0].SlotEvidence.Reason);
        Assert.Equal("instance_id_collision", result.Current[1].SlotEvidence.Reason);
        Assert.Equal("instance_id_unidentified", result.Current[2].SlotEvidence.Reason);
        Assert.Equal("instance_id_unidentified", result.Current[3].SlotEvidence.Reason);
    }

    [Fact]
    public async Task Software_enumeration_is_reported_and_pci_is_not_physical_proof()
    {
        var result = await ReadAsync(new AdapterRow("Software controller", @"SWD\Display\Test1"),
            new AdapterRow("Virtual graphics", @"PCI\VEN_FAKE&DEV_BEEF\Guest1"),
            new AdapterRow("Root adapter", @"ROOT\Display\Test2"));

        Assert.Equal("software_enumerated", Field(0));
        Assert.Equal("pci_enumerated", Field(1));
        Assert.Equal("root_enumerated", Field(2));
        Assert.Null(result.Current[0].SlotId);
        Assert.Equal("software_device_identity_unstable", result.Current[0].SlotEvidence.Reason);
        Assert.All(result.Current, item => Assert.Equal("unknown", item.DeviceIdentity.Status));

        string? Field(int index) =>
            Assert.Single(result.Current[index].Fields, field => field.Name == "pnp_enumeration").Text;
    }

    [Fact]
    public async Task Truncation_does_not_claim_uniqueness_outside_the_observed_prefix()
    {
        var rows = Enumerable.Range(0, WindowsGpuInventory.MaximumAdapters + 1)
            .Select(index => new AdapterRow("Adapter", $@"PCI\DEV_0002\Instance{index}"))
            .ToArray();

        var result = await ReadAsync(rows);

        Assert.Equal("partial", result.Evidence.Status);
        Assert.Equal("enumeration_limit", result.Evidence.Reason);
        Assert.Equal(WindowsGpuInventory.MaximumAdapters, result.Current.Count);
        Assert.All(result.Current, item =>
        {
            Assert.Null(item.SlotId);
            Assert.Equal("enumeration_limit", item.SlotEvidence.Reason);
        });
    }

    [Fact]
    public async Task Empty_inventory_and_invalid_text_are_not_reported_as_known_values()
    {
        var empty = await ReadAsync();
        Assert.Equal("partial", empty.Evidence.Status);
        Assert.Equal("no_reported_items", empty.Evidence.Reason);
        Assert.Empty(empty.Current);

        var result = await ReadAsync(new AdapterRow("Unsafe\nname", "PCI\\DEV_01\\Instance\0"),
            new AdapterRow(new string('a', 63) + "\U0001F680", @"PCI\DEV_02\Instance"));
        var first = result.Current[0];
        Assert.Null(first.SlotId);
        Assert.Null(Assert.Single(first.Fields, field => field.Name == "name").Text);
        Assert.Equal(new string('a', 63), Assert.Single(result.Current[1].Fields, field => field.Name == "name").Text);
    }

    [Theory]
    [InlineData("access_denied")]
    [InlineData("read_timeout")]
    [InlineData("source_unavailable")]
    public async Task Provider_failure_is_reported_without_exception_text_or_fabricated_rows(string reason)
    {
        Exception error = reason switch
        {
            "access_denied" => new UnauthorizedAccessException("private context"),
            "read_timeout" => new TimeoutException("private context"),
            _ => new COMException("private context", (int)ManagementStatus.InvalidClass)
        };
        var reader = new WindowsGpuInventory(new FixedClock(), () => throw error, TimeSpan.FromSeconds(5));

        var result = await reader.ReadAsync(default);
        Assert.Equal("unavailable", result.Evidence.Status);
        Assert.Equal(reason, result.Evidence.Reason);
        Assert.Empty(result.Current);
        Assert.DoesNotContain("private context", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Timed_out_query_keeps_single_flight_until_wmi_returns()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leaving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var reader = new WindowsGpuInventory(new FixedClock(), () =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(30));
            leaving.TrySetResult();
            return [];
        }, TimeSpan.FromMilliseconds(200));

        try
        {
            var pending = reader.ReadAsync(default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("read_timeout", (await pending).Evidence.Reason);
            Assert.Equal("read_in_progress", (await reader.ReadAsync(default)).Evidence.Reason);
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally
        {
            release.Set();
            await leaving.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task Pre_canceled_read_starts_no_query()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var reader = new WindowsGpuInventory(new FixedClock(),
            () => throw new InvalidOperationException("query should not start"), TimeSpan.FromSeconds(5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(cancellation.Token));
    }

    private static Task<HostAgent.Core.Models.Inventory.HardwareInventoryCategory> ReadAsync(params AdapterRow[] rows) =>
        new WindowsGpuInventory(new FixedClock(), () => rows, TimeSpan.FromSeconds(5)).ReadAsync(default);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ObservedAt;
    }
}
