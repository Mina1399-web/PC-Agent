using System.Reflection;
using HostAgent.Core.Models.Diagnosis;
using HostAgent.Core.Models.Incidents;
using HostAgent.Core.Models.Telemetry;
using Xunit;

namespace HostAgent.Core.Tests;

public sealed class IncidentEngineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-11T12:00:00Z");

    [Fact]
    public async Task Repeated_diagnosis_updates_one_incident_and_freezes_original_snapshot()
    {
        var engine = Engine();
        var volumes = new List<DiskVolumeTelemetry> { new("C:\\", "System", "NTFS", 100, 98, 2, 98) };
        var snapshot = Snapshot(Now) with { Disk = new DiskTelemetry(volumes, null, []) };
        var first = await Apply(engine, Diagnosis(Now), snapshot);
        volumes.Clear();
        var second = await Apply(engine, Diagnosis(Now.AddMinutes(1)), Snapshot(Now.AddMinutes(1)));
        Assert.Equal(first.IncidentId, second.IncidentId);
        Assert.Equal(2, second.RecurrenceCount);
        Assert.Equal(IncidentLifecycleState.Active, second.State);
        Assert.Equal(98, Assert.Single(first.Snapshots[0].Disk!.Volumes).UsedPercent);
        Assert.Equal(2, second.Snapshots.Count);
        var replay = await Apply(engine, Diagnosis(Now.AddMinutes(1)), Snapshot(Now.AddMinutes(1)));
        Assert.Equal(2, replay.RecurrenceCount);
        Assert.Equal(2, replay.Snapshots.Count);
        var separate = await Apply(engine, Diagnosis(Now) with { CorrelationKey = "disk:D" }, Snapshot(Now));
        Assert.NotEqual(first.IncidentId, separate.IncidentId);
    }

    [Fact]
    public async Task Lifecycle_accepts_supported_paths_rejects_invalid_moves_and_reopens_resolved_issue()
    {
        var engine = Engine();
        var current = await Apply(engine, Diagnosis(Now), Snapshot(Now));
        var steps = new[] { IncidentLifecycleState.Active, IncidentLifecycleState.Acknowledged,
            IncidentLifecycleState.RemediationProposed, IncidentLifecycleState.RemediationPendingApproval,
            IncidentLifecycleState.Remediating, IncidentLifecycleState.Failed,
            IncidentLifecycleState.Escalated, IncidentLifecycleState.Resolved };
        for (var index = 0; index < steps.Length; index++)
        {
            current = await Transition(engine, current.IncidentId, steps[index], Snapshot(Now.AddMinutes(index + 1)));
            Assert.Equal(steps[index], current.State);
            Assert.Equal(current.State, current.LatestDiagnosis.IncidentState);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => Transition(engine, current.IncidentId,
            IncidentLifecycleState.Remediating, Snapshot(Now.AddHours(1))));
        var reopened = await Apply(engine, Diagnosis(Now.AddDays(1)), Snapshot(Now.AddDays(1)));
        Assert.Equal(current.IncidentId, reopened.IncidentId);
        Assert.Equal(IncidentLifecycleState.Detected, reopened.State);
        Assert.Equal(2, reopened.RecurrenceCount);
    }

    [Theory]
    [InlineData(IncidentLifecycleState.Detected, IncidentLifecycleState.Remediating)]
    [InlineData(IncidentLifecycleState.Active, IncidentLifecycleState.Remediating)]
    [InlineData(IncidentLifecycleState.RemediationProposed, IncidentLifecycleState.Remediating)]
    public async Task Lifecycle_cannot_skip_approval_states(IncidentLifecycleState from, IncidentLifecycleState to)
    {
        var engine = Engine();
        var incident = await Apply(engine, Diagnosis(Now), Snapshot(Now));
        if (from != IncidentLifecycleState.Detected)
            incident = await Transition(engine, incident.IncidentId, IncidentLifecycleState.Active, Snapshot(Now.AddMinutes(1)));
        if (from == IncidentLifecycleState.RemediationProposed)
            incident = await Transition(engine, incident.IncidentId, from, Snapshot(Now.AddMinutes(2)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Transition(engine, incident.IncidentId, to, Snapshot(Now.AddHours(1))));
    }

    private static object Engine()
    {
        var type = typeof(Incident).Assembly.GetType("HostAgent.Core.Diagnostics.IncidentEngine");
        Assert.NotNull(type);
        return Activator.CreateInstance(type!)!;
    }

    [Fact]
    public async Task Every_legal_transition_and_every_invalid_pair_has_an_explicit_assertion()
    {
        var legal = new Dictionary<IncidentLifecycleState, IncidentLifecycleState[]>
        {
            [IncidentLifecycleState.Detected] = [IncidentLifecycleState.Active, IncidentLifecycleState.Resolved, IncidentLifecycleState.Escalated],
            [IncidentLifecycleState.Active] = [IncidentLifecycleState.Acknowledged, IncidentLifecycleState.RemediationProposed, IncidentLifecycleState.Resolved, IncidentLifecycleState.Escalated],
            [IncidentLifecycleState.Acknowledged] = [IncidentLifecycleState.RemediationProposed, IncidentLifecycleState.Resolved, IncidentLifecycleState.Escalated],
            [IncidentLifecycleState.RemediationProposed] = [IncidentLifecycleState.RemediationPendingApproval, IncidentLifecycleState.Resolved, IncidentLifecycleState.Escalated],
            [IncidentLifecycleState.RemediationPendingApproval] = [IncidentLifecycleState.Remediating, IncidentLifecycleState.Resolved, IncidentLifecycleState.Escalated],
            [IncidentLifecycleState.Remediating] = [IncidentLifecycleState.Resolved, IncidentLifecycleState.Failed, IncidentLifecycleState.Escalated],
            [IncidentLifecycleState.Failed] = [IncidentLifecycleState.RemediationProposed, IncidentLifecycleState.Escalated, IncidentLifecycleState.Resolved],
            [IncidentLifecycleState.Escalated] = [IncidentLifecycleState.Acknowledged, IncidentLifecycleState.Resolved],
            [IncidentLifecycleState.Resolved] = []
        };
        foreach (var from in Enum.GetValues<IncidentLifecycleState>())
        foreach (var to in Enum.GetValues<IncidentLifecycleState>())
        {
            var engine = new HostAgent.Core.Diagnostics.IncidentEngine();
            var incident = await engine.ApplyAsync(Diagnosis(Now), Snapshot(Now));
            engine.Restore([incident with { State = from, LatestDiagnosis = incident.LatestDiagnosis with { IncidentState = from } }]);
            if (from == to || legal[from].Contains(to))
                Assert.Equal(to, (await engine.TransitionAsync(incident.IncidentId, to, Snapshot(Now.AddMinutes(1)))).State);
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => engine.TransitionAsync(incident.IncidentId, to, Snapshot(Now.AddMinutes(1))).AsTask());
        }
    }

    private static async Task<Incident> Apply(object engine, Diagnosis diagnosis, IncidentSnapshot snapshot) =>
        await ((HostAgent.Core.Contracts.IIncidentEngine)engine).ApplyAsync(diagnosis, snapshot, CancellationToken.None);
    private static async Task<Incident> Transition(object engine, string id, IncidentLifecycleState state, IncidentSnapshot snapshot) =>
        await ((HostAgent.Core.Contracts.IIncidentEngine)engine).TransitionAsync(id, state, snapshot, CancellationToken.None);
    public static Diagnosis Diagnosis(DateTimeOffset at) => new(Guid.NewGuid().ToString("N"), "disk:C", at,
        DiagnosisSeverity.Critical, 94, [new("evidence", "disk", "full", 98, at)],
        new("disk_capacity", "full", "C:\\"), [], new("review", "inspect"), IncidentLifecycleState.Detected);
    public static IncidentSnapshot Snapshot(DateTimeOffset at) => new(Guid.NewGuid().ToString("N"), "", at,
        null, null, null, null, null, null, [], []);
}
