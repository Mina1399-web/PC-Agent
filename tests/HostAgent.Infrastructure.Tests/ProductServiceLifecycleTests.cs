using System.ServiceProcess;
using HostAgent.Cli;
using Xunit;
namespace HostAgent.Infrastructure.Tests;

public sealed class ProductServiceLifecycleTests
{
    [Fact]
    public async Task Product_stop_is_non_cascading_and_start_uses_reverse_order()
    {
        var calls=new List<string>();var lifecycle=new ProductServiceLifecycle(name=>new Fake(name,calls));
        await lifecycle.RunAsync(false,default);Assert.Equal(new[]{"stop:HostAgent:False","stop:HostAgent.Helper:False"},calls);
        calls.Clear();lifecycle=new(name=>new Fake(name,calls,ServiceControllerStatus.Stopped));
        await lifecycle.RunAsync(true,default);Assert.Equal(new[]{"start:HostAgent.Helper","start:HostAgent"},calls);
    }
    [Fact]
    public async Task Running_unrelated_dependent_blocks_before_any_product_stop()
    {
        var calls=new List<string>();var lifecycle=new ProductServiceLifecycle(name=>new Fake(name,calls){Dependents=name=="HostAgent.Helper"?["UnrelatedService"]:[]});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>lifecycle.RunAsync(false,default));Assert.Empty(calls);
    }
    // A failed helper restart must not strand the independently useful monitoring service.
    [Theory]
    [InlineData("open")]
    [InlineData("start")]
    [InlineData("wait")]
    public async Task Recovery_attempts_both_fixed_services_and_reports_all_start_failures(string failureStage)
    {
        var calls=new List<string>();
        var lifecycle=new ProductServiceLifecycle(name=>
        {
            if(name=="HostAgent.Helper"&&failureStage=="open")
            {calls.Add("open_failed:"+name);throw new InvalidOperationException("synthetic_open_failure");}
            return new Fake(name,calls,ServiceControllerStatus.Stopped)
                {FailureStage=name=="HostAgent.Helper"?failureStage:null};
        });
        var error=await Assert.ThrowsAnyAsync<Exception>(()=>lifecycle.RunAsync(true,default));
        Assert.Contains("start:HostAgent",calls);
        var recovery=Assert.IsType<AggregateException>(error);
        Assert.Single(recovery.InnerExceptions);
        Assert.Contains("synthetic_",recovery.InnerExceptions[0].Message);
        Assert.Equal("start:HostAgent",calls[^1]);
    }
    [Fact]
    public async Task Recovery_retains_failures_from_both_services_instead_of_reporting_success()
    {
        var calls=new List<string>();
        var lifecycle=new ProductServiceLifecycle(name=>new Fake(name,calls,ServiceControllerStatus.Stopped){FailureStage="start"});
        var error=await Assert.ThrowsAsync<AggregateException>(()=>lifecycle.RunAsync(true,default));
        Assert.Equal(2,error.InnerExceptions.Count);
        Assert.Equal(new[]{"start:HostAgent.Helper","start:HostAgent"},calls);
    }
    private sealed class Fake(string name,List<string> calls,ServiceControllerStatus status=ServiceControllerStatus.Running):IProductServiceHandle
    {
        public string[] Dependents {get;init;}=[];
        public string? FailureStage {get;init;}
        public ServiceControllerStatus Status=>status;
        public IReadOnlyList<string> RunningDependents=>Dependents;
        public void Start(){calls.Add("start:"+name);if(FailureStage=="start")throw new InvalidOperationException("synthetic_start_failure");}
        public void Stop(bool includeDependents)=>calls.Add($"stop:{name}:{includeDependents}");
        public Task WaitAsync(ServiceControllerStatus expected,TimeSpan timeout,CancellationToken token)=>
            FailureStage=="wait"?Task.FromException(new System.TimeoutException("synthetic_wait_failure")):Task.CompletedTask;
        public void Dispose(){}
    }
}
