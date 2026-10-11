namespace HostAgent.Cli;
public sealed class ConfigurationWorkflow(Func<string[],bool> confirm,Func<CancellationToken,Task> stop,
    Action<string[]> apply,Func<CancellationToken,Task> start)
{
    public async Task<bool> RunAsync(string[] args,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!confirm(args))return false;
        try{await stop(token);token.ThrowIfCancellationRequested();apply(args);}
        finally{await start(CancellationToken.None);}
        return true;
    }
}
