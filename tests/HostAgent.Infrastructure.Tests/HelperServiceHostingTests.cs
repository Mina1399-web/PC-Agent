using HostAgent.Helper;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class HelperServiceHostingTests
{
    [Fact]
    public async Task Listener_shutdown_cancels_pending_accept_without_recovery_failure()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fatal=false;
        using var worker=new HelperListenerWorker(async token=>
        {
            entered.TrySetResult();
            try{await Task.Delay(Timeout.InfiniteTimeSpan,token);}
            catch(OperationCanceledException){cancelled.TrySetResult();throw;}
        },_=>{},()=>fatal=true,TimeSpan.FromMilliseconds(10));
        await worker.StartAsync(default);
        Assert.Same(entered.Task,await Task.WhenAny(entered.Task,Task.Delay(1000)));
        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(cancelled.Task.IsCompleted);
        Assert.False(fatal);
    }
    [Fact]
    public async Task Repeated_listener_failure_backs_off_and_requests_failure_exit_after_five_attempts()
    {
        var calls=0;
        var stopped=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reports=new List<string>();
        using var worker=new HelperListenerWorker(_=>{Interlocked.Increment(ref calls);throw new IOException("synthetic confidential");},
            code=>reports.Add(code),()=>stopped.TrySetResult(),TimeSpan.FromMilliseconds(10));
        await worker.StartAsync(default);
        Assert.Same(stopped.Task,await Task.WhenAny(stopped.Task,Task.Delay(2000)));
        await worker.StopAsync(default);
        Assert.Equal(5,calls);
        Assert.Equal(5,reports.Count(code=>code=="helper_listener_unavailable"));
        Assert.DoesNotContain(reports,code=>code.Contains("confidential"));
    }
}
