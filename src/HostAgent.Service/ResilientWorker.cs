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
                var cycleStarted=clock.GetTimestamp();
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
                // Successful sampling uses start-to-start cadence. Slow collectors
                // must not silently add their duration to every sampling interval.
                // Failed cycles retain their full backoff; invocations never overlap.
                if(failures==0)
                    delay=TimeSpan.FromTicks(Math.Max(0,delay.Ticks-clock.GetElapsedTime(cycleStarted).Ticks));
                if(delay==TimeSpan.Zero)await Task.Yield();
                await Task.Delay(delay,clock,stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        report("worker_stopped");
    }
    private static async Task DrainAsync(Task running)
    {
        // Allow cooperative I/O cleanup under load before DI disposal. Keep a
        // finite grace period inside the host's 15-second shutdown budget;
        // a dependency ignoring cancellation must still not block shutdown.
        try{await running.WaitAsync(TimeSpan.FromSeconds(2));}
        catch{}
    }
}
