namespace HostAgent.Core.Dashboard;

public enum ObservationState { Missing, Pending, Unconfirmed, AwaitingMeasurement, Observed }
public sealed class OperationObservation
{
    public DashboardSnapshot? Before {get;private set;}
    public bool Pending {get;private set;}
    public ViewMetric? PostOperationMetric(ViewMetric? metric)=>!Pending&&_confirmed&&metric?.AtUtc>_completedAt?metric:null;
    private DateTimeOffset? _completedAt;
    private bool _confirmed;
    public void Begin(DashboardSnapshot before)
    {
        if(Pending)throw new InvalidOperationException("operation_already_pending");
        Before=before;Pending=true;_completedAt=null;_confirmed=false;
    }
    public void Complete(DateTimeOffset at,bool confirmed){_completedAt=at;_confirmed=confirmed;Pending=false;}
    public ObservationState Compare(DashboardSnapshot? after)
        =>Before is null||after is null?ObservationState.Missing:Pending?ObservationState.Pending:
            !_confirmed?ObservationState.Unconfirmed:
            after.Cpu?.AtUtc>_completedAt || after.Memory?.AtUtc>_completedAt?ObservationState.Observed:ObservationState.AwaitingMeasurement;
}
