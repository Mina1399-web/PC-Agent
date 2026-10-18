using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;
using Xunit;

namespace HostAgent.Infrastructure.Tests;
public sealed class IsolatedCollectorFaultTests
{
    [Fact]
    public async Task Private_failure_does_not_call_inner_and_success_after_each_failure_counts_recovery()
    {
        var inner = new CountingJob(); var fail = true;
        var job = new IsolatedCollectorFaultJob(inner, () => fail);
        await Assert.ThrowsAsync<InvalidOperationException>(() => job.CollectAndPersistAsync(default).AsTask());
        Assert.Equal(0, inner.Calls); Assert.Equal(1, job.InjectedFailures);
        fail = false;
        Assert.Equal(CollectorStatus.Success, await job.CollectAndPersistAsync(default));
        Assert.Equal(1, job.Recoveries);
        await job.CollectAndPersistAsync(default); Assert.Equal(1, job.Recoveries);
        fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => job.CollectAndPersistAsync(default).AsTask());
        fail = false; await job.CollectAndPersistAsync(default);
        Assert.Equal(2, job.Recoveries); Assert.Equal(2, job.InjectedFailures);
    }
    private sealed class CountingJob : ITelemetryCollectionJob
    {
        public int Calls;
        public string CollectorId => "cpu-memory";
        public ValueTask<CollectorStatus> CollectAndPersistAsync(CancellationToken token)
        { Calls++; return ValueTask.FromResult(CollectorStatus.Success); }
    }
}
