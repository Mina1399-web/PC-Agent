using HostAgent.Core.Models.Diagnosis;
using HostAgent.Core.Models.Incidents;

namespace HostAgent.Core.Contracts;

public interface IIncidentEngine
{
    ValueTask<Incident> ApplyAsync(Diagnosis diagnosis, IncidentSnapshot snapshot, CancellationToken cancellationToken);
    ValueTask<Incident> TransitionAsync(string incidentId, IncidentLifecycleState state, IncidentSnapshot snapshot, CancellationToken cancellationToken);
}
