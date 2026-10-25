using Microsoft.Extensions.Hosting;
using System.Runtime.ExceptionServices;

namespace HostAgent.Infrastructure.Tests;

internal sealed class TestHostLifetime(IHost host)
{
    public bool StopCompleted { get; private set; }

    public async Task RunAsync(Func<Task> assertions)
    {
        ExceptionDispatchInfo? primary = null;
        var cleanupFailures = new List<Exception>();
        try
        {
            await host.StartAsync();
            await assertions();
        }
        catch (Exception error) { primary = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await host.StopAsync(deadline.Token).WaitAsync(deadline.Token);
                StopCompleted = true;
            }
            catch (Exception error) { cleanupFailures.Add(error); }
            try { host.Dispose(); }
            catch (Exception error) { cleanupFailures.Add(error); }
        }

        if (cleanupFailures.Count > 0)
        {
            if (primary is not null) cleanupFailures.Insert(0, primary.SourceException);
            if (cleanupFailures.Count > 1) throw new AggregateException(cleanupFailures);
            ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
        }
        primary?.Throw();
    }
}
