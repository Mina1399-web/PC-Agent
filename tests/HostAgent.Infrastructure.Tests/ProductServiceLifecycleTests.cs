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
    private sealed class Fake(string name,List<string> calls,ServiceControllerStatus status=ServiceControllerStatus.Running):IProductServiceHandle
    {
        public string[] Dependents {get;init;}=[];
        public ServiceControllerStatus Status=>status;
        public IReadOnlyList<string> RunningDependents=>Dependents;
        public void Start()=>calls.Add("start:"+name);
        public void Stop(bool includeDependents)=>calls.Add($"stop:{name}:{includeDependents}");
        public Task WaitAsync(ServiceControllerStatus expected,TimeSpan timeout,CancellationToken token)=>Task.CompletedTask;
        public void Dispose(){}
    }
}
