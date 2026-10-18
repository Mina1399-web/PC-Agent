using System.Text.Json;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Service;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class IsolatedScmDashboardDispatchTests
{
    [Theory]
    [InlineData(768d, null)]
    [InlineData(767.9d, "HostAgentDevScm_00112233445566778899aabbccddeeff")]
    public void Memory_floor_decision_never_targets_the_protected_service(double availableMiB, string? expectedTarget)
    {
        Assert.Equal(expectedTarget, IsolatedScmMemoryWatchdog.GetIsolatedStopTarget(
            availableMiB, 768, "00112233445566778899aabbccddeeff"));
        Assert.NotEqual("HostAgent", IsolatedScmMemoryWatchdog.GetIsolatedStopTarget(
            availableMiB, 768, "00112233445566778899aabbccddeeff"));
    }

    [Fact]
    public void Memory_floor_decision_rejects_invalid_run_id()
        => Assert.Null(IsolatedScmMemoryWatchdog.GetIsolatedStopTarget(700, 768, "HostAgent"));

    [Fact]
    public async Task Isolated_scm_pipe_dispatches_only_overview_to_dashboard_dispatcher()
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        var dashboard = new DashboardDispatcher(fixture.Store, new(), new(), TimeProvider.System);
        var calls = 0;
        ValueTask<byte[]> Dispatch(byte[] payload, CancellationToken token)
        {
            calls++;
            return dashboard.DispatchAsync(payload, token);
        }

        using var overview = JsonDocument.Parse(await IsolatedScmDashboardDispatch.DispatchAsync(
            ViewProtocol.Request("overview", new { }), Dispatch, default));
        Assert.True(overview.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("overview", overview.RootElement.GetProperty("code").GetString());
        Assert.Equal(1, calls);

        var unsupported = new (string Command, object Body)[]
        {
            ("history", new { metric = "cpu" }),
            ("incident", new { incidentId = Guid.NewGuid().ToString("N") }),
            ("investigate", new { })
        };
        foreach (var request in unsupported)
        {
            using var rejected = JsonDocument.Parse(await IsolatedScmDashboardDispatch.DispatchAsync(
                ViewProtocol.Request(request.Command, request.Body), Dispatch, default));
            Assert.False(rejected.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("view_read_only", rejected.RootElement.GetProperty("code").GetString());
        }
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"version\":2,\"command\":\"overview\",\"body\":{}}")]
    [InlineData("{\"version\":1,\"command\":\"overview\"}")]
    public async Task Isolated_scm_pipe_rejects_malformed_view_frames_before_dispatch(string frame)
    {
        var calls = 0;
        ValueTask<byte[]> Dispatch(byte[] payload, CancellationToken token)
        {
            calls++;
            return ValueTask.FromResult(AdminProtocol.Reply(true, "unexpected_dispatch", null));
        }

        using var rejected = JsonDocument.Parse(await IsolatedScmDashboardDispatch.DispatchAsync(
            System.Text.Encoding.UTF8.GetBytes(frame), Dispatch, default));
        Assert.False(rejected.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("view_request_rejected", rejected.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, calls);
    }
}
