using Microsoft.Extensions.Hosting;
namespace HostAgent.Service;
public sealed class AdminWorker(Func<CancellationToken,Task> serve,AgentRuntimeHealth health):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var failures=0;
        try
        {
            while(!token.IsCancellationRequested)
            {
                try{health.Set("administration",false);await serve(token);failures=0;}
                catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
                catch{health.Set("administration",true);failures=Math.Min(failures+1,6);}
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(30000,100*Math.Pow(2,failures))),token);
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
    }
}
