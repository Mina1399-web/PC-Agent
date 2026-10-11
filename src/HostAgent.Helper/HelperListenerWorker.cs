using Microsoft.Extensions.Hosting;
namespace HostAgent.Helper;
public sealed class HelperListenerWorker(Func<CancellationToken,Task> serve,Action<string> report,
    Action fatal,TimeSpan initialBackoff):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures=0;
        try
        {
            while(!stoppingToken.IsCancellationRequested)
            {
                try {await serve(stoppingToken);failures=0;}
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
                catch(Exception error) when(error is IOException or UnauthorizedAccessException)
                {
                    report("helper_listener_unavailable");
                    if(++failures>=5){fatal();return;}
                }
                // Malformed clients handled inside the protocol still cannot produce a tight accept loop.
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(10000,initialBackoff.TotalMilliseconds*Math.Pow(2,Math.Max(0,failures-1)))),stoppingToken);
            }
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){}
        catch {report("helper_listener_failed");fatal();}
        report("helper_worker_stopped");
    }
}
