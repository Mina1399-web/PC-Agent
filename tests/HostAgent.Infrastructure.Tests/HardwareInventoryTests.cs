using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using HostAgent.Infrastructure.Collectors;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Service;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class HardwareInventoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Dimms_keep_capacity_without_exposing_raw_locator_or_inventing_device_identity()
    {
        var reading = Read("memory", ["DIMM_A1", "BOARD BANK 0", 17179869184UL, 0U, 34U]);
        var row = Assert.Single(reading.Current);
        Assert.Equal("partial", reading.Evidence.Status);
        Assert.Equal("Win32_PhysicalMemory", reading.Evidence.Source);
        Assert.Equal(Now, reading.Evidence.AtUtc);
        Assert.Matches("^[0-9a-f]{64}$", row.SlotId!);
        Assert.Equal("unknown", row.DeviceIdentity.Status);
        Assert.Equal("device_identity_not_collected", row.DeviceIdentity.Reason);
        var capacity = Assert.Single(row.Fields, x => x.Name == "capacity_bytes");
        Assert.Equal("available", capacity.Evidence.Status);
        Assert.Equal(17179869184L, capacity.Number);
        Assert.Equal("unknown", Assert.Single(row.Fields, x => x.Name == "configured_clock_mhz_reported").Evidence.Status);
        Assert.Equal(row.SlotId, Assert.Single(Read("memory", ["dimm_a1", "board bank 0", 1UL, 1U, 1U]).Current).SlotId);
        var serialized = JsonSerializer.Serialize(reading);
        Assert.DoesNotContain("DIMM_A1", serialized);
        Assert.DoesNotContain("BOARD BANK 0", serialized);
    }

    [Fact]
    public void Duplicate_and_placeholder_slots_remain_unidentified()
    {
        var reading = Read("memory",
            ["DIMM_A1", "BANK 0", 1UL, 1U, 1U], ["dimm_a1", "bank 0", 2UL, 1U, 1U],
            ["Default String", null, 3UL, 1U, 1U]);
        Assert.Equal("partial", reading.Evidence.Status);
        Assert.All(reading.Current, row => Assert.Null(row.SlotId));
        Assert.Equal("slot_collision", reading.Current[0].SlotEvidence.Reason);
        Assert.Equal("slot_collision", reading.Current[1].SlotEvidence.Reason);
        Assert.Equal("slot_unidentified", reading.Current[2].SlotEvidence.Reason);
    }

    [Fact]
    public void Truncation_is_partial_and_does_not_claim_unique_slots_outside_the_visible_prefix()
    {
        var rows = Enumerable.Range(0, WindowsHardwareInventory.MaximumItems + 1)
            .Select(index => new object?[] { $"CPU{index}", "CPU", "Vendor", 4U }).ToArray();
        var reading = Read("cpu", rows);
        Assert.Equal("enumeration_limit", reading.Evidence.Reason);
        Assert.Equal(WindowsHardwareInventory.MaximumItems, reading.Current.Count);
        Assert.All(reading.Current, row => Assert.Null(row.SlotId));
    }

    [Theory]
    [InlineData("access_denied")]
    [InlineData("read_timeout")]
    public void Provider_failures_preserve_old_evidence_as_prior_only(string reason)
    {
        var cache = new HardwareInventoryCache();
        Assert.Equal("waiting_for_sample", cache.Read("cpu", Now).Evidence.Reason);
        var initial = Read("cpu", ["CPU0", "Known CPU", "Vendor", 4U]);
        cache.Publish(initial, default);
        var failure = WindowsHardwareInventory.Read("cpu", new Clock(),
            (_, _) => throw (reason == "access_denied" ? new UnauthorizedAccessException("private detail") :
                new TimeoutException("private detail")), default);
        cache.Publish(failure, default);

        var reading = cache.Read("cpu", Now);
        Assert.Equal("unavailable", reading.Evidence.Status);
        Assert.Equal(reason, reading.Evidence.Reason);
        Assert.Empty(reading.Current);
        Assert.Same(Assert.Single(initial.Current), Assert.Single(reading.Prior));
        Assert.DoesNotContain("private detail", JsonSerializer.Serialize(reading));
    }

    [Fact]
    public void Partial_and_expired_inventory_cannot_present_old_rows_as_current()
    {
        var cache = new HardwareInventoryCache();
        var old = Read("cpu", ["CPU0", "First", "Vendor", 4U], ["CPU1", "Second", "Vendor", 4U]);
        var partial = Read("cpu", ["CPU0", "First", "Vendor", 0U]);
        cache.Publish(old, default);
        cache.Publish(partial, default);
        var now = cache.Read("cpu", Now);
        Assert.Equal("partial", now.Evidence.Status);
        Assert.Single(now.Current);
        Assert.Equal(2, now.Prior.Count);
        var stale = cache.Read("cpu", Now.AddHours(3));
        Assert.Equal("stale", stale.Evidence.Status);
        Assert.Empty(stale.Current);
        Assert.Equal(2, stale.Prior.Count);
        Assert.Equal(Now, stale.Prior[1].Fields[0].Evidence.AtUtc);
        Assert.Equal("invalid_sample_time", cache.Read("cpu", Now.AddSeconds(-1)).Evidence.Reason);
        cache.Publish(old, default);
        Assert.Empty(cache.Read("cpu", Now).Prior);
    }

    [Fact]
    public void Canceled_native_results_cannot_be_published()
    {
        using var cancellation = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() => WindowsHardwareInventory.Read("bios", new Clock(),
            (_, _) =>
            {
                cancellation.Cancel();
                return [["Vendor", "Firmware"]];
            }, cancellation.Token));
        var cache = new HardwareInventoryCache();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            cache.Publish(Read("bios", ["Vendor", "Firmware"]), cancellation.Token));
        Assert.Equal("waiting_for_sample", cache.Read("bios", Now).Evidence.Reason);
    }

    [Fact]
    public void Native_topology_reports_active_cores_and_logical_processors_across_groups()
    {
        var data = new byte[96];
        WriteCore(data.AsSpan(0, 48), 0, 0b101);
        WriteCore(data.AsSpan(48, 48), 2, 1UL << 63);
        Assert.Equal((2, 3), WindowsHardwareInventory.ParseTopology(data));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 100);
        var failure = WindowsHardwareInventory.Read("topology", new Clock(), (_, _) =>
        {
            // The worker must classify malformed records rather than publish a partial sum.
            _ = WindowsHardwareInventory.ParseTopology(data);
            return [];
        }, default);
        Assert.Equal("invalid_sample", failure.Evidence.Reason);
        Assert.Empty(failure.Current);
    }

    [Theory]
    [InlineData("""{"category":"gpu"}""")]
    [InlineData("""{"category":"cpu","computer":"elsewhere"}""")]
    [InlineData("""{"category":null}""")]
    public void View_protocol_rejects_other_categories_and_extra_fields(string body)
    {
        var payload = Encoding.UTF8.GetBytes("{\"version\":1,\"command\":\"hardware-inventory\",\"body\":" + body + "}");
        Assert.Throws<InvalidDataException>(() => ViewProtocol.Parse(payload));
    }

    [Fact]
    public async Task View_reads_only_the_bounded_selected_cache_category()
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        var cache = new HardwareInventoryCache();
        cache.Publish(Read("cpu", ["CPU0", "Fixture CPU", "Vendor", 4U]), default);
        cache.Publish(Read("board", ["Fixture board", "Vendor", "v1"]), default);
        var dispatcher = new DashboardDispatcher(fixture.Store, new(), new(), new Clock(), hardwareInventory: cache);
        var response = await dispatcher.DispatchAsync(ViewProtocol.Request("hardware-inventory", new { category = "cpu" }), default);
        Assert.InRange(response.Length, 1, HelperProtocol.MaximumFrameBytes);
        using var json = JsonDocument.Parse(response);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("cpu", json.RootElement.GetProperty("data").GetProperty("Category").GetString());
        Assert.Contains("Fixture CPU", Encoding.UTF8.GetString(response));
        Assert.DoesNotContain("Fixture board", Encoding.UTF8.GetString(response));
    }

    private static HostAgent.Core.Models.Inventory.HardwareInventoryCategory Read(string category, params object?[][] rows) =>
        WindowsHardwareInventory.Read(category, new Clock(), (_, _) => rows, default);

    private static void WriteCore(Span<byte> data, ushort group, ulong mask)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data[4..], 48);
        BinaryPrimitives.WriteUInt16LittleEndian(data[30..], 1);
        BinaryPrimitives.WriteUInt64LittleEndian(data[32..], mask);
        BinaryPrimitives.WriteUInt16LittleEndian(data[40..], group);
    }
}
