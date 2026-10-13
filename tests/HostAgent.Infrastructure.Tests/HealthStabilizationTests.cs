using System.Text.Json;
using HostAgent.Cli;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Core.Orchestration;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Storage;
using HostAgent.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class HealthStabilizationTests
{
    [Fact]
    public async Task Dashboard_exposes_bounded_allowlisted_partial_reasons_without_raw_warning_text()
    {
        var health = new AgentRuntimeHealth();
        health.SetCollectors([new CollectorCycleResult("disk", CollectorCycleStatus.Partial, null, 0, null)
        { WarningCodes = ["smart_health_unavailable", "volume_metrics_unavailable", "synthetic-sensitive-error"] }]);
        var disk = Assert.Single(health.Snapshot(), value => value.Component == "disk");
        Assert.Equal(new[] { "smart_health_unavailable", "volume_metrics_unavailable" }, disk.Reasons);
        var path = Path.Combine(Path.GetTempPath(), "dashboard-health-" + Guid.NewGuid() + ".db");
        try
        {
            await using var store = new SqliteHostAgentStore(path);
            await store.InitializeAsync(default);
            var bytes = await new DashboardDispatcher(store, new(), health, TimeProvider.System)
                .DispatchAsync(HostAgent.Infrastructure.Dashboard.ViewProtocol.Request("overview", new { }), default);
            using var response = JsonDocument.Parse(bytes);
            var view = response.RootElement.GetProperty("data").Deserialize<HostAgent.Core.Dashboard.DashboardSnapshot>()!;
            var projected = Assert.Single(view.Health, value => value.Component == "disk");
            Assert.Equal(disk.Reasons, projected.Reasons);
            Assert.DoesNotContain("synthetic-sensitive-error", response.RootElement.GetRawText());
        }
        finally { File.Delete(path); }
    }

    // A latched component failure would keep a recovered agent degraded indefinitely.
    [Fact]
    public async Task Mandatory_collector_health_recovers_after_failure_and_backoff_without_hiding_other_workers()
    {
        var path = Path.Combine(Path.GetTempPath(), "health-recovery-" + Guid.NewGuid() + ".db");
        try
        {
            await using var store = new SqliteHostAgentStore(path);
            var system = new SystemCollector(CollectorStatus.Success);
            var now = DateTimeOffset.UtcNow;
            var runner = new CollectorCycleRunner(
                [new TelemetryCollectionJob<DiskTelemetry>(new DiskCollector("smart_health_unavailable"), store),
                 new TelemetryCollectionJob<SystemTelemetry>(system, store)],
                TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromHours(1), () => now);
            var health = new AgentRuntimeHealth();
            health.SetCollectors(await runner.RunOnceAsync(default));
            Assert.False(health.Degraded);

            system.Status = CollectorStatus.Unavailable;
            health.SetCollectors(await runner.RunOnceAsync(default));
            Assert.True(health.Degraded);
            Assert.Equal("collector_unavailable", Assert.Single(health.Snapshot(), c => c.Component == "system").Code);
            system.Status = CollectorStatus.Success;
            health.SetCollectors(await runner.RunOnceAsync(default));
            Assert.True(health.Degraded);
            Assert.Equal("backoff_active", Assert.Single(health.Snapshot(), c => c.Component == "system").Code);

            health.Set("diagnostics", true);
            now = now.AddMinutes(1);
            health.SetCollectors(await runner.RunOnceAsync(default));
            Assert.Equal("collector_healthy", Assert.Single(health.Snapshot(), c => c.Component == "system").Code);
            Assert.False(Assert.Single(health.Snapshot(), c => c.Component == "collectors").Degraded);
            Assert.Equal("optional_smart_unavailable", Assert.Single(health.Snapshot(), c => c.Component == "disk").Code);
            Assert.True(health.Degraded);
            health.Set("diagnostics", false);
            Assert.False(health.Degraded);
        }
        finally { File.Delete(path); }
    }

    // Optional SMART alone must not hide real missing disk/system telemetry.
    [Theory]
    [InlineData("smart_health_unavailable", CollectorStatus.Success, false, "optional_smart_unavailable")]
    [InlineData("volume_metrics_unavailable", CollectorStatus.Success, true, "collector_partial")]
    [InlineData("disk_io_counters_unavailable", CollectorStatus.Success, true, "collector_partial")]
    [InlineData("smart_health_unavailable", CollectorStatus.Unavailable, true, "optional_smart_unavailable")]
    [InlineData("smart_health_unavailable", CollectorStatus.Partial, true, "optional_smart_unavailable")]
    public async Task Collector_health_distinguishes_optional_smart_from_required_failures(
        string diskWarning, CollectorStatus systemStatus, bool degraded, string diskCode)
    {
        var path = Path.Combine(Path.GetTempPath(), "health-" + Guid.NewGuid() + ".db");
        try
        {
            await using var store = new SqliteHostAgentStore(path);
            using var processor = new DiagnosticCycleProcessor(store);
            var health = new AgentRuntimeHealth();
            ITelemetryCollectionJob[] jobs =
            [new TelemetryCollectionJob<DiskTelemetry>(new DiskCollector(diskWarning), store),
             new TelemetryCollectionJob<SystemTelemetry>(new SystemCollector(systemStatus), store)];
            var type = typeof(AgentRuntimeHealth).Assembly.GetType("HostAgent.Service.CollectionCoordinator")!;
            var logger = Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(type))!;
            using var coordinator = (IDisposable)Activator.CreateInstance(type, store, jobs, processor,
                logger, null, health, new ServiceConfiguration())!;
            await (ValueTask)type.GetMethod("RunCycleAsync")!.Invoke(coordinator, [null, CancellationToken.None])!;
            Assert.Equal(degraded, health.Degraded);

            var helper = new OperatingModeTests.FakeHelper();
            using var remediation = new RemediationCoordinator(store, helper, new(), TimeProvider.System);
            var dispatcher = new AdminDispatcher(store, remediation, new(), health, (_, _, _) => ValueTask.CompletedTask);
            using var response = JsonDocument.Parse(await dispatcher.DispatchAsync(
                JsonSerializer.SerializeToUtf8Bytes(new { version = 1, command = "status", body = new { verbose = true } }), default));
            Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
            var data = response.RootElement.GetProperty("data");
            Assert.Equal(degraded, data.GetProperty("degraded").GetBoolean());
            var components = data.GetProperty("health").EnumerateArray().ToArray();
            Assert.InRange(components.Length, 2, 16);
            var disk = Assert.Single(components, item => item.GetProperty("component").GetString() == "disk");
            Assert.Equal(diskCode, disk.GetProperty("code").GetString());
            Assert.Equal(diskCode != "optional_smart_unavailable", disk.GetProperty("degraded").GetBoolean());
            var system = Assert.Single(components, item => item.GetProperty("component").GetString() == "system");
            Assert.Equal(systemStatus != CollectorStatus.Success, system.GetProperty("degraded").GetBoolean());
            Assert.DoesNotContain("synthetic-sensitive-error", response.RootElement.GetRawText());
            Assert.Equal(0, helper.Calls);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Verbose_status_is_opt_in_and_cli_sends_exact_read_only_request()
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        var health = new AgentRuntimeHealth();
        health.Set("diagnostics", true);
        using var remediation = new RemediationCoordinator(fixture.Store, fixture.Helper, new(), TimeProvider.System);
        var dispatcher = new AdminDispatcher(fixture.Store, remediation, new(), health, (_, _, _) => ValueTask.CompletedTask);
        using var normal = JsonDocument.Parse(await dispatcher.DispatchAsync(AdminProtocol.Request("status", new { }), default));
        Assert.False(normal.RootElement.GetProperty("data").TryGetProperty("health", out _));

        var output = new StringWriter();
        var calls = 0;
        var cli = new CliApplication((bytes, token) =>
        {
            calls++;
            var message = AdminProtocol.Parse(bytes);
            Assert.Equal("status", message.Command);
            Assert.True(message.Body.GetProperty("verbose").GetBoolean());
            return dispatcher.DispatchAsync(bytes, token);
        }, _ => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException(),
            (_, _) => throw new InvalidOperationException(), output, Path.GetTempPath());
        Assert.Equal(0, await cli.RunAsync(["status", "--verbose"], default));
        Assert.Contains("diagnostics_unavailable", output.ToString());
        Assert.Equal(2, await cli.RunAsync(["status", "--verbose", "unexpected"], default));
        Assert.Equal(1, calls);
        Assert.Equal(0, fixture.Helper.Calls);
    }

    [Theory]
    [InlineData("{\"verbose\":\"true\"}")]
    [InlineData("{\"verbose\":true,\"verbose\":true}")]
    [InlineData("{\"verbose\":true,\"extra\":1}")]
    public void Status_rejects_untyped_duplicate_or_extra_verbose_fields(string body)
    {
        Assert.Throws<InvalidDataException>(() => AdminProtocol.Parse(
            System.Text.Encoding.UTF8.GetBytes("{\"version\":1,\"command\":\"status\",\"body\":" + body + "}")));
    }

    private sealed class DiskCollector(string warning) : ITelemetryCollector<DiskTelemetry>
    {
        public string CollectorId => "disk";
        public ValueTask<CollectorResult<DiskTelemetry>> CollectAsync(CancellationToken token) =>
            ValueTask.FromResult(CollectorResult<DiskTelemetry>.Partial(CollectorId, DateTimeOffset.UtcNow,
                new([new("C:\\", "test", "NTFS", 100000000000, 10000000000, 90000000000, 10)],
                    new(true, 0, 0, 0, null), []), ["smart_health_unavailable", warning]));
    }
    private sealed class SystemCollector(CollectorStatus status) : ITelemetryCollector<SystemTelemetry>
    {
        public string CollectorId => "system";
        public CollectorStatus Status { get; set; } = status;
        public ValueTask<CollectorResult<SystemTelemetry>> CollectAsync(CancellationToken token)
        {
            var value = new SystemTelemetry("Windows", "10", "test", DateTimeOffset.UtcNow.AddHours(-1), TimeSpan.FromHours(1), null);
            return ValueTask.FromResult(Status switch
            {
                CollectorStatus.Unavailable => CollectorResult<SystemTelemetry>.Unavailable(CollectorId, DateTimeOffset.UtcNow, "synthetic-sensitive-error"),
                CollectorStatus.Partial => CollectorResult<SystemTelemetry>.Partial(CollectorId, DateTimeOffset.UtcNow, value, ["synthetic-sensitive-error"]),
                _ => CollectorResult<SystemTelemetry>.Success(CollectorId, DateTimeOffset.UtcNow, value)
            });
        }
    }
}
