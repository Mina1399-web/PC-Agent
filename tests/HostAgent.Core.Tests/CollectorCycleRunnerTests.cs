using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Core.Orchestration;
using Xunit;

namespace HostAgent.Core.Tests;

public sealed class CollectorCycleRunnerTests
{
    [Fact]
    public async Task One_collector_failure_does_not_stop_its_peer()
    {
        var failing = new FakeJob(
            "failed",
            _ => throw new InvalidOperationException("simulated collector fault"));
        var healthy = new FakeJob(
            "healthy",
            _ => ValueTask.FromResult(CollectorStatus.Success));
        var runner = CreateRunner([failing, healthy]);

        var results = await runner.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(CollectorCycleStatus.Failed, results.Single(result => result.CollectorId == "failed").Status);
        Assert.Equal(CollectorCycleStatus.Succeeded, results.Single(result => result.CollectorId == "healthy").Status);
        Assert.Equal(1, failing.CallCount);
        Assert.Equal(1, healthy.CallCount);
    }

    [Fact]
    public async Task Collector_timeout_cancels_the_collector_and_prevents_a_tight_retry()
    {
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new FakeJob(
            "blocked",
            async cancellationToken =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return CollectorStatus.Success;
                }
                catch (OperationCanceledException)
                {
                    cancellationObserved.TrySetResult();
                    throw;
                }
            });
        var runner = new CollectorCycleRunner(
            [blocked],
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(4),
            () => new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

        var first = Assert.Single(await runner.RunOnceAsync(CancellationToken.None));
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = Assert.Single(await runner.RunOnceAsync(CancellationToken.None));

        Assert.Equal(CollectorCycleStatus.Failed, first.Status);
        Assert.Equal("collector_timeout", first.FailureCode);
        Assert.Equal(CollectorCycleStatus.Skipped, second.Status);
        Assert.Equal(1, blocked.CallCount);
    }

    [Fact]
    public async Task Collector_that_finishes_as_timeout_cancellation_completes_can_retry_after_backoff()
    {
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        var job = new FakeJob(
            "cancel-completes",
            cancellationToken =>
            {
                if (Interlocked.Increment(ref invocationCount) > 1)
                {
                    return ValueTask.FromResult(CollectorStatus.Success);
                }

                var completion = new TaskCompletionSource<CollectorStatus>();
                _ = cancellationToken.Register(() =>
                {
                    completion.TrySetResult(CollectorStatus.Success);
                    cancellationCompleted.TrySetResult();
                });
                started.TrySetResult();
                return new ValueTask<CollectorStatus>(completion.Task);
            });
        var runner = new CollectorCycleRunner(
            [job],
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(4),
            () => now);

        var firstCycle = Task.Run(async () => await runner.RunOnceAsync(CancellationToken.None));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var first = Assert.Single(await firstCycle);
        await cancellationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        now = first.RetryAtUtc!.Value;
        CollectorCycleResult? retry = null;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var candidate = Assert.Single(await runner.RunOnceAsync(CancellationToken.None));
            if (candidate.FailureCode != "collector_still_running")
            {
                retry = candidate;
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        Assert.Equal(CollectorCycleStatus.Failed, first.Status);
        Assert.Equal("collector_timeout", first.FailureCode);
        Assert.NotNull(retry);
        Assert.Equal(CollectorCycleStatus.Succeeded, retry.Status);
        Assert.Equal(2, job.CallCount);
    }

    [Fact]
    public async Task Collector_timeout_also_bounds_synchronous_collection_work()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new FakeJob(
            "synchronous",
            _ =>
            {
                started.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(5));
                finished.TrySetResult();
                return ValueTask.FromResult(CollectorStatus.Success);
            });
        var runner = new CollectorCycleRunner(
            [blocked],
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(4));
        var cycle = Task.Run(async () => await runner.RunOnceAsync(CancellationToken.None));

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            var completed = await Task.WhenAny(cycle, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(cycle, completed);
            var timedOut = Assert.Single(await cycle);
            Assert.Equal(CollectorCycleStatus.Failed, timedOut.Status);
            Assert.Equal("collector_timeout", timedOut.FailureCode);

            var whileBlocked = Assert.Single(await runner.RunOnceAsync(CancellationToken.None));
            Assert.Equal(CollectorCycleStatus.Skipped, whileBlocked.Status);
            Assert.Equal("collector_still_running", whileBlocked.FailureCode);
        }
        finally
        {
            release.Set();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task Consecutive_failures_use_exponential_backoff_without_waiting_in_the_runner()
    {
        var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var failing = new FakeJob("retrying", _ => throw new InvalidOperationException("simulated"));
        var runner = new CollectorCycleRunner(
            [failing],
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(4),
            () => now);

        var first = Assert.Single(await runner.RunOnceAsync(CancellationToken.None));
        var skipped = Assert.Single(await runner.RunOnceAsync(CancellationToken.None));
        now = now.AddMinutes(1);
        var second = Assert.Single(await runner.RunOnceAsync(CancellationToken.None));

        Assert.Equal(1, first.ConsecutiveFailures);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 12, 1, 0, TimeSpan.Zero), first.RetryAtUtc);
        Assert.Equal(CollectorCycleStatus.Skipped, skipped.Status);
        Assert.Equal(2, second.ConsecutiveFailures);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 12, 3, 0, TimeSpan.Zero), second.RetryAtUtc);
        Assert.Equal(2, failing.CallCount);
    }

    [Fact]
    public async Task Unavailable_collector_is_reported_distinctly_and_uses_backoff()
    {
        var unavailable = new FakeJob(
            "offline",
            _ => ValueTask.FromResult(CollectorStatus.Unavailable));
        var runner = CreateRunner([unavailable]);

        var result = Assert.Single(await runner.RunOnceAsync(CancellationToken.None));

        Assert.Equal(CollectorCycleStatus.Unavailable, result.Status);
        Assert.Equal("collector_unavailable", result.FailureCode);
        Assert.Equal(1, result.ConsecutiveFailures);
    }

    private static CollectorCycleRunner CreateRunner(IReadOnlyList<FakeJob> jobs) =>
        new(
            jobs,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(4),
            () => new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

    private sealed class FakeJob(
        string collectorId,
        Func<CancellationToken, ValueTask<CollectorStatus>> execute) : ITelemetryCollectionJob
    {
        public string CollectorId { get; } = collectorId;

        public int CallCount { get; private set; }

        public ValueTask<CollectorStatus> CollectAndPersistAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return execute(cancellationToken);
        }
    }
}
