using System.Text;
using System.Text.Json;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Service;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class DashboardDiskDiagnosticsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 19, 0, 0, TimeSpan.Zero);

    // A real persisted disk sample must expose bounded native failure metadata,
    // without opening tokens or returning private payload fields in View.
    [Fact]
    public async Task Overview_projects_fresh_native_failure_without_counter_values_or_private_fields()
    {
        var diagnostics = new[] { new { Stage = 1, Counter = 1, Status = 0xC0000BB8u, DataStatus = (uint?)null, Private = "synthetic-private-canary" } };
        using var reply = await Read(diagnostics);
        var data = reply.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("DiskIoDiagnostics", out var projection), "View discards the stored native disk diagnostics.");
        Assert.Equal("available", projection.GetProperty("Status").GetString());
        Assert.Equal(Now, projection.GetProperty("AtUtc").GetDateTimeOffset());
        var record = Assert.Single(projection.GetProperty("Records").EnumerateArray());
        Assert.Equal("add_counter", record.GetProperty("Stage").GetString());
        Assert.Equal("read", record.GetProperty("Counter").GetString());
        Assert.Equal(0xC0000BB8u, record.GetProperty("Status").GetUInt32());
        Assert.Equal(JsonValueKind.Null, record.GetProperty("DataStatus").ValueKind);
        Assert.DoesNotContain("synthetic-private", reply.RootElement.GetRawText());
        Assert.DoesNotContain("987654321", projection.GetRawText());
    }

    [Theory]
    [InlineData("null", "not_recorded")]
    [InlineData("[]", "not_recorded")]
    [InlineData("{}", "invalid")]
    [InlineData("[{\"Stage\":99,\"Counter\":0,\"Status\":0}]", "invalid")]
    [InlineData("[{\"Stage\":1,\"Counter\":99,\"Status\":0}]", "invalid")]
    [InlineData("[{\"Stage\":0,\"Counter\":1,\"Status\":0}]", "invalid")]
    [InlineData("[{\"Stage\":1,\"Counter\":1,\"Status\":-1}]", "invalid")]
    [InlineData("[{\"Stage\":1,\"Counter\":1,\"Status\":4294967296}]", "invalid")]
    [InlineData("[{\"Stage\":1,\"Counter\":1,\"Status\":0,\"DataStatus\":1}]", "invalid")]
    [InlineData("[{\"Stage\":4,\"Counter\":2,\"Status\":0,\"DataStatus\":\"private\"}]", "invalid")]
    [InlineData("[{\"Stage\":4,\"Counter\":2,\"Status\":0,\"DataStatus\":4294967296}]", "invalid")]
    [InlineData("[{\"Stage\":1,\"Counter\":1,\"Status\":0,\"ValueFinite\":true}]", "invalid")]
    [InlineData("[{\"Stage\":\"private\",\"Counter\":0,\"Status\":0}]", "invalid")]
    [InlineData("[{\"Stage\":1,\"Counter\":false,\"Status\":0}]", "invalid")]
    [InlineData("[{\"Stage\":1,\"Counter\":1,\"Status\":\"private\"}]", "invalid")]
    public async Task Invalid_or_legacy_metadata_preserves_disk_volumes(string json, string expected)
    {
        using var source = JsonDocument.Parse(json);
        using var reply = await Read(source.RootElement.Clone());
        var data = reply.RootElement.GetProperty("data");
        Assert.Equal(50, Assert.Single(data.GetProperty("Disks").EnumerateArray()).GetProperty("FreeBytes").GetInt64());
        Assert.True(data.TryGetProperty("DiskIoDiagnostics", out var projection));
        Assert.Equal(expected, projection.GetProperty("Status").GetString());
        Assert.Empty(projection.GetProperty("Records").EnumerateArray());
    }

    [Theory]
    [InlineData(-180, "available")]
    [InlineData(-181, "unavailable")]
    [InlineData(1, "unavailable")]
    public async Task Projection_uses_sample_freshness_not_response_generation_time(int seconds, string expected)
    {
        using var reply = await Read(new[] { new { Stage = 0, Counter = 0, Status = 0u, DataStatus = (uint?)null } }, Now.AddSeconds(seconds));
        Assert.Equal(expected, reply.RootElement.GetProperty("data").GetProperty("DiskIoDiagnostics").GetProperty("Status").GetString());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(11)]
    public async Task Duplicate_or_oversized_metadata_is_rejected_without_hiding_volumes(int count)
    {
        using var reply = await Read(Enumerable.Range(0, count).Select(_ => new { Stage = 0, Counter = 0, Status = 0u }).ToArray());
        var data = reply.RootElement.GetProperty("data");
        Assert.Single(data.GetProperty("Disks").EnumerateArray());
        Assert.Equal("invalid", data.GetProperty("DiskIoDiagnostics").GetProperty("Status").GetString());
    }

    [Fact]
    public async Task Invalid_data_native_return_keeps_its_distinct_counter_status()
    {
        using var reply = await Read(new[] { new { Stage = 4, Counter = 3, Status = 0xC0000BC6u, DataStatus = (uint?)0x800007D1u } });
        var projection = reply.RootElement.GetProperty("data").GetProperty("DiskIoDiagnostics");
        Assert.Equal("available", projection.GetProperty("Status").GetString());
        var record = Assert.Single(projection.GetProperty("Records").EnumerateArray());
        Assert.Equal(0xC0000BC6u, record.GetProperty("Status").GetUInt32());
        Assert.Equal(0x800007D1u, record.GetProperty("DataStatus").GetUInt32());
    }

    [Fact]
    public async Task Legacy_sample_without_metadata_keeps_volumes_and_does_not_claim_native_success()
    {
        using var reply = await Read(null, omitMetadata: true);
        var data = reply.RootElement.GetProperty("data");
        Assert.Single(data.GetProperty("Disks").EnumerateArray());
        Assert.Equal("not_recorded", data.GetProperty("DiskIoDiagnostics").GetProperty("Status").GetString());
    }

    private static async Task<JsonDocument> Read(object? diagnostics, DateTimeOffset? sampledAt = null, bool omitMetadata = false)
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        object io = omitMetadata ? new { IsAvailable = false } : new { IsAvailable = false, ReadBytesPerSecond = 987654321, Diagnostics = diagnostics };
        var value = new
        {
            Volumes = new[] { new DiskVolumeTelemetry("C:\\", "synthetic-private-volume", "NTFS", 100, 50, 50, 50) },
            IoCounters = io,
            Health = Array.Empty<object>()
        };
        await fixture.Store.SaveTelemetrySnapshotAsync(new(Guid.NewGuid(), "disk", 1, sampledAt ?? Now,
            JsonSerializer.Serialize(new { CollectorId = "disk", Status = 1, Value = value })), default);
        var bytes = await new DashboardDispatcher(fixture.Store, new(), new(), new Clock())
            .DispatchAsync(ViewProtocol.Request("overview", new { }), default);
        var reply = JsonDocument.Parse(bytes);
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(0, fixture.Helper.Calls);
        return reply;
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
