using System.Diagnostics;
using System.Text.Json;
using HostAgent.Core.Models.Incidents;
using HostAgent.Core.Models.Telemetry;

public static class CompleteSoakProbe
{
    public enum RecoveryDecision { Pending, Recovered, Failed }
    public static RecoveryDecision RecoveryCheckpoint(bool fresh, bool recovered, TimeSpan elapsed, TimeSpan budget)
        => elapsed >= budget ? RecoveryDecision.Failed
            : fresh && recovered ? RecoveryDecision.Recovered : RecoveryDecision.Pending;
    public static bool ConcurrentFaultsVerified(int requested, int consumedUnderBothFaults, int writesUnderBothFaults)
        => requested == 6 && consumedUnderBothFaults == 6 && writesUnderBothFaults == 6;
    public static bool ProgressVerified(DateTimeOffset sampledAt, DateTimeOffset now, long snapshots, long before)
        => sampledAt <= now && now - sampledAt <= TimeSpan.FromSeconds(40) && snapshots > before;
    private const int DurationSeconds = 1800;
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 2 || args[0] != "isolated-complete-soak") return 2;
        var id = CompleteProbeGuard.Parse(args[1]); CompleteProbeGuard.ValidateVm(true); CompleteProbeGuard.CheckLimits();
        var directory = CompleteProbeGuard.NewDirectory(id, "complete-soak");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1900));
        var token = deadline.Token;
        var samples = new List<SoakSample>(); var expectedUnavailable = 0; var unexpectedUnavailable = 0; var viewSuccess = 0;
        var requestedCollectorFailures = 0; var outageCycles = 0; var concurrentCycles = 0; var ok = false; string? errorCode = null;
        long snapshots = 0; var incidents = 0; var recurrence = 0; var duplicateActiveCorrelation = false;
        var hostQuickCheck = false; var notificationsQuickCheck = false; var minimumFreeMiB = CompleteProbeGuard.FreeMiB();
        var overlapLock = new object(); var collectorOverlapCycles = new HashSet<int>(); var writeOverlapCycles = new HashSet<int>();
        var progressChecks = 0; long lastFaultSnapshotCount = 0;
        var phaseProofs = new List<object>(); var activeCycle = 0; string? activePhase = null;
        var activePhaseSeconds = 0d;
        IReadOnlyList<HostAgent.Service.ComponentHealth> lastHealth = [];
        var observedCollectorOverlap = 0; var observedWriteOverlap = 0; var observedPairedOverlap = 0;
        double? finalSystemAgeSeconds = null; double? finalCpuAgeSeconds = null;
        var graph = new PrivateRuntimeGraph(directory, id);
        var timer = Stopwatch.StartNew();
        try
        {
            await graph.StartAsync(token);
            await CompleteStorageProbe.WaitAsync(async () => await graph.SnapshotCountAsync(token) > 0 && await graph.OverviewAsync(token), TimeSpan.FromSeconds(40), token);
            timer.Restart(); var nextView = TimeSpan.Zero; var nextSample = TimeSpan.Zero;
            var cycleState = new CompleteSoakCycle(0, timer.Elapsed);
            CompleteSoakCycle.Phase? entered = null;
            var outageObserved = false;
            graph.CollectorFailureConsumed = () =>
            {
                if (graph.ViewSuspended && graph.WriteFault.Enabled)
                    lock (overlapLock) collectorOverlapCycles.Add(Volatile.Read(ref activeCycle));
            };
            graph.WriteFault.FailureInjected = () =>
            {
                if (graph.ViewSuspended && graph.WriteFault.Enabled)
                    lock (overlapLock) writeOverlapCycles.Add(Volatile.Read(ref activeCycle));
            };
            var viewStopped = false;
            Console.WriteLine(JsonSerializer.Serialize(new { ready = true, mode = "complete_soak", fixtureId = id.ToString("N"), durationSeconds = DurationSeconds,
                collectionSeconds = 10, cycles = 6, softFloorMiB = 768, hardFloorMiB = 512, maximumFixtureMiB = 128, maximumEntries = 256,
                schedule = "observed_recovery_between_faults", observationBudgetSeconds = 40, recoveryBudgetSeconds = 100 }));
            while (timer.Elapsed.TotalSeconds < DurationSeconds || activeCycle < 6)
            {
                CompleteProbeGuard.CheckLimits(); token.ThrowIfCancellationRequested();
                minimumFreeMiB = Math.Min(minimumFreeMiB, CompleteProbeGuard.FreeMiB());
                if (activeCycle < 6)
                {
                    activePhase = cycleState.Stage.ToString();
                    if (entered != cycleState.Stage)
                    {
                        if (cycleState.SuspendView && !viewStopped)
                        {
                            await graph.SuspendViewAsync(token); viewStopped = true; outageCycles++;
                            outageObserved = false;
                            cycleState.MarkViewSuspended(timer.Elapsed);
                        }
                        else if (!cycleState.SuspendView && viewStopped)
                        { await graph.ResumeViewAsync(token); viewStopped = false; }
                        if (cycleState.Stage == CompleteSoakCycle.Phase.CombinedFault)
                        {
                            lastFaultSnapshotCount = await graph.SnapshotCountAsync(token);
                            using var armDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                            armDeadline.CancelAfter(TimeSpan.FromSeconds(40));
                            await graph.ArmCombinedFaultAsync(armDeadline.Token);
                            cycleState.MarkArmed(timer.Elapsed);
                            requestedCollectorFailures++; concurrentCycles++;
                        }
                        else
                        {
                            graph.WriteFault.Enabled = cycleState.EnableWriteFault;
                            if (cycleState.NeedsCollectorRequest)
                            { graph.RequestCollectorFailure(); requestedCollectorFailures++; }
                        }
                        entered = cycleState.Stage;
                    }
                    var cpu = await LatestSnapshotAsync(graph, "cpu-memory", token);
                    var system = await LatestSnapshotAsync(graph, "system", token);
                    snapshots = await graph.SnapshotCountAsync(token);
                    var now = DateTimeOffset.UtcNow;
                    finalCpuAgeSeconds = cpu is null ? null : (now-cpu.SampledAtUtc).TotalSeconds;
                    finalSystemAgeSeconds = system is null ? null : (now-system.SampledAtUtc).TotalSeconds;
                    bool overlap;
                    lock (overlapLock) overlap = collectorOverlapCycles.Contains(activeCycle) && writeOverlapCycles.Contains(activeCycle);
                    var observation = new CompleteSoakObservation(snapshots, cpu?.SnapshotId ?? Guid.Empty, system?.SnapshotId ?? Guid.Empty,
                        cpu?.SampledAtUtc ?? DateTimeOffset.MinValue, system?.SampledAtUtc ?? DateTimeOffset.MinValue, now,
                        graph.CollectorFault.InjectedFailures, graph.CollectorFault.Recoveries, graph.WriteFault.InjectedFailures, overlap, outageObserved);
                    activePhaseSeconds = (timer.Elapsed-cycleState.PhaseStarted).TotalSeconds;
                    if (cycleState.Stage == CompleteSoakCycle.Phase.Completed)
                    {
                        if (timer.Elapsed.TotalSeconds >= (activeCycle + 1) * 300)
                        {
                            activeCycle++;
                            if (activeCycle < 6) { cycleState = new(activeCycle, timer.Elapsed); entered = null; }
                        }
                    }
                    else if (cycleState.Advance(timer.Elapsed, observation))
                    {
                        phaseProofs.Add(new { cycle = activeCycle + 1, phase = activePhase, elapsedSeconds = Math.Round(timer.Elapsed.TotalSeconds,1), observedAtUtc=now,
                            snapshots, cpuAgeSeconds = finalCpuAgeSeconds, systemAgeSeconds = finalSystemAgeSeconds,
                            failures = graph.CollectorFault.InjectedFailures, recoveries = graph.CollectorFault.Recoveries });
                        if (cycleState.Stage == CompleteSoakCycle.Phase.Completed) progressChecks++;
                    }
                }
                if (timer.Elapsed >= nextView)
                {
                    var available = await graph.OverviewAsync(token);
                    if (viewStopped) { if (available) throw new InvalidOperationException("suspended_view_answered"); expectedUnavailable++; outageObserved=true; }
                    else if (available) viewSuccess++; else unexpectedUnavailable++;
                    nextView = timer.Elapsed + TimeSpan.FromSeconds(5);
                }
                if (timer.Elapsed >= nextSample)
                {
                    CompleteStorageProbe.CheckFixture(directory, 128L * 1024 * 1024, 256);
                    using var process = Process.GetCurrentProcess(); process.Refresh();
                    var bytes = new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.TopDirectoryOnly).Sum(file => file.Length);
                    var heads = await graph.DurableStore.ReadIncidentHeadsAsync(token);
                    if (heads.Count > 256) throw new InvalidOperationException("soak_incident_capacity_exceeded");
                    duplicateActiveCorrelation |= heads.Where(head => head.State != IncidentLifecycleState.Resolved)
                        .GroupBy(head => head.CorrelationKey, StringComparer.Ordinal).Any(group => group.Count() > 1);
                    var sample = new SoakSample((int)timer.Elapsed.TotalSeconds, process.WorkingSet64, process.PrivateMemorySize64,
                        process.HandleCount, process.Threads.Count, bytes, heads.Count);
                    samples.Add(sample);
                    lastHealth = graph.Health.Snapshot();
                    Console.WriteLine(JsonSerializer.Serialize(new { sample.Seconds, sample.WorkingSetBytes, sample.PrivateBytes, sample.Handles, sample.Threads,
                        sample.StorageBytes, sample.Incidents, freeMiB = Math.Round(minimumFreeMiB, 1), collectorFailures = graph.CollectorFault.InjectedFailures,
                        collectorRecoveries = graph.CollectorFault.Recoveries, writeFailures = graph.WriteFault.InjectedFailures,
                        activeCycle, activePhase, cpuAgeSeconds=finalCpuAgeSeconds, systemAgeSeconds=finalSystemAgeSeconds,
                        viewSuccess, expectedUnavailable, unexpectedUnavailable }));
                    if (process.WorkingSet64 > 512L * 1024 * 1024 || process.PrivateMemorySize64 > 768L * 1024 * 1024)
                        throw new InvalidOperationException("soak_process_memory_bound");
                    nextSample = timer.Elapsed + TimeSpan.FromSeconds(10);
                }
                await Task.Delay(250, token);
            }
            graph.WriteFault.Enabled = false;
            if (viewStopped) await graph.ResumeViewAsync(token);
            if (!await graph.OverviewAsync(token)) throw new InvalidOperationException("final_view_not_recovered");
            snapshots = await graph.SnapshotCountAsync(token);
            var finalSystemAt = await LatestAsync(graph, "system", token); var finalCpuAt = await LatestAsync(graph, "cpu-memory", token); var finalNow = DateTimeOffset.UtcNow;
            finalSystemAgeSeconds = (finalNow - finalSystemAt).TotalSeconds; finalCpuAgeSeconds = (finalNow - finalCpuAt).TotalSeconds;
            if (!ProgressVerified(finalSystemAt, finalNow, snapshots, lastFaultSnapshotCount)
                || !ProgressVerified(finalCpuAt, finalNow, snapshots, lastFaultSnapshotCount))
                throw new InvalidOperationException("soak_final_collector_progress_failed");
            var finalHeads = await graph.DurableStore.ReadIncidentHeadsAsync(token); incidents = finalHeads.Count;
            recurrence = finalHeads.Sum(head => head.RecurrenceCount);
            await graph.StopAsync(token);
            hostQuickCheck = await CompleteStorageProbe.QuickCheckReadOnlyAsync(graph.DatabasePath, token);
            notificationsQuickCheck = await CompleteStorageProbe.QuickCheckReadOnlyAsync(graph.NotificationPath, token);
            var trend = EvaluateTrend(samples);
            CaptureOverlap();
            ok = timer.Elapsed.TotalSeconds >= DurationSeconds && hostQuickCheck && notificationsQuickCheck && unexpectedUnavailable == 0
                && activeCycle == 6 && viewSuccess >= 200 && expectedUnavailable >= 12 && requestedCollectorFailures == 12
                && graph.CollectorFault.InjectedFailures == requestedCollectorFailures && graph.CollectorFault.Recoveries == requestedCollectorFailures
                && graph.WriteFault.InjectedFailures >= 12 && outageCycles == 12 && concurrentCycles == 6 && snapshots >= 100 && progressChecks == 6
                && ConcurrentFaultsVerified(concurrentCycles, observedCollectorOverlap, observedPairedOverlap)
                && !duplicateActiveCorrelation && graph.HelperCalls == 0 && trend.Accepted;
            if (!ok) errorCode = "soak_expected_outcome_not_met";
        }
        catch (Exception error)
        {
            lastHealth = graph.Health.Snapshot();
            errorCode = error switch { OperationCanceledException => "soak_deadline_or_guard",
                InvalidOperationException when error.Message is "soak_live_collector_progress_failed" or "soak_final_collector_progress_failed" => error.Message,
                InvalidOperationException when error.Message is "soak_process_memory_bound" or "soak_incident_capacity_exceeded"
                    or "final_view_not_recovered" or "suspended_view_answered" => error.Message,
                TimeoutException when error.Message == "soak_phase_deadline" => "soak_phase_deadline",
                _ => "soak_assertion_or_runtime_failure" };
        }
        finally
        {
            graph.WriteFault.Enabled = false;
            try
            {
                // One shared budget includes stop, disposal and both integrity
                // checks. No independent second 30s stop after budget expiry.
                using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                await graph.DisposeAsync(cleanupDeadline.Token);
                CompleteProbeGuard.CheckLimits();
                hostQuickCheck = await CompleteStorageProbe.QuickCheckReadOnlyAsync(graph.DatabasePath, cleanupDeadline.Token);
                notificationsQuickCheck = await CompleteStorageProbe.QuickCheckReadOnlyAsync(graph.NotificationPath, cleanupDeadline.Token);
                if (ok && (!hostQuickCheck || !notificationsQuickCheck)) { ok = false; errorCode = "soak_post_stop_integrity_failed"; }
            }
            catch (Exception)
            { if (ok) { ok = false; errorCode = "soak_stop_or_final_integrity_unconfirmed"; } }
        }
        CaptureOverlap();
        if (CompleteProbeGuard.ResourceFailureCode is { } resourceError) { ok = false; errorCode = resourceError; }
        var result = new { finished = true, mode = "complete_soak", fixtureId = id.ToString("N"), ok, errorCode,
            elapsedSeconds = Math.Round(timer.Elapsed.TotalSeconds, 1), configuredSeconds = DurationSeconds, directory,
            completedCycles = activeCycle, activePhase, activePhaseSeconds, phaseProofs, lastHealth,
            minimumFreeMiB = Math.Round(minimumFreeMiB, 1), viewSuccess, expectedUnavailable, unexpectedUnavailable,
            requestedCollectorFailures, injectedCollectorFailures = graph.CollectorFault.InjectedFailures, collectorRecoveries = graph.CollectorFault.Recoveries,
            writeFailures = graph.WriteFault.InjectedFailures, outageCycles, concurrentCycles,
            schedule = "observed_recovery_between_faults", observationBudgetSeconds = 40, recoveryBudgetSeconds = 100,
            actualCollectorOverlapCycles = observedCollectorOverlap, actualWriteOverlapCycles = observedWriteOverlap,
            actualThreeFaultOverlapCycles = observedPairedOverlap, progressChecks,
            finalSystemAgeSeconds, finalCpuAgeSeconds, snapshots, incidents, recurrence, duplicateActiveCorrelation,
            resourceAbort = CompleteProbeGuard.ResourceFailureCode is not null, resourceFailureCode = CompleteProbeGuard.ResourceFailureCode,
            helperCalls = graph.HelperCalls, hostQuickCheck = hostQuickCheck ? "ok" : "not_ok_or_not_run",
            notificationsQuickCheck = notificationsQuickCheck ? "ok" : "not_ok_or_not_run", trend = EvaluateTrend(samples), sampleCount = samples.Count,
            writeFailureMode = "private_live_sqlite_full_write_boundary_equivalent", physicalVolumeFullTested = false, fullProductLongTermLeakQualification = false };
        await File.WriteAllTextAsync(Path.Combine(directory, "soak-result.json"), JsonSerializer.Serialize(result), CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(result)); return ok ? 0 : 1;
        void CaptureOverlap()
        {
            lock (overlapLock)
            {
                observedCollectorOverlap = collectorOverlapCycles.Count; observedWriteOverlap = writeOverlapCycles.Count;
                observedPairedOverlap = collectorOverlapCycles.Intersect(writeOverlapCycles).Count();
            }
        }
    }
    private static async Task<TelemetrySnapshot?> LatestSnapshotAsync(PrivateRuntimeGraph graph, string collector, CancellationToken token)
    {
        var values = await graph.DurableStore.ReadTelemetrySnapshotsAsync(collector, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddSeconds(1), 1, token);
        return values.Count == 1 ? values[0] : null;
    }
    private static async Task<DateTimeOffset> LatestAsync(PrivateRuntimeGraph graph, string collector, CancellationToken token)
    {
        var snapshots = await graph.DurableStore.ReadTelemetrySnapshotsAsync(collector, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddSeconds(1), 1, token);
        return snapshots.Count == 1 ? snapshots[0].SampledAtUtc : DateTimeOffset.MinValue;
    }

    public sealed record SoakSample(int Seconds, long WorkingSetBytes, long PrivateBytes, int Handles, int Threads, long StorageBytes, int Incidents);
    public sealed record SoakTrend(bool Accepted, int WarmSamples, double PrivateGrowthMiB, double HandleGrowth, double ThreadGrowth, long MaximumStorageBytes);
    public static SoakTrend EvaluateTrend(IReadOnlyList<SoakSample> samples)
    {
        var warm = samples.Where(sample => sample.Seconds >= 120).ToArray();
        if (warm.Length < 20) return new(false, warm.Length, 0, 0, 0, samples.Count == 0 ? 0 : samples.Max(sample => sample.StorageBytes));
        var first = warm.Take(10).ToArray(); var last = warm.TakeLast(10).ToArray();
        var privateGrowth = (last.Average(sample => sample.PrivateBytes) - first.Average(sample => sample.PrivateBytes)) / 1048576d;
        var handleGrowth = last.Average(sample => sample.Handles) - first.Average(sample => sample.Handles);
        var threadGrowth = last.Average(sample => sample.Threads) - first.Average(sample => sample.Threads);
        var storage = samples.Max(sample => sample.StorageBytes);
        return new(privateGrowth <= 128 && handleGrowth <= 128 && threadGrowth <= 32 && storage <= 128L * 1024 * 1024,
            warm.Length, Math.Round(privateGrowth, 1), Math.Round(handleGrowth, 1), Math.Round(threadGrowth, 1), storage);
    }
}
