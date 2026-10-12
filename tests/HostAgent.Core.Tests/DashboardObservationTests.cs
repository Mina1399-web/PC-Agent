using HostAgent.Core.Dashboard;
using Xunit;
namespace HostAgent.Core.Tests;

public sealed class DashboardObservationTests
{
    [Fact]
    public void Each_displayed_metric_must_individually_be_newer_than_confirmed_completion()
    {
        var at=DateTimeOffset.UtcNow;var view=new DashboardSnapshot(at,"Confirmation",false,false,new(at,99),new(at,80),null,[],[],[],null,[],[],[],[]);
        var review=new OperationObservation();review.Begin(view);review.Complete(at.AddSeconds(30),true);
        Assert.NotNull(review.PostOperationMetric(new(at.AddSeconds(31),42)));
        Assert.Null(review.PostOperationMetric(new(at.AddSeconds(29),64)));
        Assert.Null(review.PostOperationMetric(new(at.AddSeconds(30),64)));
        review.Begin(view);Assert.Null(review.PostOperationMetric(new(at.AddSeconds(31),42)));
    }
    [Fact]
    public void Pre_approval_sample_is_never_a_post_operation_observation_and_sessions_cannot_overlap()
    {
        var at=DateTimeOffset.UtcNow;
        DashboardSnapshot View(DateTimeOffset stamp)=>new(stamp,"Confirmation",false,false,new(stamp,42),new(stamp,64),null,[],[],[],null,[],[],[],[]);
        var review=new OperationObservation();review.Begin(View(at));
        Assert.Equal(ObservationState.Pending,review.Compare(View(at.AddSeconds(60))));
        Assert.Throws<InvalidOperationException>(()=>review.Begin(View(at)));
        review.Complete(at.AddSeconds(65),true);
        Assert.Equal(ObservationState.AwaitingMeasurement,review.Compare(View(at.AddSeconds(60))));
        Assert.Equal(ObservationState.Observed,review.Compare(View(at.AddSeconds(66))));
    }
    [Fact]
    public void Unknown_or_failed_cli_outcome_cannot_become_confirmed_by_lower_cpu()
    {
        var at=DateTimeOffset.UtcNow;
        var view=new DashboardSnapshot(at,"Confirmation",false,false,new(at,99),null,null,[],[],[],null,[],[],[],[]);
        var review=new OperationObservation();review.Begin(view);review.Complete(at.AddSeconds(30),false);
        Assert.Equal(ObservationState.Unconfirmed,review.Compare(view with{Cpu=new(at.AddSeconds(60),1)}));
    }
}
