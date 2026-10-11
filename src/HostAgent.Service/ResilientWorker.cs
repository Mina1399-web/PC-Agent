using Microsoft.Extensions.Hosting;
namespace HostAgent.Service;
public sealed class ResilientWorker(Func<CancellationToken,Task> cycle,TimeSpan interval,TimeSpan deadline,
    TimeProvider clock,Action<string> report) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval,TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(deadline,TimeSpan.Zero);
        var failures=0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var deadlineSource=new CancellationTokenSource(deadline,clock);
                using var linked=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken,deadlineSource.Token);
                var running=Task.Run(()=>cycle(linked.Token),CancellationToken.None);
                // Observe eventual exceptions even if shutdown ends the bounded wait first.
                _=running.ContinueWith(task=>{_=task.Exception;},CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
                try
                {
                    await running.WaitAsync(deadline,clock,stoppingToken);
                    if (failures>0) report("cycle_recovered");
                    failures=0;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {await DrainAsync(running);break;}
                catch (Exception error) when (error is TimeoutException || deadlineSource.IsCancellationRequested)
                {
                    linked.Cancel();
                    report("cycle_timeout");
                    failures++;
                    // No overlapping invocation after timeout, even when the dependency ignores cancellation.
                    try { await running.WaitAsync(stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {await DrainAsync(running);break;}
                    catch { /* Timeout already reported with a fixed code. */ }
                }
                catch { report("cycle_failed");failures++; }
                var delay=TimeSpan.FromTicks((long)Math.Min(TimeSpan.FromHours(1).Ticks,
                    interval.Ticks*Math.Pow(2,Math.Min(Math.Max(failures-1,0),6))));
                await Task.Delay(delay,clock,stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        report("worker_stopped");
    }
    private static async Task DrainAsync(Task running)
    {
        // Let cooperative dependencies release connections before DI disposal, without hanging on an uncooperative job.
        try{await running.WaitAsync(TimeSpan.FromMilliseconds(500));}
        catch{}
    }
}
