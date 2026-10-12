using HostAgent.Core.Dashboard;
using Xunit;

namespace HostAgent.Core.Tests;

public sealed class DashboardRecommendationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static DashboardSnapshot Empty => new(Now,"Shadow",true,false,null,null,null,[],[],[],null,[],[],[],[]);
    private static ViewMetric[] Sustained(double value) => Enumerable.Range(0,5).Select(i=>new ViewMetric(Now.AddMinutes(i-4),value)).ToArray();

    [Fact]
    public void Sustained_cpu_and_memory_have_separate_evidence_and_no_automatic_action()
    {
        var cards=new RecommendationEngine().Evaluate(Empty with{RecentCpu=Sustained(95),RecentMemory=Sustained(93)},Now);
        Assert.Equal(new[]{"sustained_cpu","sustained_memory"},cards.Select(x=>x.Code));
        Assert.Equal(GuidedAction.StartupSettings,cards[0].Action);
        Assert.Equal(GuidedAction.AppsSettings,cards[1].Action);
        Assert.All(cards,x=>{Assert.NotEmpty(x.Evidence);Assert.NotEmpty(x.Impact);Assert.NotEmpty(x.Rollback);});
    }

    [Fact]
    public void Full_disk_recommends_review_instead_of_claiming_files_are_safe_to_delete()
    {
        var cards=new RecommendationEngine().Evaluate(Empty with{Disks=[new("C:",5,100,Now)]},Now);
        var card=Assert.Single(cards);
        Assert.Equal("disk_space:C:",card.Code);
        Assert.Equal(GuidedAction.StorageSettings,card.Action);
        Assert.Contains("5",card.Evidence);
        Assert.Contains("záloh",card.Rollback);
    }

    [Fact]
    public void Single_spike_missing_stale_future_duplicate_or_gapped_samples_do_not_recommend()
    {
        foreach(var samples in new IReadOnlyList<ViewMetric>[] {[],[new(Now,99)],Sustained(99).Select(x=>x with{AtUtc=x.AtUtc.AddHours(-1)}).ToArray(),
            Sustained(99).Select(x=>x with{AtUtc=x.AtUtc.AddHours(1)}).ToArray(),Enumerable.Repeat(new ViewMetric(Now,99),5).ToArray(),
            [new(Now.AddMinutes(-20),99),new(Now.AddMinutes(-10),99),new(Now,99)],Sustained(40),Sustained(double.NaN)})
            Assert.Empty(new RecommendationEngine().Evaluate(Empty with{RecentCpu=samples,RecentMemory=samples},Now));
        Assert.Empty(new RecommendationEngine().Evaluate(Empty with{Disks=[new("C:",1,100,Now.AddHours(-1)),new("D:",1,0,Now)]},Now));
    }

    [Fact]
    public void Crash_card_preserves_classification_only_confidence_and_does_not_offer_driver_repair()
    {
        var cards=new RecommendationEngine().Evaluate(Empty with{Incidents=[new(Guid.NewGuid().ToString("N"),"Active","bugcheck_irql_not_less_or_equal",88,"High",Now,2)]},Now);
        var card=Assert.Single(cards);
        Assert.Equal("analyze_crash",card.Code);
        Assert.Contains("klasifikace",card.Evidence);
        Assert.Contains("88",card.Evidence);
        Assert.Equal(GuidedAction.Investigate,card.Action);
        Assert.Contains("ovladač",card.Impact);
    }

    [Fact]
    public void Resolved_old_or_unrelated_incidents_do_not_create_crash_card()
    {
        Assert.Empty(new RecommendationEngine().Evaluate(Empty with{Incidents=[new("a","Resolved","windows_bugcheck",80,"High",Now,1),
            new("b","Active","windows_bugcheck",80,"High",Now.AddDays(-2),1),new("c","Active","service_crash",90,"High",Now,1)]},Now));
    }
}
