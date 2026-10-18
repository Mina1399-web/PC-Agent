using System.Security.Principal;
using System.Text.Json;
using HostAgent.Core.Contracts;
using HostAgent.Core.Metrics;
using HostAgent.Core.Models.Remediation;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Core.Orchestration;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Collectors;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Heartbeat;
using HostAgent.Infrastructure.Notifications;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Storage;
using HostAgent.Service;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Test host only. All collectors and runtime workers below are the production
// implementations; fault controls and the overview-only endpoint are private.
public sealed class PrivateRuntimeGraph : IAsyncDisposable
{
    private readonly IHost host;
    private readonly SecurityIdentifier sid;
    private readonly DashboardDispatcher dispatcher;
    private DashboardWorker? view;
    private int queuedCollectorFailures;
    private int viewSuspended = 1;
    private bool stopped;
    public string DirectoryPath { get; }
    public string DatabasePath => Path.Combine(DirectoryPath, "host-agent.db");
    public string NotificationPath => Path.Combine(DirectoryPath, "notifications.db");
    public string PipeName { get; }
    public SqliteHostAgentStore DurableStore { get; }
    public IHostAgentStore Store { get; }
    public CompleteStorageWriteFault WriteFault { get; }
    public IsolatedCollectorFaultJob CollectorFault { get; }
    public AgentRuntimeHealth Health { get; }
    public int HelperCalls => denyHelper.Calls;
    public bool ViewSuspended => Volatile.Read(ref viewSuspended) != 0;
    public Action? CollectorFailureConsumed { get; set; }
    private readonly DenyHelper denyHelper = new();
    private readonly List<PipeServerDiagnostic> pipeDiagnostics = [];
    public IReadOnlyList<PipeServerDiagnostic> Diagnostics { get { lock (pipeDiagnostics) return pipeDiagnostics.ToArray(); } }

    public PrivateRuntimeGraph(string directory, Guid id)
    {
        CompleteProbeGuard.AssertPlainPath(directory);
        DirectoryPath = directory;
        PipeName = "HostAgent.Dev.Complete.View." + id.ToString("N");
        using var identity = WindowsIdentity.GetCurrent();
        sid = identity.User ?? throw new InvalidOperationException("private_sid_missing");
        DurableStore = new(DatabasePath);
        Store = CompleteStorageWriteFault.Create(DurableStore, out var writeFault);
        WriteFault = writeFault;
        var configuration = new ServiceConfiguration { CollectionIntervalSeconds = 10, Mode = OperatingMode.Shadow, ReadOnly = true };
        CollectorFault = new(new TelemetryCollectionJob<CpuMemoryTelemetry>(new WindowsCpuMemoryCollector(), Store,
            new CpuMemoryMetricProjector()), ConsumeCollectorFailure);
        var builder = new HostBuilder().ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IHostAgentStore>(Store);
            services.AddSingleton<IHelperClient>(denyHelper);
            services.AddSingleton<INotificationStore>(new SqliteNotificationStore(NotificationPath));
            services.AddSingleton<INotificationSender>(new LocalNotificationSender());
            services.AddSingleton<IHeartbeatSender>(new DisabledHeartbeat());
            services.AddSingleton<DiagnosticCycleProcessor>();
            Add(services, new WindowsSystemCollector(), new SystemTelemetryMetricProjector());
            services.AddSingleton<ITelemetryCollectionJob>(CollectorFault);
            Add(services, new WindowsDiskCollector(), new DiskTelemetryMetricProjector());
            Add(services, new WindowsProcessServicesCollector(), new ProcessServiceMetricProjector());
            Add(services, new WindowsNetworkCollector(false), new NetworkTelemetryMetricProjector());
            Add(services, new WindowsEventLogCollector(Store), new EventLogMetricProjector());
            ServiceRuntime.AddWorkers(services, configuration);
        });
        host = builder.Build();
        Health = host.Services.GetRequiredService<AgentRuntimeHealth>();
        dispatcher = new(Store, configuration, Health, TimeProvider.System);
    }

    public async Task StartAsync(CancellationToken token)
    {
        await host.StartAsync(token).WaitAsync(TimeSpan.FromSeconds(25), token);
        await ResumeViewAsync(token);
    }
    public void RequestCollectorFailure() => Interlocked.Exchange(ref queuedCollectorFailures, 1);
    public Task ArmCombinedFaultAsync(CancellationToken token)
    {
        if (!ViewSuspended) throw new InvalidOperationException("combined_view_must_be_suspended");
        return CollectorFault.ArmBetweenCollectionsAsync(() =>
        {
            WriteFault.Enabled = true;
            RequestCollectorFailure();
        }, token);
    }
    public async Task ResumeViewAsync(CancellationToken token)
    {
        if (view is not null) throw new InvalidOperationException("private_view_already_running");
        var server = new AdminPipeServer(PipeName, sid, sid,
            (payload, cancellation) => IsolatedScmDashboardDispatch.DispatchAsync(payload, dispatcher.DispatchAsync, cancellation),
            TimeSpan.FromSeconds(6), reportDiagnostic: value => { lock (pipeDiagnostics) if (pipeDiagnostics.Count < 128) pipeDiagnostics.Add(value); });
        view = new DashboardWorker(server.ServeOnceAsync);
        await view.StartAsync(token);
        Volatile.Write(ref viewSuspended, 0);
    }
    public async Task SuspendViewAsync(CancellationToken token)
    {
        if (view is null) throw new InvalidOperationException("private_view_already_stopped");
        var current = view;
        await current.StopAsync(token).WaitAsync(TimeSpan.FromSeconds(10), token);
        current.Dispose(); view = null; Volatile.Write(ref viewSuspended, 1);
    }
    public async Task<bool> OverviewAsync(CancellationToken token)
    {
        using var response = JsonDocument.Parse(await new ViewPipeClient(PipeName, sid, TimeSpan.FromSeconds(3))
            .SendAsync(ViewProtocol.Request("overview", new { }), token));
        return response.RootElement.GetProperty("ok").GetBoolean();
    }
    public async Task StopAsync(CancellationToken token)
    {
        if (stopped) return;
        if (view is not null) await SuspendViewAsync(token);
        await host.StopAsync(token).WaitAsync(TimeSpan.FromSeconds(25), token);
        stopped = true;
    }
    public async ValueTask DisposeAsync()
    {
        try { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await StopAsync(deadline.Token); }
        finally
        {
            if (host is IAsyncDisposable asyncHost) await asyncHost.DisposeAsync(); else host.Dispose();
            await DurableStore.DisposeAsync();
        }
    }
    public async ValueTask DisposeAsync(CancellationToken cleanupToken)
    {
        try { await StopAsync(cleanupToken); }
        finally
        {
            try
            {
                if (host is IAsyncDisposable asyncHost) await asyncHost.DisposeAsync().AsTask().WaitAsync(cleanupToken);
                else host.Dispose();
            }
            finally { await DurableStore.DisposeAsync().AsTask().WaitAsync(cleanupToken); }
        }
    }
    public async Task<long> SnapshotCountAsync(CancellationToken token) => await CountAsync(DatabasePath, "telemetry_snapshots", token);
    public static async Task<long> CountAsync(string path, string table, CancellationToken token)
    {
        if (table is not ("telemetry_snapshots" or "incidents" or "baseline_samples")) throw new ArgumentException("private_table_not_allowed");
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 2 }.ToString());
        await connection.OpenAsync(token);
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA query_only=ON;";
        await command.ExecuteNonQueryAsync(token); command.CommandText = "SELECT COUNT(*) FROM " + table + ";";
        return Convert.ToInt64(await command.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture);
    }
    private void Add<T>(IServiceCollection services, ITelemetryCollector<T> collector, ITelemetryMetricProjector<T> projector) where T : class
        => services.AddSingleton<ITelemetryCollectionJob>(new TelemetryCollectionJob<T>(collector, Store, projector));
    private bool ConsumeCollectorFailure()
    {
        if (Interlocked.Exchange(ref queuedCollectorFailures, 0) == 0) return false;
        CollectorFailureConsumed?.Invoke();
        return true;
    }
    private sealed class DenyHelper : IHelperClient
    {
        public int Calls;
        public ValueTask<HelperOperationResponse> ExecuteAsync(HelperOperationRequest request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return ValueTask.FromResult(new HelperOperationResponse(1, request.RequestId, HelperResult.Denied, "private_helper_denied")); }
    }
}
