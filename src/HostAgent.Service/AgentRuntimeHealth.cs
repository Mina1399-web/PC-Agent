using System.Collections.Concurrent;
namespace HostAgent.Service;
public sealed class AgentRuntimeHealth
{
    private readonly ConcurrentDictionary<string,bool> _failures=new(StringComparer.Ordinal);
    public bool Degraded=>_failures.Values.Any(value=>value);
    public void Set(string component,bool degraded)=>_failures[component]=degraded;
}
