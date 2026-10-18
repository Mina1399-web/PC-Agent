public sealed record CompleteSoakObservation(long Snapshots, Guid CpuId, Guid SystemId,
    DateTimeOffset CpuAt, DateTimeOffset SystemAt, DateTimeOffset Now,
    int Failures, int Recoveries, int WriteFailures, bool ActualTripleOverlap, bool ActualViewUnavailable = false);

// DEV harness only: no subsequent fault is scheduled until the preceding
// persistence/collector recovery is observed. UTC freshness remains strict;
// phase deadlines use monotonic elapsed time supplied by the probe.
public sealed class CompleteSoakCycle(int cycle, TimeSpan started)
{
    public enum Phase { ViewOutage, CollectorFault, CollectorRecovery, WriteFault, WriteRecovery, CombinedFault, CombinedRecovery, Completed }
    public Phase Stage { get; private set; } = Phase.ViewOutage;
    public TimeSpan PhaseStarted { get; private set; } = started;
    public bool SuspendView => Stage is Phase.ViewOutage or Phase.CombinedFault;
    public bool EnableWriteFault => Stage is Phase.WriteFault or Phase.CombinedFault;
    public bool NeedsCollectorRequest => Stage is Phase.CollectorFault or Phase.CombinedFault;
    public long BeforeSnapshots { get; private set; }
    private Guid beforeCpu, beforeSystem;
    private int beforeWrites;
    public void MarkViewSuspended(TimeSpan elapsed)
    {
        if (Stage is not (Phase.ViewOutage or Phase.CombinedFault))
            throw new InvalidOperationException("outage_activation_stage_required");
        PhaseStarted = elapsed;
    }
    public void MarkArmed(TimeSpan elapsed)
    {
        if (Stage != Phase.CombinedFault) throw new InvalidOperationException("combined_arm_stage_required");
        PhaseStarted = elapsed;
    }

    public bool Fresh(CompleteSoakObservation value)
        => value.CpuId != Guid.Empty && value.SystemId != Guid.Empty
            && value.CpuId != beforeCpu && value.SystemId != beforeSystem
            && CompleteSoakProbe.ProgressVerified(value.CpuAt, value.Now, value.Snapshots, BeforeSnapshots)
            && CompleteSoakProbe.ProgressVerified(value.SystemAt, value.Now, value.Snapshots, BeforeSnapshots);

    public bool Advance(TimeSpan elapsed, CompleteSoakObservation value)
    {
        if (Stage == Phase.Completed) return false;
        var age = elapsed - PhaseStarted;
        var recovery = Stage is Phase.CollectorRecovery or Phase.WriteRecovery or Phase.CombinedRecovery;
        var budget = TimeSpan.FromSeconds(recovery ? 100 : Stage == Phase.ViewOutage ? 30 : 40);
        var complete = Stage switch
        {
            Phase.ViewOutage => age >= TimeSpan.FromSeconds(10) && value.ActualViewUnavailable,
            Phase.CollectorFault => value.Failures == cycle * 2 + 1,
            Phase.CollectorRecovery => Fresh(value) && value.Recoveries == cycle * 2 + 1,
            Phase.WriteFault => value.WriteFailures > beforeWrites,
            Phase.WriteRecovery => Fresh(value),
            Phase.CombinedFault => age >= TimeSpan.FromSeconds(10) && value.ActualViewUnavailable && value.Failures == cycle * 2 + 2 && value.ActualTripleOverlap,
            Phase.CombinedRecovery => Fresh(value) && value.Recoveries == cycle * 2 + 2,
            _ => false
        };
        if (CompleteSoakProbe.RecoveryCheckpoint(complete, true, age, budget) == CompleteSoakProbe.RecoveryDecision.Failed)
            throw new TimeoutException("soak_phase_deadline");
        if (!complete) return false;
        Stage++;
        PhaseStarted = elapsed;
        BeforeSnapshots = value.Snapshots;
        beforeCpu = value.CpuId; beforeSystem = value.SystemId; beforeWrites = value.WriteFailures;
        return true;
    }
}
