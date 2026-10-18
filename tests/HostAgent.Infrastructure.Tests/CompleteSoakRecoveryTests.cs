using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Core.Orchestration;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class CompleteSoakRecoveryTests
{
    [Fact]
    public async Task Combined_fault_cannot_arm_storage_while_preexisting_CPU_collection_is_in_flight()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var full = false; var queued = false; var calls = 0;
        var inner = new AsyncJob(async () =>
        {
            calls++; started.TrySetResult(); await release.Task;
            Assert.False(full); // A pre-arm write must finish before FULL can consume this collector's retry budget.
            return CollectorStatus.Success;
        });
        var job = new IsolatedCollectorFaultJob(inner, () => { var value=queued; queued=false; return value; });
        var first = job.CollectAndPersistAsync(default).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var arm = job.ArmBetweenCollectionsAsync(() => { full=true; queued=true; }, default);
        var incorrectlyArmed = arm.IsCompleted;
        release.SetResult();
        try { await first; } catch when(incorrectlyArmed) { }
        Assert.False(incorrectlyArmed);
        await arm.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>job.CollectAndPersistAsync(default).AsTask());
        Assert.Equal(1,job.InjectedFailures);
        Assert.Equal(1,calls);
    }
    [Fact]
    public async Task In_flight_retry_is_pending_and_only_actual_fresh_recovery_passes()
    {
        var now = DateTimeOffset.Parse("2026-10-18T00:00:00Z");
        var start = now;
        var fail = true;
        var runner = new CollectorCycleRunner([new Job(() => fail)], TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60), TimeSpan.FromHours(1), () => now);
        now = start.AddSeconds(215);
        var failed = Assert.Single(await runner.RunOnceAsync(default));
        Assert.Equal(start.AddSeconds(275), failed.RetryAtUtc);
        fail = false;
        now = start.AddSeconds(274);
        Assert.Equal(CollectorCycleStatus.Skipped, Assert.Single(await runner.RunOnceAsync(default)).Status);
        Assert.Equal(CompleteSoakProbe.RecoveryDecision.Pending,
            CompleteSoakProbe.RecoveryCheckpoint(false, false, TimeSpan.FromSeconds(59), TimeSpan.FromSeconds(100)));
        now = start.AddSeconds(285); // The next real scheduled cycle can finish after the old t280 assertion.
        var recovered = Assert.Single(await runner.RunOnceAsync(default));
        Assert.Equal(CollectorCycleStatus.Succeeded, recovered.Status);
        Assert.Equal(0, recovered.ConsecutiveFailures);
        Assert.Equal(CompleteSoakProbe.RecoveryDecision.Recovered,
            CompleteSoakProbe.RecoveryCheckpoint(true, true, TimeSpan.FromSeconds(70), TimeSpan.FromSeconds(100)));
    }

    [Fact]
    public void Recovery_never_accepts_stale_data_or_revoked_fault_without_recovery()
    {
        Assert.Equal(CompleteSoakProbe.RecoveryDecision.Pending,
            CompleteSoakProbe.RecoveryCheckpoint(false, true, TimeSpan.FromSeconds(80), TimeSpan.FromSeconds(100)));
        Assert.Equal(CompleteSoakProbe.RecoveryDecision.Pending,
            CompleteSoakProbe.RecoveryCheckpoint(true, false, TimeSpan.FromSeconds(80), TimeSpan.FromSeconds(100)));
        Assert.Equal(CompleteSoakProbe.RecoveryDecision.Failed,
            CompleteSoakProbe.RecoveryCheckpoint(false, false, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(100)));
    }

    [Fact]
    public void Cycle_waits_for_new_samples_from_both_collectors_before_each_next_fault()
    {
        var now=DateTimeOffset.Parse("2026-10-18T00:00:00Z");
        var a=new CompleteSoakObservation(10,Guid.NewGuid(),Guid.NewGuid(),now,now,now,0,0,0,false,true);
        var cycle=new CompleteSoakCycle(0,TimeSpan.Zero);
        Assert.True(cycle.Advance(TimeSpan.FromSeconds(10),a));
        Assert.Equal(CompleteSoakCycle.Phase.CollectorFault,cycle.Stage);
        a=a with {Failures=1};
        Assert.True(cycle.Advance(TimeSpan.FromSeconds(11),a));
        Assert.False(cycle.Advance(TimeSpan.FromSeconds(80),a with {Recoveries=1,Snapshots=100}));
        Assert.Equal(CompleteSoakCycle.Phase.CollectorRecovery,cycle.Stage);
        a=a with {Recoveries=1,Snapshots=20,CpuId=Guid.NewGuid(),SystemId=Guid.NewGuid()};
        Assert.True(cycle.Advance(TimeSpan.FromSeconds(90),a));
        Assert.Equal(CompleteSoakCycle.Phase.WriteFault,cycle.Stage);
        a=a with {WriteFailures=1};
        Assert.True(cycle.Advance(TimeSpan.FromSeconds(91),a));
        Assert.False(cycle.Advance(TimeSpan.FromSeconds(100),a with {CpuId=Guid.NewGuid(),Snapshots=30}));
        a=a with {CpuId=Guid.NewGuid(),SystemId=Guid.NewGuid(),Snapshots=40};
        Assert.True(cycle.Advance(TimeSpan.FromSeconds(170),a));
        Assert.Equal(CompleteSoakCycle.Phase.CombinedFault,cycle.Stage);
        cycle.MarkArmed(TimeSpan.FromSeconds(170));
        Assert.False(cycle.Advance(TimeSpan.FromSeconds(179),a with {Failures=2,ActualTripleOverlap=true}));
        a=a with {Failures=2,ActualTripleOverlap=true};
        Assert.True(cycle.Advance(TimeSpan.FromSeconds(180),a));
        a=a with {Recoveries=2,Snapshots=50,CpuId=Guid.NewGuid(),SystemId=Guid.NewGuid()};
        Assert.True(cycle.Advance(TimeSpan.FromSeconds(250),a));
        Assert.Equal(CompleteSoakCycle.Phase.Completed,cycle.Stage);
    }

    [Fact]
    public void Missing_fault_or_recovery_cannot_run_past_the_declared_phase_budget()
    {
        var now=DateTimeOffset.UtcNow;
        var a=new CompleteSoakObservation(1,Guid.NewGuid(),Guid.NewGuid(),now,now,now,0,0,0,false,true);
        var cycle=new CompleteSoakCycle(0,TimeSpan.Zero);
        cycle.Advance(TimeSpan.FromSeconds(10),a);
        Assert.Throws<TimeoutException>(()=>cycle.Advance(TimeSpan.FromSeconds(50),a));
        Assert.Equal(CompleteSoakProbe.RecoveryDecision.Failed,
            CompleteSoakProbe.RecoveryCheckpoint(true,true,TimeSpan.FromSeconds(101),TimeSpan.FromSeconds(100)));
    }

    [Fact]
    public void Slow_server_stop_is_not_counted_as_ten_seconds_of_verified_outage()
    {
        var now=DateTimeOffset.UtcNow;
        var a=new CompleteSoakObservation(1,Guid.NewGuid(),Guid.NewGuid(),now,now,now,0,0,0,false,true);
        var cycle=new CompleteSoakCycle(0,TimeSpan.Zero);
        cycle.MarkViewSuspended(TimeSpan.FromSeconds(9));
        Assert.False(cycle.Advance(TimeSpan.FromSeconds(12),a));
        Assert.False(cycle.Advance(TimeSpan.FromSeconds(19),a with {ActualViewUnavailable=false}));
        Assert.True(cycle.Advance(TimeSpan.FromSeconds(19),a));
    }

    [Fact]
    public async Task Cancelled_arm_does_not_arm_FULL_and_does_not_leak_the_collection_gate()
    {
        var job=new IsolatedCollectorFaultJob(new Job(()=>false),()=>false);
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        var armed=false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>job.ArmBetweenCollectionsAsync(()=>armed=true,cancel.Token));
        Assert.False(armed);
        Assert.Equal(CollectorStatus.Success,await job.CollectAndPersistAsync(default));
        await job.ArmBetweenCollectionsAsync(()=>armed=true,default);
        Assert.True(armed);
    }

    [Fact]
    public async Task Repeated_failure_keeps_real_exponential_backoff_and_cannot_be_declared_recovered()
    {
        var start=DateTimeOffset.Parse("2026-10-18T00:00:00Z");var now=start.AddSeconds(215);var fail=true;
        var runner=new CollectorCycleRunner([new Job(()=>fail)],TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(60),TimeSpan.FromHours(1),()=>now);
        Assert.Equal(1,Assert.Single(await runner.RunOnceAsync(default)).ConsecutiveFailures);
        now=start.AddSeconds(275);
        var repeated=Assert.Single(await runner.RunOnceAsync(default));
        Assert.Equal(2,repeated.ConsecutiveFailures);Assert.Equal(start.AddSeconds(395),repeated.RetryAtUtc);
        fail=false;now=start.AddSeconds(280);
        Assert.Equal(CollectorCycleStatus.Skipped,Assert.Single(await runner.RunOnceAsync(default)).Status);
        Assert.Equal(CompleteSoakProbe.RecoveryDecision.Failed,
            CompleteSoakProbe.RecoveryCheckpoint(false,false,TimeSpan.FromSeconds(100),TimeSpan.FromSeconds(100)));
        now=start.AddSeconds(395);
        Assert.Equal(0,Assert.Single(await runner.RunOnceAsync(default)).ConsecutiveFailures);
    }

    [Fact]
    public void New_snapshot_ids_do_not_override_stale_or_future_timestamps()
    {
        var now=DateTimeOffset.UtcNow;
        var a=new CompleteSoakObservation(1,Guid.NewGuid(),Guid.NewGuid(),now,now,now,0,0,0,false,true);
        var cycle=new CompleteSoakCycle(0,TimeSpan.Zero);cycle.Advance(TimeSpan.FromSeconds(10),a);
        a=a with {Snapshots=2,CpuId=Guid.NewGuid(),SystemId=Guid.NewGuid()};
        Assert.False(cycle.Fresh(a with {Now=now.AddSeconds(41)}));
        Assert.False(cycle.Fresh(a with {CpuAt=now.AddSeconds(1)}));
        Assert.True(cycle.Fresh(a with {Now=now.AddSeconds(40)}));
    }

    private sealed class Job(Func<bool> fail) : ITelemetryCollectionJob
    {
        public string CollectorId => "cpu-memory";
        public IReadOnlyList<string> WarningCodes => [];
        public ValueTask<CollectorStatus> CollectAndPersistAsync(CancellationToken token)
            => fail() ? throw new InvalidOperationException("synthetic_failure") : ValueTask.FromResult(CollectorStatus.Success);
    }
    private sealed class AsyncJob(Func<Task<CollectorStatus>> collect) : ITelemetryCollectionJob
    {
        public string CollectorId => "cpu-memory";
        public IReadOnlyList<string> WarningCodes => [];
        public ValueTask<CollectorStatus> CollectAndPersistAsync(CancellationToken token)=>new(collect());
    }
}
