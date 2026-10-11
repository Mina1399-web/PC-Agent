using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Incidents;
using HostAgent.Infrastructure.Security;
using HostAgent.Core.Models.Audit;
using HostAgent.Core.Models.Policy;
using HostAgent.Core.Models.Diagnosis;
namespace HostAgent.Service;
public sealed class IncidentPublisher(IHostAgentStore store,INotificationStore notifications,ServiceConfiguration configuration)
{
    public async ValueTask PublishAsync(IReadOnlyList<Incident> incidents,DateTimeOffset now,CancellationToken cancellationToken)
    {
        foreach (var incident in incidents)
        {
            // One notice per severity/state/occurrence epoch. Resolution/reopening allows a fresh notification.
            var id=incident.IncidentId+":"+incident.State+":"+incident.LatestDiagnosis.Severity+":"+
                (incident.OccurrenceStartedAtUtc??incident.CreatedAtUtc).UtcTicks;
            if (await notifications.ReadAsync(id,cancellationToken) is not null) continue;
            var severity=incident.State==IncidentLifecycleState.Escalated?DiagnosisSeverity.Critical:incident.LatestDiagnosis.Severity;
            if (configuration.Mode==OperatingMode.Shadow)
                await store.AppendAuditEntryAsync(new(Guid.NewGuid().ToString("N"),now,"HostAgent",incident.IncidentId,incident.LatestDiagnosis.DiagnosisId,
                    incident.LatestDiagnosis.Confidence,incident.LatestDiagnosis.Evidence.Select(value=>value.EvidenceId).ToArray(),
                    incident.LatestDiagnosis.RecommendedRemediation.RecommendationCode,PolicyDecision.READ_ONLY,ApprovalStatus.Pending,
                    null,null,"shadow_recommendation",null),cancellationToken);
            // Fixed template sends no raw evidence, process path, event payload, identity, or credentials.
            await notifications.EnqueueAsync(new(id,incident.IncidentId,severity,
                $"Host Agent incident {incident.IncidentId}: {severity}; state {incident.State}; confidence {incident.LatestDiagnosis.Confidence}."),now,cancellationToken);
        }
    }
}
