using System.Text.Json;
using HostAgent.Core.Models.Inventory;
using HostAgent.Infrastructure.Collectors;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Service;
using Xunit;
using Status = HostAgent.Infrastructure.Collectors.WindowsDeviceHealthReader.Status;

namespace HostAgent.Infrastructure.Tests;

public sealed class DeviceHealthTests
{
    [Theory]
    [InlineData(0,0,22,"healthy",null)]
    [InlineData(0,1024,22,"unknown","disabled")]
    [InlineData(0,1024,10,"unhealthy","unhealthy")]
    [InlineData(0,1024,0,"unknown","unknown")]
    [InlineData(13,1024,10,"unavailable","unavailable")]
    [InlineData(51,1024,10,"access_denied","access_denied")]
    public void Windows_return_and_problem_bit_govern_classification(int result,int flags,int problem,
        string outcome,string? finding)
    {
        var report=Project(new(Guid.Empty,(uint)result,(uint)flags,(uint)problem));
        Assert.Equal(outcome,report.Outcome);
        if(finding is null) Assert.Empty(report.Findings);
        else
        {
            var row=Assert.Single(report.Findings);
            Assert.Equal(finding,row.State);
            Assert.Equal(finding is "disabled" or "unhealthy" ? (uint?)problem : null,row.ProblemCode);
        }
    }

    [Fact]
    public void Bounded_and_missing_samples_do_not_assert_health()
    {
        Assert.Equal("unknown",Project().Outcome);
        var limit=WindowsDeviceHealthReader.Project(Enumerable.Repeat(new Status(Guid.Empty,0,1024,22),1100),
            TimeProvider.System,default);
        Assert.Equal("partial",limit.Coverage.Status);
        Assert.Equal(1024,limit.Enumerated);
        Assert.Equal(32,limit.Findings.Count);
        Assert.Equal("unknown",limit.Outcome);
        var partial=Project(new(Guid.Empty,0,0,0),new(Guid.Empty,13,1024,10));
        Assert.Equal("partial",partial.Coverage.Status);
        Assert.Equal("unknown",partial.Outcome);
    }

    [Fact]
    public async Task Report_is_fixed_read_only_bounded_and_expired_findings_are_historical()
    {
        await using var fixture=await OperatingModeTests.Fixture.Create();
        var cache=new DeviceHealthCache();
        var report=Project(new Status(Guid.NewGuid(),0,1024,10));
        cache.Publish(report,default);
        var at=report.Coverage.AtUtc!.Value;
        var stale=cache.Read(at.AddMinutes(16));
        Assert.Equal("unknown",stale.Outcome);
        Assert.Equal("stale",stale.Coverage.Status);
        Assert.Single(stale.Findings);
        Assert.Equal(at,stale.Coverage.AtUtc);
        var dispatcher=new DashboardDispatcher(fixture.Store,new(),new(),TimeProvider.System,deviceHealth:cache);
        var bytes=await dispatcher.DispatchAsync(ViewProtocol.Request("device-health",new{}),default);
        Assert.InRange(bytes.Length,1,HelperProtocol.MaximumFrameBytes);
        using var reply=JsonDocument.Parse(bytes);
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        var read=reply.RootElement.GetProperty("data").Deserialize<DeviceHealthReport>()!;
        Assert.Equal("unhealthy",read.Outcome);
        Assert.Equal(0,fixture.Helper.Calls);
        Assert.Throws<InvalidDataException>(()=>ViewProtocol.Request("device-health",new{target="another"}));
    }

    [Fact]
    public void Native_read_returns_only_bounded_local_device_evidence()
    {
        var report=new WindowsDeviceHealthReader(TimeProvider.System).Read(default);
        Assert.InRange(report.Enumerated,0,1024);
        Assert.InRange(report.Checked,0,report.Enumerated);
        Assert.InRange(report.Findings.Count,0,32);
        Assert.Equal(DeviceHealthReport.Source,report.Coverage.Source);
        Assert.NotNull(report.Coverage.AtUtc);
    }

    private static DeviceHealthReport Project(params Status[] statuses) =>
        WindowsDeviceHealthReader.Project(statuses,TimeProvider.System,default);
}
