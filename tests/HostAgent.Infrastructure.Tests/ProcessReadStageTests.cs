using System.ComponentModel;
using HostAgent.Core.Orchestration;
using HostAgent.Infrastructure.Collectors;
using HostAgent.Service;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class ProcessReadStageTests
{
    // Defect: genuine non-Idle access denial is visible only as an aggregate
    // warning, so the denied operation cannot be distinguished without a new probe.
    [Theory]
    [InlineData(0,"process_identity_access_denied")]
    [InlineData(1,"process_name_access_denied")]
    [InlineData(2,"process_cpu_time_access_denied")]
    [InlineData(3,"process_working_set_access_denied")]
    [InlineData(4,"process_start_time_access_denied")]
    public void Denial_has_a_fixed_stage_reason_without_process_identity_or_exception_text(int stage,string expected)
    {
        Assert.Equal(expected,ProcessMetricFailure.ClassifyStage(new Win32Exception(5,"synthetic-private-text"),123,(ProcessMetricReadStage)stage));
    }
    [Fact]
    public void Idle_unknown_stages_and_non_denial_errors_are_not_relabelled()
    {
        Assert.Null(ProcessMetricFailure.ClassifyStage(new Win32Exception(5),0,ProcessMetricReadStage.CpuTime));
        Assert.Null(ProcessMetricFailure.ClassifyStage(new Win32Exception(87),123,ProcessMetricReadStage.CpuTime));
        Assert.Null(ProcessMetricFailure.ClassifyStage(new InvalidOperationException("Access is denied"),123,ProcessMetricReadStage.CpuTime));
        Assert.Null(ProcessMetricFailure.ClassifyStage(new Win32Exception(5),123,(ProcessMetricReadStage)999));
        Assert.Equal("some_process_metrics_access_denied",ProcessMetricFailure.Classify(new Win32Exception(5),123));
    }
    [Theory]
    [InlineData("process_identity_access_denied")]
    [InlineData("process_name_access_denied")]
    [InlineData("process_cpu_time_access_denied")]
    [InlineData("process_working_set_access_denied")]
    [InlineData("process_start_time_access_denied")]
    public void Bounded_stage_reason_reaches_readonly_health_without_hiding_degradation(string reason)
    {
        var health=new AgentRuntimeHealth();
        health.SetCollectors([new("process-services",CollectorCycleStatus.Partial,null,0,null)
            {WarningCodes=["some_process_metrics_access_denied",reason,"synthetic-private-text"]}]);
        var component=Assert.Single(health.Snapshot(),x=>x.Component=="process-services");
        Assert.True(health.Degraded); Assert.True(component.Degraded);
        Assert.Equal(new[]{"some_process_metrics_access_denied",reason},component.Reasons);
    }
}
