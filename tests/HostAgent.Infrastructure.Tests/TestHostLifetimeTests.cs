using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class TestHostLifetimeTests
{
    [Fact]
    public async Task Early_assertion_failure_waits_for_cooperative_cleanup_before_disposing_host()
    {
        var path = Path.Combine(Path.GetTempPath(), "lifetime-" + Guid.NewGuid().ToString("N") + ".tmp");
        var worker = new HoldingWorker(path);
        var host = new HostBuilder().ConfigureServices(services => services.AddSingleton<IHostedService>(_ => worker)).Build();
        var lifetime = new TestHostLifetime(host);
        var primary = new InvalidOperationException("synthetic_primary_assertion");
        try
        {
            var run = lifetime.RunAsync(async () =>
            {
                await worker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                throw primary;
            });
            await worker.Cleaning.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var completedBeforeCleanup = run.IsCompleted;
            worker.ReleaseCleanup.TrySetResult();
            var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => run);
            Assert.Same(primary, observed);
            Assert.False(completedBeforeCleanup);
            Assert.True(lifetime.StopCompleted);
            using var reopened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            worker.ReleaseCleanup.TrySetResult();
            // Also join the original host on the red path; never leave a fixture
            // handle active merely because the helper under test was defective.
            try { await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
            finally { host.Dispose(); }
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Primary_stop_and_dispose_failures_are_all_preserved_without_claiming_stop_completed()
    {
        var primary = new InvalidOperationException("synthetic_primary_assertion");
        var stopping = new IOException("synthetic_stop_failure");
        var disposing = new ApplicationException("synthetic_dispose_failure");
        var host = new FailingHost(stopping, disposing);
        var lifetime = new TestHostLifetime(host);
        var observed = await Assert.ThrowsAsync<AggregateException>(() => lifetime.RunAsync(() => Task.FromException(primary)));
        Assert.Equal(new Exception[] { primary, stopping, disposing }, observed.InnerExceptions);
        Assert.False(lifetime.StopCompleted);
        Assert.True(host.DisposeCalled);
    }

    [Fact]
    public async Task Successful_stop_remains_observed_even_when_dispose_fails()
    {
        var disposing = new ApplicationException("synthetic_dispose_failure");
        var host = new FailingHost(null, disposing);
        var lifetime = new TestHostLifetime(host);
        var observed = await Assert.ThrowsAsync<ApplicationException>(() => lifetime.RunAsync(() => Task.CompletedTask));
        Assert.Same(disposing, observed);
        Assert.True(lifetime.StopCompleted);
        Assert.True(host.DisposeCalled);
    }

    private sealed class HoldingWorker(string path) : BackgroundService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cleaning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            finally
            {
                Cleaning.TrySetResult();
                await ReleaseCleanup.Task;
            }
        }
    }

    private sealed class FailingHost(Exception? stopFailure, Exception disposeFailure) : IHost
    {
        public IServiceProvider Services { get; } = new ServiceCollection().BuildServiceProvider();
        public bool DisposeCalled { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => stopFailure is null ? Task.CompletedTask : Task.FromException(stopFailure);
        public void Dispose() { DisposeCalled = true; throw disposeFailure; }
    }
}
