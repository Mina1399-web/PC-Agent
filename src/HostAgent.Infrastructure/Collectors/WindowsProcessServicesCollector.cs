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
    private readonly Dictionary<int, ProcessObservation> _previousProcesses = [];
    private readonly Dictionary<string, HostServiceStatus> _previousServices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<IWindowsProcessSample[]>? _readProcesses;
    private readonly Func<ServiceController[]> _readServices = ServiceController.GetServices;

    public WindowsProcessServicesCollector() { }

    internal WindowsProcessServicesCollector(Func<IWindowsProcessSample[]> readProcesses, Func<ServiceController[]>? readServices = null)
    {
        _readProcesses = readProcesses;
        _readServices = readServices ?? ServiceController.GetServices;
    }

    public string CollectorId => "process-services";

    public ValueTask<CollectorResult<ProcessServiceTelemetry>> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var warnings = new List<string>();
        var processes = ReadProcesses(now, warnings, cancellationToken);
        var services = ReadServices(now, warnings, cancellationToken, out var changes);
        var telemetry = new ProcessServiceTelemetry(processes, services, changes);

        return ValueTask.FromResult(warnings.Count == 0
            ? CollectorResult<ProcessServiceTelemetry>.Success(CollectorId, now, telemetry)
            : CollectorResult<ProcessServiceTelemetry>.Partial(CollectorId, now, telemetry, warnings));
    }

    private IReadOnlyList<ProcessTelemetry> ReadProcesses(
        DateTimeOffset now,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        IWindowsProcessSample[] runningProcesses;
        try
        {
            runningProcesses = _readProcesses is null ? WindowsProcessSample.ReadAll(cancellationToken) : _readProcesses();
        }
        catch (Win32Exception)
        {
            warnings.Add("process_list_unavailable");
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
                    stage=ProcessMetricReadStage.CpuTime;
                    double? cpuPercent = null;
                    var cpuRead = false;
                    try
                    {
                        var cpuTime = process.TotalProcessorTime;
                        stage=ProcessMetricReadStage.StartTime;
                        cpuPercent = UpdateProcessCpu(id, cpuTime, process.CpuCapturedAtUtc ?? now, process.CpuGeneration);
                        cpuRead = true;
                    }
                    catch (Exception exception) when (
                        exception is Win32Exception or InvalidOperationException or NotSupportedException)
                    {
                        if (process.HasExited) continue;
                        var warning = ProcessMetricFailure.Classify(exception, id);
                        if (warning is null) continue; // exact known Idle limitation only
                        warnings.Add(warning);
                        if (ProcessMetricFailure.ClassifyStage(exception, id, stage) is { } detail)
                            warnings.Add(detail);
                    }
                    DateTimeOffset? startedAt = null;
                    if (process.CounterCpuRecovered)
                    {
                        warnings.Add("process_identity_unavailable");
                    }
                    else try
                    {
                        startedAt = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
                    }
                    catch (Exception exception) when (
                        exception is Win32Exception or InvalidOperationException or NotSupportedException)
                    {
                        if (process.HasExited) continue;
                        warnings.Add("some_process_start_times_unavailable");
                        if(ProcessMetricFailure.ClassifyStage(exception,knownProcessId,ProcessMetricReadStage.StartTime) is { } detail)
                            warnings.Add(detail);
                    }
                    stage=ProcessMetricReadStage.Name;
                    var name=process.ProcessName;
                    var path=TryGetExecutablePath(process,warnings);
                    stage=ProcessMetricReadStage.WorkingSet;
                    var memoryBytes=Math.Max(0,process.WorkingSet64);
                    result.Add(new ProcessTelemetry(
                        id,
                        name,
                        path,
                        cpuPercent,
                        memoryBytes,
                        startedAt));
                    if (cpuRead) seen.Add(id);
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    if (process.HasExited) continue;
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

        lock (_sync)
        {
            foreach (var staleProcessId in _previousProcesses.Keys.Where(id => !seen.Contains(id)).ToArray())
            {
                _previousProcesses.Remove(staleProcessId);
            }
        }

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
            controllers = _readServices();
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            warnings.Add("service_catalog_unavailable");
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

    private double? UpdateProcessCpu(int processId, TimeSpan cpuTime, DateTimeOffset now, ProcessCpuGeneration generation)
    {
        lock (_sync)
        {
            double? cpuPercent = null;
            if (_previousProcesses.TryGetValue(processId, out var previous) && previous.Generation == generation)
            {
                var elapsed = now - previous.CapturedAtUtc;
                var cpuDelta = cpuTime - previous.CpuTime;
                if (elapsed > TimeSpan.Zero && cpuDelta >= TimeSpan.Zero)
                {
                    cpuPercent = Math.Clamp(
                        100d * cpuDelta.TotalSeconds / (elapsed.TotalSeconds * Math.Max(1, Environment.ProcessorCount)),
                        0,
                        100);
                }
            }

            _previousProcesses[processId] = new ProcessObservation(cpuTime, now, generation);
            return cpuPercent;
        }
    }

    private static string? TryGetExecutablePath(IWindowsProcessSample process, ICollection<string> warnings)
    {
        try
        {
            var path = process.ExecutablePath;
            if (path is null && process.CounterCpuRecovered)
                warnings.Add("some_executable_paths_unavailable");
            return path;
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

    private sealed record ProcessObservation(TimeSpan CpuTime, DateTimeOffset CapturedAtUtc, ProcessCpuGeneration Generation);
}
