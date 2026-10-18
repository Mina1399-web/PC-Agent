using HostAgent.Service;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class CompleteStorageSafetyRegressionTests
{
    [Fact]
    public void Unrelated_initial_degradation_is_not_proof_of_storage_fault()
    {
        ComponentHealth[] baseline = [new("system", "collector_healthy", false, [])];
        ComponentHealth[] unrelated = [baseline[0], new("cpu-memory", "collector_partial", true, ["cpu_baseline_not_ready"])];
        Assert.False(CompleteStorageProbe.ComponentHasStorageFailure(baseline, unrelated));
        Assert.True(CompleteStorageProbe.ComponentHasStorageFailure(baseline, [new("system", "collector_exception", true, [])]));
    }
    [Fact]
    public void Queued_collector_request_does_not_prove_actual_overlapping_faults()
    {
        Assert.False(CompleteSoakProbe.ConcurrentFaultsVerified(6, 0, 0));
        Assert.False(CompleteSoakProbe.ConcurrentFaultsVerified(6, 6, 0));
        Assert.True(CompleteSoakProbe.ConcurrentFaultsVerified(6, 6, 6));
    }
    [Fact]
    public void Increasing_historical_snapshot_total_does_not_prove_current_worker_progress()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(CompleteSoakProbe.ProgressVerified(now.AddMinutes(-2), now, 101, 100));
        Assert.False(CompleteSoakProbe.ProgressVerified(now.AddSeconds(1), now, 101, 100));
        Assert.False(CompleteSoakProbe.ProgressVerified(now, now, 100, 100));
        Assert.True(CompleteSoakProbe.ProgressVerified(now.AddSeconds(-5), now, 101, 100));
    }
}
