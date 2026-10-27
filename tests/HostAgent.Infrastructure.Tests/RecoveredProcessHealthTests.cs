using HostAgent.Core.Orchestration;
using HostAgent.Service;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class RecoveredProcessHealthTests
{
    // Only the explicit counter-recovered/unknown UTC identity capability is
    // optional. Actual resource loss, service failures and mixed errors remain faults.
    [Fact]
    public void Recovered_resources_keep_unavailable_exact_identity_visible_without_an_agent_fault()
    {
        var health = new AgentRuntimeHealth();
        health.SetCollectors([
            new("cpu-memory", CollectorCycleStatus.Succeeded, null, 0, null),
            new("process-services", CollectorCycleStatus.Partial, null, 0, null)
            { WarningCodes = ["process_identity_unavailable", "some_executable_paths_unavailable"] }
        ]);
        var component = Assert.Single(health.Snapshot(), x => x.Component == "process-services");
        Assert.Equal("optional_process_identity_unavailable", component.Code);
        Assert.False(component.Degraded);
        Assert.Equal(new[] { "process_identity_unavailable", "some_executable_paths_unavailable" }, component.Reasons);
        Assert.False(health.Degraded);
        Assert.Equal("healthy", Assert.Single(health.Snapshot(), x => x.Component == "collectors").Code);
    }

    [Theory]
    [InlineData("process_list_unavailable")]
    [InlineData("service_catalog_unavailable")]
    [InlineData("some_process_metrics_unavailable")]
    [InlineData("some_process_metrics_access_denied")]
    [InlineData("process_cpu_time_access_denied")]
    [InlineData("some_service_states_unavailable")]
    [InlineData("unknown_private_warning")]
    public void Optional_identity_cannot_hide_any_other_collection_failure(string warning)
    {
        var health = new AgentRuntimeHealth();
        health.SetCollectors([new("process-services", CollectorCycleStatus.Partial, null, 0, null)
        { WarningCodes = ["process_identity_unavailable", warning] }]);
        Assert.True(health.Degraded);
        Assert.True(Assert.Single(health.Snapshot(), x => x.Component == "process-services").Degraded);
    }

    [Theory]
    [InlineData("cpu-memory", CollectorCycleStatus.Partial)]
    [InlineData("process-services", CollectorCycleStatus.Unavailable)]
    [InlineData("process-services", CollectorCycleStatus.Failed)]
    [InlineData("process-services", CollectorCycleStatus.Skipped)]
    public void Identity_capability_exception_is_limited_to_partial_process_collection(string component, CollectorCycleStatus status)
    {
        var health = new AgentRuntimeHealth();
        health.SetCollectors([new(component, status, null, 0, null) { WarningCodes = ["process_identity_unavailable"] }]);
        Assert.True(health.Degraded);
    }
}
