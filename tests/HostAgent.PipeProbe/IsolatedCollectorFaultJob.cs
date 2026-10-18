using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Service;

// Test-only private substitution; it never disables an OS collector or Windows service.
public sealed class IsolatedCollectorFaultJob(ITelemetryCollectionJob inner, Func<bool> fail) : ITelemetryCollectionJob
{
    public string CollectorId => inner.CollectorId;
    public IReadOnlyList<string> WarningCodes => inner.WarningCodes;
    public int InjectedFailures { get; private set; }
    public int Recoveries { get; private set; }
    private bool awaitingRecovery;
    private readonly SemaphoreSlim collectionGate = new(1, 1);
    public async Task ArmBetweenCollectionsAsync(Action arm, CancellationToken token)
    {
        await collectionGate.WaitAsync(token);
        try { token.ThrowIfCancellationRequested(); arm(); }
        finally { collectionGate.Release(); }
    }
    public async ValueTask<CollectorStatus> CollectAndPersistAsync(CancellationToken token)
    {
        await collectionGate.WaitAsync(token);
        try
        {
        token.ThrowIfCancellationRequested();
        if (fail())
        {
            InjectedFailures++; awaitingRecovery = true;
            throw new InvalidOperationException("isolated_collector_injected_failure");
        }
        var status = await inner.CollectAndPersistAsync(token);
        if (awaitingRecovery && status is CollectorStatus.Success or CollectorStatus.Partial)
        { Recoveries++; awaitingRecovery = false; }
        return status;
        }
        finally { collectionGate.Release(); }
    }
}
