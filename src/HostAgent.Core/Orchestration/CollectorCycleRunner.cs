using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Core.Orchestration;

public enum CollectorCycleStatus
{
    Succeeded,
    Partial,
    Unavailable,
    Failed,
    Skipped
}

public sealed record CollectorCycleResult(
    string CollectorId,
    CollectorCycleStatus Status,
    string? FailureCode,
    int ConsecutiveFailures,
    DateTimeOffset? RetryAtUtc);

public sealed class CollectorCycleRunner
{
    private readonly IReadOnlyList<ITelemetryCollectionJob> _jobs;
    private readonly TimeSpan _collectionTimeout;
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maximumBackoff;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Dictionary<string, ExecutionState> _states;
    private readonly object _statesLock = new();

    public CollectorCycleRunner(
        IReadOnlyList<ITelemetryCollectionJob> jobs,
        TimeSpan collectionTimeout,
        TimeSpan initialBackoff,
        TimeSpan maximumBackoff,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        if (jobs.Any(static job => string.IsNullOrWhiteSpace(job.CollectorId)))
        {
            throw new ArgumentException("Every collection job must have a non-empty ID.", nameof(jobs));
        }

        var duplicateId = jobs.GroupBy(static job => job.CollectorId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new ArgumentException($"Duplicate collector job ID: {duplicateId.Key}.", nameof(jobs));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(collectionTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initialBackoff, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBackoff, initialBackoff);

        _jobs = jobs.ToArray();
        _collectionTimeout = collectionTimeout;
        _initialBackoff = initialBackoff;
        _maximumBackoff = maximumBackoff;
        _utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
        _states = _jobs.ToDictionary(
            static job => job.CollectorId,
            static _ => new ExecutionState(),
            StringComparer.OrdinalIgnoreCase);
    }

    public async ValueTask<IReadOnlyList<CollectorCycleResult>> RunOnceAsync(
        CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(_jobs.Select(job => RunJobAsync(job, cancellationToken)));
        return results.OrderBy(static result => result.CollectorId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<CollectorCycleResult> RunJobAsync(
        ITelemetryCollectionJob job,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _utcNow().ToUniversalTime();
        var state = _states[job.CollectorId];
        lock (_statesLock)
        {
            if (state.IsRunning)
            {
                return Skipped(job.CollectorId, state, "collector_still_running");
            }

            if (state.RetryAtUtc is { } retryAt && retryAt > now)
            {
                return Skipped(job.CollectorId, state, "backoff_active");
            }

            state.IsRunning = true;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operation = Task.Run(
            () => InvokeJobAsync(job, timeoutSource.Token),
            CancellationToken.None);
        var timeout = Task.Delay(_collectionTimeout, cancellationToken);
        var completed = await Task.WhenAny(operation, timeout);

        if (completed != operation)
        {
            timeoutSource.Cancel();
            if (cancellationToken.IsCancellationRequested)
            {
                _ = ObserveLateCompletionAsync(operation, state);
                throw new OperationCanceledException(cancellationToken);
            }

            var timeoutResult = RecordFailure(
                job.CollectorId,
                state,
                "collector_timeout",
                keepRunning: true);
            _ = ObserveLateCompletionAsync(operation, state);
            return timeoutResult;
        }

        try
        {
            var status = await operation;
            return status switch
            {
                CollectorStatus.Success => RecordAvailable(job.CollectorId, state, CollectorCycleStatus.Succeeded),
                CollectorStatus.Partial => RecordAvailable(job.CollectorId, state, CollectorCycleStatus.Partial),
                CollectorStatus.Unavailable => RecordFailure(
                    job.CollectorId,
                    state,
                    "collector_unavailable",
                    CollectorCycleStatus.Unavailable),
                CollectorStatus.Failed => RecordFailure(job.CollectorId, state, "collector_failed"),
                _ => RecordFailure(job.CollectorId, state, "collector_status_unknown")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ResetRunning(state);
            throw;
        }
        catch (Exception)
        {
            return RecordFailure(job.CollectorId, state, "collector_exception");
        }
    }

    private CollectorCycleResult RecordAvailable(
        string collectorId,
        ExecutionState state,
        CollectorCycleStatus status)
    {
        lock (_statesLock)
        {
            state.IsRunning = false;
            state.ConsecutiveFailures = 0;
            state.RetryAtUtc = null;
            return new CollectorCycleResult(collectorId, status, null, 0, null);
        }
    }

    private CollectorCycleResult RecordFailure(
        string collectorId,
        ExecutionState state,
        string failureCode,
        CollectorCycleStatus status = CollectorCycleStatus.Failed,
        bool keepRunning = false)
    {
        lock (_statesLock)
        {
            state.IsRunning = keepRunning;
            state.ConsecutiveFailures = Math.Min(state.ConsecutiveFailures + 1, 31);
            var retryAt = _utcNow().ToUniversalTime() + GetBackoff(state.ConsecutiveFailures);
            state.RetryAtUtc = retryAt;
            return new CollectorCycleResult(
                collectorId,
                status,
                failureCode,
                state.ConsecutiveFailures,
                retryAt);
        }
    }

    private void ResetRunning(ExecutionState state)
    {
        lock (_statesLock)
        {
            state.IsRunning = false;
        }
    }

    private TimeSpan GetBackoff(int failureCount)
    {
        var factor = Math.Pow(2, Math.Min(failureCount - 1, 30));
        var ticks = Math.Min(_maximumBackoff.Ticks, _initialBackoff.Ticks * factor);
        return TimeSpan.FromTicks((long)ticks);
    }

    private async Task ObserveLateCompletionAsync(Task<CollectorStatus> operation, ExecutionState state)
    {
        try
        {
            await operation;
        }
        catch (Exception exception)
        {
            lock (_statesLock)
            {
                state.LateFailureType = exception.GetType().Name;
            }
        }
        finally
        {
            ResetRunning(state);
        }
    }

    private static async Task<CollectorStatus> InvokeJobAsync(
        ITelemetryCollectionJob job,
        CancellationToken cancellationToken) =>
        await job.CollectAndPersistAsync(cancellationToken);

    private CollectorCycleResult Skipped(
        string collectorId,
        ExecutionState state,
        string failureCode) =>
        new(
            collectorId,
            CollectorCycleStatus.Skipped,
            failureCode,
            state.ConsecutiveFailures,
            state.RetryAtUtc);

    private sealed class ExecutionState
    {
        public bool IsRunning { get; set; }

        public int ConsecutiveFailures { get; set; }

        public DateTimeOffset? RetryAtUtc { get; set; }

        public string? LateFailureType { get; set; }
    }
}
