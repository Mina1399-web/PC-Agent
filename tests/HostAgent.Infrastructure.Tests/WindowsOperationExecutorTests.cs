using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Security;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class WindowsOperationExecutorTests
{
    [Fact]
    public void Native_adapter_rejects_self_and_system_pid_without_side_effects()
    {
        var type = typeof(WindowsOperationExecutor).Assembly.GetType("HostAgent.Infrastructure.Security.WindowsOperationApi");
        Assert.NotNull(type);
        var api = Assert.IsAssignableFrom<IWindowsOperationApi>(Activator.CreateInstance(type));
        Assert.Throws<UnauthorizedAccessException>(() => api.OpenProcess(Environment.ProcessId));
        Assert.Throws<UnauthorizedAccessException>(() => api.OpenProcess(4));
    }
    [Theory]
    [InlineData(HelperOperation.StartService, "FixtureService:True")]
    [InlineData(HelperOperation.StopService, "FixtureService:False")]
    [InlineData(HelperOperation.RestartService, "FixtureService:False,FixtureService:True")]
    public async Task Service_handler_uses_only_local_binding_and_ordered_typed_steps(HelperOperation operation, string expected)
    {
        var api = new FakeApi(); var executor = new WindowsOperationExecutor(api);
        await executor.ExecuteAsync(Task3Fixture.Request() with { Operation = operation }, new("service:test-service", "FixtureService", null, null), default);
        Assert.Equal(expected, string.Join(',', api.ServiceCalls));
    }
    [Fact]
    public async Task Process_handler_requires_pinned_pid_creation_time_path_hash_and_noncritical_identity()
    {
        var api = new FakeApi(); var executor = new WindowsOperationExecutor(api);
        var request = Task3Fixture.Request() with { Operation = HelperOperation.TerminateProcess, Parameters = new ProcessParameters("worker", 1234, 1000) };
        var binding = new OperationBinding("process:worker", null, @"C:\Trusted\worker.exe", new string('A', 64));
        foreach (var identity in new[] {
            api.Identity with { Pid = 9999 }, api.Identity with { StartTimeUtcTicks = 1001 },
            api.Identity with { ExecutablePath = @"C:\Other\worker.exe" },
            api.Identity with { Sha256 = new string('B', 64) }, api.Identity with { IsCritical = true } })
        {
            api.Identity = identity;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => executor.ExecuteAsync(request, binding, default).AsTask());
        }
        Assert.Equal(0, api.Terminations);
        api.Identity = new(1234, 1000, @"C:\Trusted\worker.exe", new string('A', 64), false);
        await executor.ExecuteAsync(request, binding, default); Assert.Equal(1, api.Terminations);
        Assert.Equal(6, api.Disposals);
    }
    [Fact]
    public async Task Cancellation_before_or_between_restart_steps_prevents_further_side_effects()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var api = new FakeApi(); var executor = new WindowsOperationExecutor(api);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(Task3Fixture.Request(),
            new("service:test-service", "FixtureService", null, null), cancellation.Token).AsTask());
        Assert.Empty(api.ServiceCalls);
        using var midway = new CancellationTokenSource(); api.AfterStop = midway.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(Task3Fixture.Request(),
            new("service:test-service", "FixtureService", null, null), midway.Token).AsTask());
        Assert.Equal(new[] { "FixtureService:False" }, api.ServiceCalls);
    }
    private sealed class FakeApi : IWindowsOperationApi
    {
        public List<string> ServiceCalls { get; } = []; public int Terminations; public int Disposals; public Action? AfterStop;
        public ProcessIdentity Identity = new(1234, 1000, @"C:\Trusted\worker.exe", new string('A', 64), false);
        public ValueTask SetServiceRunningAsync(string name, bool running, CancellationToken cancellationToken)
        { ServiceCalls.Add(name + ":" + running); if (!running) AfterStop?.Invoke(); return ValueTask.CompletedTask; }
        public IProcessLease OpenProcess(int pid) => new Lease(this, Identity);
        private sealed class Lease(FakeApi api, ProcessIdentity identity) : IProcessLease
        { public ProcessIdentity Identity => identity; public void Terminate() => api.Terminations++; public void Dispose() => api.Disposals++; }
    }
}
