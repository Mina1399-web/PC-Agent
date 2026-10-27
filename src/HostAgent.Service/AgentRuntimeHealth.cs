using System.Collections.Concurrent;
using HostAgent.Core.Orchestration;
namespace HostAgent.Service;
public sealed record ComponentHealth(string Component, string Code, bool Degraded, IReadOnlyList<string> Reasons);
public sealed class AgentRuntimeHealth
{
    private static readonly HashSet<string> AllowedWarnings = new(StringComparer.Ordinal)
    {
        "volume_not_ready", "volume_metrics_unavailable", "disk_io_counters_unavailable", "some_disk_io_counters_unavailable",
        "smart_health_unavailable", "system_cpu_counter_unavailable", "cpu_baseline_not_ready", "process_list_unavailable",
        "some_process_metrics_unavailable", "some_process_metrics_access_denied", "some_process_start_times_unavailable", "process_list_truncated",
        "process_identity_access_denied", "process_name_access_denied", "process_cpu_time_access_denied",
        "process_working_set_access_denied", "process_start_time_access_denied",
        "process_identity_unavailable",
        "service_catalog_unavailable", "some_service_states_unavailable", "some_executable_paths_unavailable",
        "some_service_start_modes_unavailable", "some_service_triggers_unavailable",
        "some_network_adapter_details_unavailable", "no_network_adapters_available", "windows_version_registry_unavailable",
        "shutdown_history_unavailable"
    };
    private readonly ConcurrentDictionary<string, ComponentHealth> _components = new(StringComparer.Ordinal);
    public bool Degraded => _components.Values.Any(value => value.Degraded);
    public IReadOnlyList<ComponentHealth> Snapshot() => _components.Values.OrderBy(value => value.Component, StringComparer.Ordinal).ToArray();

    public void Set(string component, bool degraded)
    {
        // Never expose arbitrary caller text, exception messages or payloads.
        component = component is "collection_worker" or "collectors" or "diagnostics" or "publication" or "administration"
            ? component : "runtime";
        var code = degraded ? component + "_unavailable" : "healthy";
        _components[component] = new(component, code, degraded, degraded ? [code] : []);
    }

    public void SetCollectors(IReadOnlyList<CollectorCycleResult> results)
    {
        var anyDegraded = results.Count == 0;
        var anyTelemetryAvailable = false;
        foreach (var result in results)
        {
            var optionalSmartOnly = result.CollectorId == "disk" && result.Status == CollectorCycleStatus.Partial
                && result.WarningCodes.Count > 0 && result.WarningCodes.All(code => code == "smart_health_unavailable");
            // Executable paths are explicitly best-effort in the specification. Keep
            // the capability warning visible, without hiding other process/service faults.
            var optionalPathsOnly = result.CollectorId == "process-services" && result.Status == CollectorCycleStatus.Partial
                && result.WarningCodes.Count > 0 && result.WarningCodes.All(code => code == "some_executable_paths_unavailable");
            // Emitted only after actual CPU/memory recovery from a coherent
            // performance row. Exact UTC identity stays null and actions blocked.
            var optionalIdentityOnly = result.CollectorId == "process-services" && result.Status == CollectorCycleStatus.Partial
                && result.WarningCodes.Contains("process_identity_unavailable")
                && result.WarningCodes.All(code => code is "process_identity_unavailable" or "some_executable_paths_unavailable");
            var degraded = result.Status != CollectorCycleStatus.Succeeded && !optionalSmartOnly && !optionalPathsOnly && !optionalIdentityOnly;
            anyDegraded |= degraded;
            anyTelemetryAvailable |= result.Status is CollectorCycleStatus.Succeeded or CollectorCycleStatus.Partial;
            var code = optionalSmartOnly ? "optional_smart_unavailable"
                : optionalPathsOnly ? "optional_process_paths_unavailable"
                : optionalIdentityOnly ? "optional_process_identity_unavailable" : result.Status switch
            {
                CollectorCycleStatus.Succeeded => "collector_healthy",
                CollectorCycleStatus.Partial => "collector_partial",
                CollectorCycleStatus.Unavailable => "collector_unavailable",
                _ => result.FailureCode is "collector_timeout" or "collector_exception" or "backoff_active" or "collector_still_running"
                    ? result.FailureCode : "collector_failed"
            };
            if (result.CollectorId is "system" or "cpu-memory" or "disk" or "process-services" or "network" or "event-log")
            {
                var reasons = result.WarningCodes.Where(AllowedWarnings.Contains).Distinct(StringComparer.Ordinal).Take(8).ToArray();
                _components[result.CollectorId] = new(result.CollectorId, code, degraded, reasons);
            }
        }

        var aggregateCode = !anyDegraded
            ? "healthy"
            : anyTelemetryAvailable ? "collectors_partial" : "collectors_unavailable";
        _components["collectors"] = new("collectors", aggregateCode, anyDegraded,
            anyDegraded ? [aggregateCode] : []);
    }
}
