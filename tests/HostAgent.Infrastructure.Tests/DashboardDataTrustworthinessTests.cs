using System.Text.Json;
using HostAgent.Core.Dashboard;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Service;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class DashboardDataTrustworthinessTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("valid", "available", null, 0d)]
    [InlineData("missing", "unknown", "missing_sample", null)]
    [InlineData("invalid", "unavailable", "invalid_sample", null)]
    [InlineData("stale", "stale", "sample_expired", null)]
    [InlineData("denied", "unavailable", "access_denied", null)]
    public async Task Memory_preserves_provenance_without_falling_back_after_a_new_failure(
        string input, string status, string? reason, double? expectedValue)
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        var at = input == "stale" ? Now.AddMinutes(-4) : Now.AddSeconds(-10);
        if (input != "missing")
        {
            await Save(fixture, "cpu-memory", at, 0, new
            {
                CpuUsagePercent = 15, TotalPhysicalMemoryBytes = input == "invalid" ? 0 : 100,
                AvailablePhysicalMemoryBytes = 100, MemoryPressurePercent = 0
            });
        }
        if (input == "denied")
        {
            at = Now;
            await fixture.Store.SaveServiceHealthAsync(new("cpu-memory", at, "Unavailable",
                """{"ErrorCode":"global_memory_status_access_denied","Private":"fixture-secret"}"""), default);
        }

        var view = await Read<DashboardSnapshot>(fixture, "overview", new { });
        var state = Assert.IsType<ViewOverviewData>(view.DataState).Memory;
        Assert.Equal("GlobalMemoryStatusEx", state.Source);
        Assert.Equal(status, state.Status);
        Assert.Equal(reason, state.Reason);
        Assert.Equal(input == "missing" ? (DateTimeOffset?)null : at, state.AtUtc);
        Assert.Equal(expectedValue, view.Memory?.Value);
    }

    [Theory]
    [InlineData(null, 0, "available", null)]
    [InlineData("service_catalog_access_denied", 0, "unavailable", "access_denied")]
    [InlineData("some_service_states_unavailable", 1, "partial", "incomplete_sample")]
    public async Task Empty_service_list_is_distinct_from_denied_and_partial_reads(
        string? warning, int rows, string status, string? reason)
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        await Save(fixture, "process-services", Now, warning is null ? 0 : 1, new
        {
            Processes = Array.Empty<object>(),
            Services = Enumerable.Repeat(new { ServiceName = "fixture", DisplayName = "Fixture", Status = 4 }, rows).ToArray(),
            ServiceChanges = Array.Empty<object>()
        }, warning);
        var view = await Read<DashboardSnapshot>(fixture, "overview", new { });
        Assert.Equal(rows, view.Services.Count);
        var states = Assert.IsType<ViewOverviewData>(view.DataState);
        Assert.Equal(status, states.Services.Status);
        Assert.Equal(reason, states.Services.Reason);
        Assert.Equal("available", states.Processes.Status);
    }

    [Fact]
    public async Task Legacy_ram_zero_requires_matching_raw_bytes_in_history()
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        var valid = Now.AddHours(-1);
        var invalid = valid.AddMinutes(-30);
        var unproven = invalid.AddMinutes(-30);
        foreach (var at in new[] { valid, invalid, unproven })
            await fixture.Store.SaveMetricSampleAsync(new(Guid.NewGuid(), "memory.used.percent", "host", at, 0), default);
        foreach (var at in new[] { valid, invalid })
            await Save(fixture, "cpu-memory", at, 0, new { TotalPhysicalMemoryBytes = at == valid ? 100 : 0, AvailablePhysicalMemoryBytes = 100 });
        var point = Assert.Single(await Read<ViewMetric[]>(fixture, "history", new { metric = "memory" }));
        Assert.Equal(valid, point.AtUtc);
        Assert.Equal(0d, point.Value);
    }

    private static async Task Save(OperatingModeTests.Fixture fixture, string collector, DateTimeOffset at,
        int status, object value, string? warning = null) =>
        await fixture.Store.SaveTelemetrySnapshotAsync(new(Guid.NewGuid(), collector, 1, at,
            JsonSerializer.Serialize(new { CollectorId = collector, Status = status, Value = value,
                Warnings = warning is null ? Array.Empty<string>() : new[] { warning } })), default);

    private static async Task<T> Read<T>(OperatingModeTests.Fixture fixture, string command, object body)
    {
        var dispatcher = new DashboardDispatcher(fixture.Store, new(), new(), new Clock());
        using var reply = JsonDocument.Parse(await dispatcher.DispatchAsync(ViewProtocol.Request(command, body), default));
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.DoesNotContain("fixture-secret", reply.RootElement.GetRawText());
        return reply.RootElement.GetProperty("data").Deserialize<T>()!;
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
