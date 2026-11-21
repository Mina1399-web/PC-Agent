using System.ComponentModel;
using System.Diagnostics;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Telemetry;
using System.ServiceProcess;

namespace HostAgent.Infrastructure.Collectors;

public sealed class WindowsProcessServicesCollector : ITelemetryCollector<ProcessServiceTelemetry>
{
    public const int MaximumProcesses = 4096;
    private readonly object _sync = new();
    private readonly ProcessCpuTracker _processCpu = new();
    private readonly Dictionary<string, HostServiceStatus> _previousServices = new(StringComparer.OrdinalIgnoreCase);

    public string CollectorId => "process-services";

    public ValueTask<CollectorResult<ProcessServiceTelemetry>> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var warnings = new List<string>();
        var processes = ReadProcesses(warnings, cancellationToken);
        var services = ReadServices(now, warnings, cancellationToken, out var changes);
        var telemetry = new ProcessServiceTelemetry(processes, services, changes);

        return ValueTask.FromResult(warnings.Count == 0
            ? CollectorResult<ProcessServiceTelemetry>.Success(CollectorId, now, telemetry)
            : CollectorResult<ProcessServiceTelemetry>.Partial(CollectorId, now, telemetry, warnings));
    }

    private IReadOnlyList<ProcessTelemetry> ReadProcesses(
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        Process[] runningProcesses;
        try
        {
            runningProcesses = Process.GetProcesses();
        }
        catch (Win32Exception error)
        {
            _processCpu.Retain([]);
            warnings.Add(error.NativeErrorCode == 5 ? "process_list_access_denied" : "process_list_unavailable");
            return Array.Empty<ProcessTelemetry>();
        }

        var result = new List<ProcessTelemetry>(Math.Min(runningProcesses.Length, MaximumProcesses));
        var seen = new HashSet<int>();
        for (var index = 0; index < runningProcesses.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var process = runningProcesses[index];
            using (process)
            {
                if (index >= MaximumProcesses)
                {
                    continue;
                }

                int? knownProcessId = null;
                var stage=ProcessMetricReadStage.Identity;
                try
                {
                    var id = process.Id;
                    knownProcessId = id;
                    if (id <= 0) continue;
                    stage=ProcessMetricReadStage.Name;
                    var name = process.ProcessName;
                    var sample = WindowsProcessCpuSample.Read(id, warnings);
                    var now = DateTimeOffset.UtcNow;
                    var cpuPercent = _processCpu.Record(id, sample, now, Environment.ProcessorCount);
                    if (sample.Exited) continue;
                    seen.Add(id);
                    var memory = ProcessMetricFailure.WorkingSet(process, id, warnings);
                    if (memory is null && cpuPercent is null) continue;
                    var path = TryGetExecutablePath(process,warnings);
                    var verified = sample.StartedAt is { } startedAt && startedAt <= now;
                    result.Add(new(id, name, path, cpuPercent, memory, verified ? sample.StartedAt : null)
                    {
                        CpuIdentityVerified = verified
                    });
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    if (ProcessMetricFailure.Classify(exception, knownProcessId) is { } warning)
                        warnings.Add(warning);
                    if (ProcessMetricFailure.ClassifyStage(exception,knownProcessId,stage) is { } detail)
                        warnings.Add(detail);
                }
            }
        }

        if (runningProcesses.Length > MaximumProcesses)
        {
            warnings.Add("process_list_truncated");
        }

        _processCpu.Retain(seen);

        return result.AsReadOnly();
    }

    private IReadOnlyList<ServiceTelemetry> ReadServices(
        DateTimeOffset now,
        ICollection<string> warnings,
        CancellationToken cancellationToken,
        out IReadOnlyList<ServiceStateChange> changes)
    {
        ServiceController[] controllers;
        try
        {
            controllers = ServiceController.GetServices();
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            warnings.Add(exception is UnauthorizedAccessException or Win32Exception { NativeErrorCode: 5 }
                ? "service_catalog_access_denied" : "service_catalog_unavailable");
            changes = Array.Empty<ServiceStateChange>();
            return Array.Empty<ServiceTelemetry>();
        }

        var services = new List<ServiceTelemetry>(controllers.Length);
        var stateChanges = new List<ServiceStateChange>();
        var currentStates = new Dictionary<string, HostServiceStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var controller in controllers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (controller)
            {
                try
                {
                    var status = MapStatus(controller.Status);
                    var startMode = HostServiceStartMode.Unknown;
                    try
                    {
                        startMode = MapStartMode(controller.StartType);
                    }
                    catch (Exception exception) when (
                        exception is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
                    {
                        warnings.Add("some_service_start_modes_unavailable");
                    }

                    var service = new ServiceTelemetry(
                        controller.ServiceName,
                        controller.DisplayName,
                        status,
                        startMode,
                        ReadTriggers(controller.ServiceName, warnings));
                    services.Add(service);
                    currentStates[service.ServiceName] = status;
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException)
                {
                    warnings.Add("some_service_states_unavailable");
                }
            }
        }

        lock (_sync)
        {
            foreach (var (serviceName, currentStatus) in currentStates)
            {
                if (_previousServices.TryGetValue(serviceName, out var previousStatus)
                    && previousStatus != currentStatus)
                {
                    stateChanges.Add(new ServiceStateChange(
                        serviceName,
                        previousStatus,
                        currentStatus,
                        now));
                }
            }

            _previousServices.Clear();
            foreach (var (serviceName, status) in currentStates)
            {
                _previousServices.Add(serviceName, status);
            }
        }

        changes = stateChanges.AsReadOnly();
        return services.AsReadOnly();
    }

    private static string? TryGetExecutablePath(Process process, ICollection<string> warnings)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            warnings.Add("some_executable_paths_unavailable");
            return null;
        }
    }

    private static HostServiceStatus MapStatus(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Stopped => HostServiceStatus.Stopped,
        ServiceControllerStatus.StartPending => HostServiceStatus.StartPending,
        ServiceControllerStatus.StopPending => HostServiceStatus.StopPending,
        ServiceControllerStatus.Running => HostServiceStatus.Running,
        ServiceControllerStatus.ContinuePending => HostServiceStatus.ContinuePending,
        ServiceControllerStatus.PausePending => HostServiceStatus.PausePending,
        ServiceControllerStatus.Paused => HostServiceStatus.Paused,
        _ => HostServiceStatus.Unknown
    };

    private static HostServiceStartMode MapStartMode(ServiceStartMode startMode) => startMode switch
    {
        ServiceStartMode.Automatic => HostServiceStartMode.Automatic,
        ServiceStartMode.Manual => HostServiceStartMode.Manual,
        ServiceStartMode.Disabled => HostServiceStartMode.Disabled,
        ServiceStartMode.Boot => HostServiceStartMode.Boot,
        ServiceStartMode.System => HostServiceStartMode.System,
        _ => HostServiceStartMode.Unknown
    };

    private static bool? ReadTriggers(string serviceName, ICollection<string> warnings)
    {
        var value = ServiceTriggerQuery.HasTriggers(serviceName);
        if (value is null) warnings.Add("some_service_triggers_unavailable");
        return value;
    }

}
