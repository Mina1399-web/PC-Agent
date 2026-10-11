using System.Text.Json;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Diagnosis;
using HostAgent.Core.Models.Incidents;

namespace HostAgent.Core.Diagnostics;

public sealed class IncidentEngine : IIncidentEngine
{
    private readonly Dictionary<string, Incident> _incidents = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public ValueTask<Incident> ApplyAsync(Diagnosis diagnosis, IncidentSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnosis);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosis.CorrelationKey);
        cancellationToken.ThrowIfCancellationRequested();
        if (diagnosis.Confidence is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(diagnosis));
        lock (_gate)
        {
            _incidents.TryGetValue(diagnosis.CorrelationKey, out var prior);
            if (prior is not null && diagnosis.ObservedAtUtc <= prior.UpdatedAtUtc)
                return ValueTask.FromResult(Clone(prior));
            var id = prior?.IncidentId ?? Guid.NewGuid().ToString("N");
            var state = prior is null || prior.State == IncidentLifecycleState.Resolved
                ? IncidentLifecycleState.Detected
                : prior.State == IncidentLifecycleState.Detected ? IncidentLifecycleState.Active : prior.State;
            var updated = new Incident(id, diagnosis.CorrelationKey, state,
                prior?.CreatedAtUtc ?? diagnosis.ObservedAtUtc, diagnosis.ObservedAtUtc,
                (prior?.RecurrenceCount ?? 0) + 1, diagnosis with { IncidentState = state },
                AddSnapshot(prior, snapshot with { IncidentId = id, DiagnosticEvidence = diagnosis.Evidence }));
            _incidents[diagnosis.CorrelationKey] = Clone(updated);
            return ValueTask.FromResult(Clone(updated));
        }
    }

    public ValueTask<Incident> TransitionAsync(string incidentId, IncidentLifecycleState state, IncidentSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var prior = _incidents.Values.SingleOrDefault(item => item.IncidentId == incidentId)
                ?? throw new KeyNotFoundException("Unknown incident.");
            if (state == prior.State) return ValueTask.FromResult(Clone(prior));
            if (snapshot.CapturedAtUtc < prior.UpdatedAtUtc || !CanTransition(prior.State, state))
                throw new InvalidOperationException("The incident transition is invalid or predates current state.");
            var updated = prior with
            {
                State = state, UpdatedAtUtc = snapshot.CapturedAtUtc,
                LatestDiagnosis = prior.LatestDiagnosis with { IncidentState = state },
                Snapshots = AddSnapshot(prior, snapshot with { IncidentId = incidentId, DiagnosticEvidence = prior.LatestDiagnosis.Evidence })
            };
            _incidents[prior.CorrelationKey] = Clone(updated);
            return ValueTask.FromResult(Clone(updated));
        }
    }

    public void Restore(IEnumerable<Incident> incidents)
    {
        ArgumentNullException.ThrowIfNull(incidents);
        lock (_gate)
        {
            var restored = incidents.ToDictionary(item => item.CorrelationKey, Clone, StringComparer.Ordinal);
            _incidents.Clear();
            foreach (var entry in restored) _incidents.Add(entry.Key, entry.Value);
        }
    }

    private static IReadOnlyList<IncidentSnapshot> AddSnapshot(Incident? prior, IncidentSnapshot snapshot) =>
        (prior?.Snapshots ?? []).Append(snapshot).TakeLast(64).ToArray();

    private static Incident Clone(Incident incident) =>
        JsonSerializer.Deserialize<Incident>(JsonSerializer.Serialize(incident))!;

    public static bool CanTransition(IncidentLifecycleState from, IncidentLifecycleState to) => (from, to) switch
    {
        (IncidentLifecycleState.Detected, IncidentLifecycleState.Active or IncidentLifecycleState.Resolved or IncidentLifecycleState.Escalated) => true,
        (IncidentLifecycleState.Active, IncidentLifecycleState.Acknowledged or IncidentLifecycleState.RemediationProposed or IncidentLifecycleState.Resolved or IncidentLifecycleState.Escalated) => true,
        (IncidentLifecycleState.Acknowledged, IncidentLifecycleState.RemediationProposed or IncidentLifecycleState.Resolved or IncidentLifecycleState.Escalated) => true,
        (IncidentLifecycleState.RemediationProposed, IncidentLifecycleState.RemediationPendingApproval or IncidentLifecycleState.Resolved or IncidentLifecycleState.Escalated) => true,
        (IncidentLifecycleState.RemediationPendingApproval, IncidentLifecycleState.Remediating or IncidentLifecycleState.Resolved or IncidentLifecycleState.Escalated) => true,
        (IncidentLifecycleState.Remediating, IncidentLifecycleState.Resolved or IncidentLifecycleState.Failed or IncidentLifecycleState.Escalated) => true,
        (IncidentLifecycleState.Failed, IncidentLifecycleState.RemediationProposed or IncidentLifecycleState.Escalated or IncidentLifecycleState.Resolved) => true,
        (IncidentLifecycleState.Escalated, IncidentLifecycleState.Acknowledged or IncidentLifecycleState.Resolved) => true,
        _ => false
    };
}
