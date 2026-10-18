using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using HostAgent.Core.Orchestration;
using HostAgent.Infrastructure.Collectors;
using HostAgent.Service;
using Xunit;
using Xunit.Abstractions;

namespace HostAgent.Infrastructure.Tests;

public sealed class ProcessMetricFailureTests(ITestOutputHelper output)
{
    // Treating every process read error as access denial would hide genuine
    // collector failures; only the exact native error may receive that reason.
    [Theory]
    [InlineData(5, "some_process_metrics_access_denied")]
    [InlineData(6, "some_process_metrics_unavailable")]
    [InlineData(87, "some_process_metrics_unavailable")]
    [InlineData(299, "some_process_metrics_unavailable")]
    public void Native_error_code_preserves_access_denial_and_other_failures(int nativeCode, string expected)
    {
        Assert.Equal(expected, Classify(new Win32Exception(nativeCode, "synthetic process read failure")));
    }

    [Fact]
    public void Error_message_does_not_determine_access_denial_or_escape_into_the_warning()
    {
        Assert.Equal("some_process_metrics_access_denied", Classify(new Win32Exception(5, "unrelated synthetic text")));
        Assert.Equal("some_process_metrics_unavailable", Classify(new Win32Exception(87, "Access is denied")));
    }

    [Fact]
    public void Exited_process_and_non_native_failures_remain_general_unavailability()
    {
        Assert.Equal("some_process_metrics_unavailable", Classify(new InvalidOperationException("Process has exited")));
        Assert.Equal("some_process_metrics_unavailable", Classify(new NotSupportedException("synthetic unsupported API")));
        Assert.Equal("some_process_metrics_unavailable", Classify(new UnauthorizedAccessException("synthetic denial without native code")));
        Assert.Equal("some_process_metrics_unavailable", Classify(new Exception("synthetic unexpected failure")));
    }

    [Fact]
    public void Precise_access_denial_reason_remains_partial_and_degrading()
    {
        var health = new AgentRuntimeHealth();
        health.SetCollectors([new CollectorCycleResult("cpu-memory", CollectorCycleStatus.Partial, null, 0, null)
        { WarningCodes = ["some_process_metrics_access_denied"] }]);

        var component = Assert.Single(health.Snapshot(), value => value.Component == "cpu-memory");
        Assert.Equal("collector_partial", component.Code);
        Assert.True(component.Degraded);
        Assert.True(health.Degraded);
        Assert.Equal(new[] { "some_process_metrics_access_denied" }, component.Reasons);
    }

    [Fact]
    public async Task Current_read_only_collectors_preserve_available_host_and_process_service_telemetry()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var cpu = await new WindowsCpuMemoryCollector().CollectAsync(deadline.Token);
        var processes = await new WindowsProcessServicesCollector().CollectAsync(deadline.Token);
        Assert.NotNull(cpu.Value);
        Assert.NotNull(processes.Value);
        Assert.True(cpu.Value.TotalPhysicalMemoryBytes > 0);
        Assert.NotEmpty(cpu.Value.TopProcessesByMemory);
        Assert.NotEmpty(processes.Value.Processes);
        Assert.NotEmpty(processes.Value.Services);
        Assert.Contains(processes.Value.Processes, process => process.ProcessId == Environment.ProcessId);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            identityScope = "current_test_process_not_installed_service",
            cpuStatus = cpu.Status.ToString(), cpuWarnings = cpu.Warnings,
            processStatus = processes.Status.ToString(), processWarnings = processes.Warnings,
            observedProcessCount = processes.Value.Processes.Count,
            observedServiceCount = processes.Value.Services.Count
        }));
    }

    private static string Classify(Exception error)
    {
        var type = typeof(WindowsCpuMemoryCollector).Assembly.GetType("HostAgent.Infrastructure.Collectors.ProcessMetricFailure");
        Assert.NotNull(type);
        var classify = type.GetMethod("Classify", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(classify);
        return Assert.IsType<string>(classify.Invoke(null, [error]));
    }
}
