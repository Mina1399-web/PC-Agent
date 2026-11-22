using System.Text.Json;
using HostAgent.Core.Dashboard;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Service;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class NetworkDiagnosticsTests
{
    private const string PrivateValue = "fixture-private-192.0.2.1";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly NetworkAdapterTelemetry Adapter =
        new(PrivateValue, PrivateValue, PrivateValue, "Up", [PrivateValue], [PrivateValue], [PrivateValue, PrivateValue]);

    [Fact]
    public async Task Bounded_projection_uses_existing_samples_without_private_values_or_helper_calls()
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        await Save(fixture, Now, new NetworkTelemetry(Enumerable.Repeat(Adapter, 25).ToArray(),
        [
            new(NetworkProbeKind.Gateway, PrivateValue, NetworkProbeStatus.Succeeded, TimeSpan.Zero, 0, 0, null),
            new(NetworkProbeKind.InternetIp, PrivateValue, NetworkProbeStatus.Succeeded, TimeSpan.Zero, 10, 33.3, null),
            new(NetworkProbeKind.DnsResolution, PrivateValue, NetworkProbeStatus.Succeeded, TimeSpan.Zero, 0, 33.3, null)
        ]));
        var report = await Read(fixture);
        Assert.Equal("partial", report.Configuration.Status);
        Assert.True(report.AdapterLimitReached);
        Assert.Equal(16, report.Adapters.Count);
        Assert.All(report.Adapters, row => Assert.Equal(new ViewNetworkAdapter("Up", 1, 2), row));
        Assert.Equal(Now, report.Configuration.AtUtc);
        Assert.Collection(report.Probes,
            gateway => { Assert.Equal("available", gateway.State.Status); Assert.Equal(0d, gateway.Milliseconds); Assert.Equal(0d, gateway.PacketLossPercent); },
            internet => { Assert.Equal("partial", internet.State.Status); Assert.Equal("response_loss", internet.State.Reason); Assert.Equal(33.3, internet.PacketLossPercent); },
            dns => { Assert.Equal("DNS", dns.State.Source); Assert.Equal(0d, dns.Milliseconds); Assert.Null(dns.PacketLossPercent); });
        Assert.Equal(0, fixture.Helper.Calls);
        Assert.Throws<InvalidDataException>(() => ViewProtocol.Request("network-diagnostics", new { target = PrivateValue }));
    }

    [Fact]
    public async Task Disabled_missing_gateway_and_timeout_keep_distinct_reasons()
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        await Save(fixture, Now, new NetworkTelemetry([],
        [
            new(NetworkProbeKind.Gateway, PrivateValue, NetworkProbeStatus.Unavailable, TimeSpan.Zero, null, null, "gateway_not_configured"),
            new(NetworkProbeKind.InternetIp, PrivateValue, NetworkProbeStatus.Unavailable, TimeSpan.Zero, null, null, "network_probes_disabled"),
            new(NetworkProbeKind.DnsResolution, PrivateValue, NetworkProbeStatus.TimedOut, TimeSpan.Zero, null, null, "dns_timed_out")
        ]));
        var report = await Read(fixture);
        Assert.Equal("available", report.Configuration.Status);
        Assert.Empty(report.Adapters);
        Assert.Equal(new[] { "gateway_missing", "probe_disabled", "read_timeout" }, report.Probes.Select(x => x.State.Reason));
        Assert.All(report.Probes, probe => { Assert.Equal("unavailable", probe.State.Status); Assert.Null(probe.Milliseconds); });
    }

    [Theory]
    [InlineData("legacy", "unknown", "missing_sample")]
    [InlineData("stale", "stale", "sample_expired")]
    [InlineData("future", "unknown", "missing_sample")]
    [InlineData("denied", "unavailable", "access_denied")]
    public async Task Legacy_stale_future_and_newer_failure_do_not_publish_valid_current_data(
        string input, string status, string reason)
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        if (input == "legacy") await Save(fixture, Now, new { });
        else
        {
            var at = input == "future" ? Now.AddMinutes(1) : input == "stale" ? Now.AddMinutes(-4) : Now.AddSeconds(-1);
            await Save(fixture, at, new NetworkTelemetry([Adapter], []));
        }

        if (input == "denied")
            await fixture.Store.SaveServiceHealthAsync(new("network", Now, "Unavailable",
                """{"ErrorCode":"network_adapters_access_denied","Private":"fixture-private-192.0.2.1"}"""), default);

        var report = await Read(fixture);
        Assert.Equal(status, report.Configuration.Status);
        Assert.Equal(reason, report.Configuration.Reason);
        Assert.Empty(report.Adapters);
        Assert.All(report.Probes, probe =>
        {
            Assert.Equal(status, probe.State.Status);
            Assert.Equal(reason, probe.State.Reason);
            Assert.Null(probe.Milliseconds);
            Assert.Null(probe.PacketLossPercent);
        });
        if (input == "denied") Assert.Equal(Now, report.Configuration.AtUtc);
    }

    private static async Task Save(OperatingModeTests.Fixture fixture, DateTimeOffset at, object value) =>
        await fixture.Store.SaveTelemetrySnapshotAsync(new(Guid.NewGuid(), "network", 1, at,
            JsonSerializer.Serialize(new { CollectorId = "network", Status = 0, Value = value, Warnings = Array.Empty<string>() })), default);

    private static async Task<ViewNetworkDiagnostics> Read(OperatingModeTests.Fixture fixture)
    {
        var dispatcher = new DashboardDispatcher(fixture.Store, new(), new(), new FixedClock());
        var controller = new DashboardController(async (request, token) =>
        {
            var bytes = await dispatcher.DispatchAsync(request, token);
            Assert.InRange(bytes.Length, 1, HelperProtocol.MaximumFrameBytes);
            Assert.DoesNotContain(PrivateValue, System.Text.Encoding.UTF8.GetString(bytes));
            using var frame = new MemoryStream();
            await HelperProtocol.WriteFrameAsync(frame, bytes, token);
            frame.Position = 0;
            return await HelperProtocol.ReadFrameAsync(frame, token);
        });
        await controller.RefreshAsync(default);
        Assert.Equal("connected", controller.State);
        var diagnostics = await controller.ReadAsync<ViewNetworkDiagnostics>("network-diagnostics", new { }, default);
        Assert.Equal("connected", controller.State);
        return Assert.IsType<ViewNetworkDiagnostics>(diagnostics);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
