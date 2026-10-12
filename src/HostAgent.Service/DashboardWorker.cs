using Microsoft.Extensions.Hosting;
namespace HostAgent.Service;

// Read-only view availability never changes remediation policy or stops monitoring.
public sealed class DashboardWorker(Func<CancellationToken,Task> serve):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        try
        {
            while(!token.IsCancellationRequested)
            {
                try {await serve(token);}
                catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
                catch {await Task.Delay(1000,token);}
                await Task.Delay(100,token);
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
    }
}
