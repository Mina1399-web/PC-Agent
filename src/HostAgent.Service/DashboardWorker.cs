using Microsoft.Extensions.Hosting;
using System.IO;
namespace HostAgent.Service;

// Read-only view availability never changes remediation policy or stops monitoring.
public sealed class DashboardWorker(Func<CancellationToken,Task> serve,
    Action<PipeWorkerDiagnostic>? reportDiagnostic=null,TimeSpan? retryDelay=null):BackgroundService
{
    private TimeSpan RetryDelay=>retryDelay??TimeSpan.FromSeconds(1);
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        try
        {
            while(!token.IsCancellationRequested)
            {
                try {await serve(token);}
                catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
                catch(Exception error)
                {
                    try{reportDiagnostic?.Invoke(new("serve",FailureKind(error),Math.Max(0,(long)RetryDelay.TotalMilliseconds)));}catch{}
                    await Task.Delay(RetryDelay,token);
                }
                await Task.Delay(100,token);
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
    }
    private static string FailureKind(Exception error)=>error switch
    {
        OperationCanceledException=>"cancelled",
        IOException=>"io",
        UnauthorizedAccessException=>"unauthorized",
        InvalidDataException=>"invalid_frame",
        _=>"unexpected"
    };
}
public sealed record PipeWorkerDiagnostic(string Stage,string Kind,long RetryDelayMilliseconds);
