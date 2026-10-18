using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace HostAgent.Desktop.Tests;

internal static class StaTest
{
    public static async Task RunAsync(Action action, TimeSpan? timeout = null)
    {
        var completion = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Exception? failure = null;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                action();
            }
            catch (Exception error) { failure = error; }
            finally
            {
                try { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
                catch (Exception error)
                {
                    failure = failure is null ? error : new AggregateException(failure, error);
                }
                // A passing body is insufficient: closing its windows and shutting
                // down the dispatcher are part of the same test lifetime.
                completion.SetResult(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        var failure = await completion.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(30));
        if (!thread.Join(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The WPF test thread did not terminate after dispatcher shutdown.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
