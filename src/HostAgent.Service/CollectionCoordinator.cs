using System.Text.Json;
using HostAgent.Core.Contracts;
using HostAgent.Core.Orchestration;
using HostAgent.Core.Models.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HostAgent.Infrastructure.Security;

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
    private readonly DiagnosticCycleProcessor _diagnostics;
    private readonly ILogger<CollectionCoordinator> _logger;
    private readonly IncidentPublisher? _publisher;
    private readonly AgentRuntimeHealth _health;
    private readonly ServiceConfiguration _configuration;
    private DateTimeOffset _nextRetentionAtUtc = DateTimeOffset.MinValue;

    public CollectionCoordinator(
        IHostAgentStore store,
        IEnumerable<ITelemetryCollectionJob> jobs,
        DiagnosticCycleProcessor diagnostics,
        ILogger<CollectionCoordinator> logger) : this(store,jobs,diagnostics,logger,null,new(),new()) { }

    public CollectionCoordinator(IHostAgentStore store,IEnumerable<ITelemetryCollectionJob> jobs,
        DiagnosticCycleProcessor diagnostics,ILogger<CollectionCoordinator> logger,IncidentPublisher? publisher,
        AgentRuntimeHealth health,ServiceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(diagnostics);

        _store = store;
        _logger = logger;
        _diagnostics = diagnostics;
        _publisher=publisher;
        _health=health;
        _configuration=configuration;
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

        using var worker=new ResilientWorker(token=>RunCycleAsync(null,token).AsTask(),
            TimeSpan.FromSeconds(_configuration.CollectionIntervalSeconds),TimeSpan.FromMinutes(2),TimeProvider.System,
            code=>{_health.Set("collection_worker",code is "cycle_failed" or "cycle_timeout");
                _logger.LogWarning("Collection worker {Code}.",code);});
        await worker.StartAsync(stoppingToken);
        await worker.ExecuteTask!;
    }

    public async ValueTask RunCycleAsync(DateTimeOffset? nowUtc, CancellationToken cancellationToken)
    {
        await ApplyRetentionIfDueAsync(nowUtc ?? DateTimeOffset.UtcNow, cancellationToken);
        var results = await _runner.RunOnceAsync(cancellationToken);
        _health.Set("collectors",results.Any(result=>result.Status!=CollectorCycleStatus.Succeeded));
        foreach (var result in results)
        {
            LogResult(result);
            await RecordFailureHealthAsync(result, cancellationToken);
        }
        try
        {
            var incidents=await _diagnostics.ProcessAsync(nowUtc ?? DateTimeOffset.UtcNow, cancellationToken);
            _health.Set("diagnostics",_diagnostics.BaselineFailureCode is not null);
            if(_publisher is not null) await _publisher.PublishAsync(incidents,nowUtc??DateTimeOffset.UtcNow,cancellationToken);
            if (_diagnostics.BaselineFailureCode is { } failureCode)
                _logger.LogWarning("Adaptive baseline is degraded with {FailureCode}; fixed diagnosis remains active.", failureCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _health.Set("diagnostics",true);
            _logger.LogError(exception, "Diagnosis persistence failed; read-only collection continues and diagnosis will retry next cycle.");
        }
    }

    private async ValueTask ApplyRetentionIfDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
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
