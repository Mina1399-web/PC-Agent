using System.Diagnostics;
using System.Runtime.InteropServices;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.ServiceProcess;

// Test-only bootstrap of the current runtime graph. No production Program,
// admin endpoint, helper pipe, protected data or outbound HTTP registration.
internal static class IsolatedRuntimeProbe
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 3 || !Guid.TryParseExact(args[1], "N", out var id) || args[2] is not ("observe" or "cpu" or "cpu-short" or "resilience")) return 2;
        using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS", false);
        using var additions = new ServiceController("VBoxService");
        if (Environment.MachineName != "AGENTPC" || bios?.GetValue("SystemProductName") as string != "VirtualBox"
            || additions.Status != ServiceControllerStatus.Running || FreeMiB() < 1024) return 3;
        var evidence = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HostAgent-DEV-evidence");
        // Reject links in every existing parent before any write or fault.
        for (DirectoryInfo? parent = new(evidence); parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) return 3;
        var directory = Path.Combine(evidence, "runtime-candidate-" + id.ToString("N"));
        if (Directory.Exists(directory)) return 3; // New fixture only.
        Directory.CreateDirectory(directory);
        // Recheck the newly created fixture and its ancestors immediately
        // before storage construction; no existing fixture can be reused.
        for (DirectoryInfo? parent = new(directory); parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) return 3;
        var database = Path.Combine(directory, "fixture.db");
        var store = new SqliteHostAgentStore(database);
        var endurance = args[2] == "resilience";
        var config = new ServiceConfiguration { CollectionIntervalSeconds = endurance ? 10 : 60 }; // Shadow/read-only; probes and outbound disabled.
        var faultClock = Stopwatch.StartNew();
        IsolatedCollectorFaultJob? faultJob = null;
        var builder = new HostBuilder().ConfigureLogging(logging => logging.ClearProviders());
        var diagnostics = new List<PipeServerDiagnostic>();
        var dispatchDiagnostics = new List<DashboardDispatchDiagnostic>();
        using var identity = WindowsIdentity.GetCurrent(); var sid = identity.User!;
        var name = "HostAgent-isolated-runtime-" + id.ToString("N");
        var helper = new DenyHelper();
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IHostAgentStore>(store);
            services.AddSingleton<IHelperClient>(helper);
            services.AddSingleton<INotificationStore>(new SqliteNotificationStore(Path.Combine(directory, "notices.db")));
            services.AddSingleton<INotificationSender>(new LocalNotificationSender());
            services.AddSingleton<IHeartbeatSender>(new DisabledHeartbeat());
            services.AddSingleton<DiagnosticCycleProcessor>();
            Add(services, new WindowsSystemCollector(), new SystemTelemetryMetricProjector());
            if (endurance)
            {
                faultJob = new(new TelemetryCollectionJob<CpuMemoryTelemetry>(new WindowsCpuMemoryCollector(), store, new CpuMemoryMetricProjector()),
                    () => (faultClock.Elapsed.TotalSeconds >= 10 && faultJob!.InjectedFailures == 0)
                        || (faultClock.Elapsed.TotalSeconds >= 85 && faultJob!.InjectedFailures == 1 && faultJob.Recoveries >= 1));
                services.AddSingleton<ITelemetryCollectionJob>(faultJob);
            }
            else Add(services, new WindowsCpuMemoryCollector(), new CpuMemoryMetricProjector());
            Add(services, new WindowsDiskCollector(), new DiskTelemetryMetricProjector());
            Add(services, new WindowsProcessServicesCollector(), new ProcessServiceMetricProjector());
            Add(services, new WindowsNetworkCollector(false), new NetworkTelemetryMetricProjector());
            Add(services, new WindowsEventLogCollector(store), new EventLogMetricProjector());
            ServiceRuntime.AddWorkers(services, config);
            services.AddHostedService(sp =>
            {
                var dispatcher = new DashboardDispatcher(store, config, sp.GetRequiredService<AgentRuntimeHealth>(), TimeProvider.System,
                    d => { if (d.Kind != "completed" || d.ElapsedMilliseconds >= 500) lock (dispatchDiagnostics) { if (dispatchDiagnostics.Count < 64) dispatchDiagnostics.Add(d); } });
                var server = new AdminPipeServer(name, sid, sid, dispatcher.DispatchAsync, TimeSpan.FromSeconds(10),
                    reportDiagnostic: d => { lock (diagnostics) diagnostics.Add(d); });
                return new DashboardWorker(server.ServeOnceAsync);
            });
        });
        using var host = builder.Build();
        using var stress = new CancellationTokenSource();
        using var guardStop = new CancellationTokenSource();
        using var guardAbort = new CancellationTokenSource();
        var guard = new Thread(() =>
        {
            while (!guardStop.IsCancellationRequested)
            {
                if (FreeMiB() < 768 || new DriveInfo(Path.GetPathRoot(directory)!).AvailableFreeSpace < 20L * 1024 * 1024 * 1024)
                { stress.Cancel(); guardAbort.Cancel(); }
                guardStop.Token.WaitHandle.WaitOne(250);
            }
        }) { IsBackground = true };
        guard.Start();
        var threads = new List<Thread>();
        var started = DateTimeOffset.UtcNow; var timer = Stopwatch.StartNew();
        var minimumFree = FreeMiB(); var aborted = false; var views = 0; var failures = 0;
        try
        {
            await host.StartAsync().WaitAsync(TimeSpan.FromSeconds(20));
            if (args[2].StartsWith("cpu", StringComparison.Ordinal) && !guardAbort.IsCancellationRequested && FreeMiB() >= 1024)
            {
                for (var i = 0; i < Math.Clamp(Environment.ProcessorCount, 1, 4); i++)
                {
                    var thread = new Thread(() => { while (!stress.IsCancellationRequested) Thread.SpinWait(10000); })
                        { IsBackground = true, Priority = ThreadPriority.Lowest };
                    threads.Add(thread); thread.Start();
                }
            }
            else if (args[2].StartsWith("cpu", StringComparison.Ordinal)) { aborted = true; }
            Console.WriteLine(JsonSerializer.Serialize(new { ready = true, mode = args[2], cpuThreads = threads.Count }));
            var duration = endurance ? 190 : args[2] == "cpu" ? 450 : args[2] == "cpu-short" ? 80 : 20;
            var nextView = TimeSpan.Zero;
            while (!aborted && !guardAbort.IsCancellationRequested && timer.Elapsed < TimeSpan.FromSeconds(duration))
            {
                var free = FreeMiB(); minimumFree = Math.Min(minimumFree, free);
                if (free < 768 || new DriveInfo(Path.GetPathRoot(directory)!).AvailableFreeSpace < 20L * 1024 * 1024 * 1024)
                { aborted = true; break; } // Soft abort above the 512 MiB hard floor.
                if (timer.Elapsed >= nextView)
                {
                    using var bound = CancellationTokenSource.CreateLinkedTokenSource(guardAbort.Token);
                    bound.CancelAfter(TimeSpan.FromSeconds(12));
                    PipeClientDiagnostic? clientDiagnostic = null;
                    var viewStarted = Stopwatch.GetTimestamp();
                    using var response = JsonDocument.Parse(await new ViewPipeClient(name, sid, TimeSpan.FromSeconds(8), d => clientDiagnostic = d)
                        .SendAsync(ViewProtocol.Request("overview", new { }), bound.Token));
                    var ok = response.RootElement.GetProperty("ok").GetBoolean();
                    views++; if (!ok) failures++;
                    Console.WriteLine(JsonSerializer.Serialize(new { elapsedSeconds = (int)timer.Elapsed.TotalSeconds, overview = ok,
                        code = response.RootElement.GetProperty("code").GetString(),
                        viewMilliseconds = (long)Stopwatch.GetElapsedTime(viewStarted).TotalMilliseconds,
                        clientDiagnostic, freeMiB = Math.Round(free, 1) }));
                    nextView = timer.Elapsed + TimeSpan.FromSeconds(10);
                }
                await Task.Delay(250);
            }
        }
        catch (OperationCanceledException) when (guardAbort.IsCancellationRequested) { aborted = true; }
        finally
        {
            stress.Cancel();
            foreach (var thread in threads) if (!thread.Join(TimeSpan.FromSeconds(3))) aborted = true;
            try { await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
            finally
            {
                guardStop.Cancel();
                if (!guard.Join(TimeSpan.FromSeconds(3))) aborted = true;
            }
        }
        aborted |= guardAbort.IsCancellationRequested;
        var samples = (await store.ReadMetricSeriesAsync("cpu.usage.percent", "host", started.AddSeconds(-1), DateTimeOffset.UtcNow, 32, default))
            .OrderBy(x => x.SampledAtUtc).ToArray();
        var incidents = await store.ReadIncidentHeadsAsync(default);
        var cpu = incidents.Where(x => x.CorrelationKey.StartsWith("cpu_sustained:", StringComparison.Ordinal)).ToArray();
        var gaps = samples.Zip(samples.Skip(1), (a, b) => (b.SampledAtUtc - a.SampledAtUtc).TotalSeconds).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            finished = true, mode = args[2], aborted, elapsedSeconds = Math.Round(timer.Elapsed.TotalSeconds, 1),
            minimumFreeMiB = Math.Round(minimumFree, 1), views, failures, helperCalls = helper.Calls,
            cpuSamples = samples.Length, highCpuSamples = samples.Count(x => x.Value >= 85),
            injectedCollectorFailures = faultJob?.InjectedFailures ?? 0, collectorRecoveries = faultJob?.Recoveries ?? 0,
            maximumSampleGapSeconds = gaps.Length > 0 ? Math.Round(gaps.Max(), 1) : (double?)null,
            cpuDiagnoses = cpu.Select(x => new { Code = x.LatestDiagnosis.ProbableRootCause.Code, x.LatestDiagnosis.Confidence }).ToArray(),
            transportFailures = diagnostics.ToArray(), dispatchFailures = dispatchDiagnostics.ToArray(), fixtureId = id.ToString("N")
        }));
        return !aborted && failures == 0 && helper.Calls == 0 && (args[2] != "cpu" || cpu.Length > 0)
            && (!endurance || RecoveryProofIsComplete(faultJob!.InjectedFailures, faultJob.Recoveries, views, samples.Length)) ? 0 : 1;

        void Add<T>(IServiceCollection services, ITelemetryCollector<T> collector, ITelemetryMetricProjector<T> projector) where T : class
            => services.AddSingleton<ITelemetryCollectionJob>(new TelemetryCollectionJob<T>(collector, store, projector));
    }

    internal static bool RecoveryProofIsComplete(int injectedFailures, int recoveries, int views, int persistedSamples)
        => injectedFailures == 2 && recoveries == injectedFailures && views >= 10 && persistedSamples >= recoveries;

    private sealed class DenyHelper : IHelperClient
    {
        public int Calls;
        public ValueTask<HelperOperationResponse> ExecuteAsync(HelperOperationRequest request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return ValueTask.FromResult(new HelperOperationResponse(1, request.RequestId, HelperResult.Denied, "fixture_denied")); }
    }
    private static double FreeMiB()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref memory) ? memory.AvailablePhysical / 1048576d : 0;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
}
