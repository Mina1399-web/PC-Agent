using System.Collections.Concurrent;
using HostAgent.Core.Orchestration;
namespace HostAgent.Service;
public sealed record ComponentHealth(string Component, string Code, bool Degraded);
public sealed class AgentRuntimeHealth
{
    private readonly ConcurrentDictionary<string, ComponentHealth> _components = new(StringComparer.Ordinal);
    public bool Degraded => _components.Values.Any(value => value.Degraded);
    public IReadOnlyList<ComponentHealth> Snapshot() => _components.Values.OrderBy(value => value.Component, StringComparer.Ordinal).ToArray();

    public void Set(string component, bool degraded)
    {
        // Never expose arbitrary caller text, exception messages or payloads.
        component = component is "collection_worker" or "collectors" or "diagnostics" or "publication" or "administration"
            ? component : "runtime";
        _components[component] = new(component, degraded ? component + "_unavailable" : "healthy", degraded);
    }

    public void SetCollectors(IReadOnlyList<CollectorCycleResult> results)
    {
        var anyDegraded = false;
        foreach (var result in results)
        {
            var optionalSmartOnly = result.CollectorId == "disk" && result.Status == CollectorCycleStatus.Partial
                && result.WarningCodes.Count > 0 && result.WarningCodes.All(code => code == "smart_health_unavailable");
            var degraded = result.Status != CollectorCycleStatus.Succeeded && !optionalSmartOnly;
            anyDegraded |= degraded;
            var code = optionalSmartOnly ? "optional_smart_unavailable" : result.Status switch
            {
                CollectorCycleStatus.Succeeded => "collector_healthy",
                CollectorCycleStatus.Partial => "collector_partial",
                CollectorCycleStatus.Unavailable => "collector_unavailable",
                _ => result.FailureCode is "collector_timeout" or "collector_exception" or "backoff_active" or "collector_still_running"
                    ? result.FailureCode : "collector_failed"
            };
            if (result.CollectorId is "system" or "cpu-memory" or "disk" or "process-services" or "network" or "event-log")
                _components[result.CollectorId] = new(result.CollectorId, code, degraded);
        }
        Set("collectors", anyDegraded);
    }
}
