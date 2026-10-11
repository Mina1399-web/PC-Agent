using System.Text.Json;
using HostAgent.Core.Contracts;
using HostAgent.Core.Orchestration;
using HostAgent.Core.Models.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostAgent.Service;

internal sealed class CollectionCoordinator : BackgroundService
{
    private static readonly TimeSpan CollectionInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CollectionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromHours(1);
    private static readonly TimeSpan DetailedRetention = TimeSpan.FromDays(30);

    private readonly IHostAgentStore _store;
    private readonly CollectorCycleRunner _runner;
    private readonly ILogger<CollectionCoordinator> _logger;
    private DateTimeOffset _nextRetentionAtUtc = DateTimeOffset.MinValue;

    public CollectionCoordinator(
        IHostAgentStore store,
        IEnumerable<ITelemetryCollectionJob> jobs,
        ILogger<CollectionCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _logger = logger;
        _runner = new CollectorCycleRunner(
            jobs.ToArray(),
            CollectionTimeout,
            InitialBackoff,
            MaximumBackoff);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _store.InitializeAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            _logger.LogCritical(exception, "Host Agent storage initialization failed; collection will continue with degraded persistence.");
        }

        using var timer = new PeriodicTimer(CollectionInterval);
        do
        {
            try
            {
                await ApplyRetentionIfDueAsync(stoppingToken);
                var results = await _runner.RunOnceAsync(stoppingToken);
                foreach (var result in results)
                {
                    LogResult(result);
                    await RecordFailureHealthAsync(result, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "A collection cycle failed unexpectedly; the worker remains active.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async ValueTask ApplyRetentionIfDueAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < _nextRetentionAtUtc)
        {
            return;
        }

        try
        {
            var deletedRows = await _store.ApplyRetentionAsync(now, DetailedRetention, cancellationToken);
            _nextRetentionAtUtc = now.AddDays(1);
            _logger.LogInformation(
                "Daily SQLite retention completed; removed {DeletedRows} old telemetry, metric, and health rows.",
                deletedRows);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _nextRetentionAtUtc = now.AddHours(1);
            _logger.LogError(exception, "SQLite retention failed; it will be retried after one hour.");
        }
    }

    private void LogResult(CollectorCycleResult result)
    {
        if (result.Status is CollectorCycleStatus.Succeeded)
        {
            _logger.LogDebug("Collector {CollectorId} completed successfully.", result.CollectorId);
            return;
        }

        if (result.Status is CollectorCycleStatus.Partial or CollectorCycleStatus.Unavailable)
        {
            _logger.LogWarning(
                "Collector {CollectorId} completed with status {CollectorStatus}.",
                result.CollectorId,
                result.Status);
            return;
        }

        if (result.Status is CollectorCycleStatus.Skipped)
        {
            _logger.LogDebug(
                "Collector {CollectorId} was skipped with reason {FailureCode}; retry at {RetryAtUtc}.",
                result.CollectorId,
                result.FailureCode,
                result.RetryAtUtc);
            return;
        }

        _logger.LogError(
            "Collector {CollectorId} failed with {FailureCode}; consecutive failures {ConsecutiveFailures}; retry at {RetryAtUtc}.",
            result.CollectorId,
            result.FailureCode,
            result.ConsecutiveFailures,
            result.RetryAtUtc);
    }

    private async ValueTask RecordFailureHealthAsync(
        CollectorCycleResult result,
        CancellationToken cancellationToken)
    {
        if (result.Status != CollectorCycleStatus.Failed
            || result.FailureCode is not ("collector_timeout" or "collector_exception" or "collector_status_unknown"))
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                result.FailureCode,
                result.ConsecutiveFailures,
                result.RetryAtUtc
            });
            await _store.SaveServiceHealthAsync(
                new ServiceHealthSnapshot(
                    result.CollectorId,
                    DateTimeOffset.UtcNow,
                    result.FailureCode,
                    payload),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not persist failure health for collector {CollectorId}.",
                result.CollectorId);
        }
    }
}
