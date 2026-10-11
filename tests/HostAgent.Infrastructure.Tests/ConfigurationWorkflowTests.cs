using HostAgent.Cli;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class ConfigurationWorkflowTests
{
    [Fact]
    public async Task Declined_configuration_has_no_effects_and_accepted_configuration_is_bracketed_by_stop_and_restart()
    {
        var calls=new List<string>();var accepted=false;
        var workflow=new ConfigurationWorkflow(args=>accepted,token=>{calls.Add("stop");return Task.CompletedTask;},args=>calls.Add("apply"),token=>{calls.Add("start");return Task.CompletedTask;});
        Assert.False(await workflow.RunAsync(["configure","Shadow","off","off"],default));Assert.Empty(calls);
        accepted=true;Assert.True(await workflow.RunAsync(["configure","Shadow","off","off"],default));
        Assert.Equal(new[]{"stop","apply","start"},calls);
    }
    [Fact]
    public async Task Failed_configuration_still_restarts_product_services_and_propagates_failure()
    {
        var calls=new List<string>();
        var workflow=new ConfigurationWorkflow(_=>true,token=>{calls.Add("stop");return Task.CompletedTask;},_=>throw new InvalidDataException("synthetic"),token=>{calls.Add("start");return Task.CompletedTask;});
        await Assert.ThrowsAsync<InvalidDataException>(()=>workflow.RunAsync(["configure","Shadow","off","off"],default));
        Assert.Equal(new[]{"stop","start"},calls);
    }
}
