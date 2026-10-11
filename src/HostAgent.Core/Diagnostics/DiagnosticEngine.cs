using System.Globalization;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Baseline;
using HostAgent.Core.Models.Diagnosis;
using HostAgent.Core.Models.Incidents;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Core.Diagnostics;

public sealed class DiagnosticEngine : IDiagnosticEngine
{
    private static readonly TimeSpan CpuWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EventCorrelationWindow = TimeSpan.FromHours(24);
    private const double SustainedCpuThresholdPercent = 85;
    private const double MemoryPressureThresholdPercent = 95;
    private const double DiskHighThresholdPercent = 95;
    private const double DiskCriticalThresholdPercent = 98;
    private const long CriticallyLowDiskBytes = 1024L * 1024 * 1024;

    public ValueTask<IReadOnlyList<Diagnosis>> EvaluateAsync(
        HostEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(evidence.Baselines);
        ArgumentNullException.ThrowIfNull(evidence.RecentMetrics);
        cancellationToken.ThrowIfCancellationRequested();

        var diagnoses = new List<Diagnosis>();
        AddCpuDiagnosis(evidence, diagnoses);
        AddMemoryDiagnosis(evidence, diagnoses);
        AddDiskDiagnoses(evidence, diagnoses);
        AddNetworkDiagnoses(evidence, diagnoses);
        AddServiceDiagnoses(evidence, diagnoses);
        AddAdaptiveDiagnoses(evidence, diagnoses);
        return ValueTask.FromResult<IReadOnlyList<Diagnosis>>(diagnoses.AsReadOnly());
    }

    private static void AddCpuDiagnosis(HostEvidence evidence, ICollection<Diagnosis> diagnoses)
    {
        var samples = evidence.RecentMetrics
            .Where(sample => sample.MetricName == "cpu.usage.percent"
                && sample.Scope == "host"
                && sample.SampledAtUtc <= evidence.SampledAtUtc
                && sample.SampledAtUtc >= evidence.SampledAtUtc - CpuWindow)
            .OrderBy(static sample => sample.SampledAtUtc)
            .ToArray();
        var sustained = GetSustainedCpuSamples(samples);
        if (sustained.Count < 5
            || sustained[^1].SampledAtUtc - sustained[0].SampledAtUtc < TimeSpan.FromMinutes(4))
        {
            return;
        }

        var topProcesses = evidence.CpuMemory?.TopProcessesByCpu
            .Where(static process => process.CpuUsagePercent is >= 0 && double.IsFinite(process.CpuUsagePercent.Value))
            .OrderByDescending(static process => process.CpuUsagePercent)
            .ToArray() ?? [];
        var totalProcessCpu = topProcesses.Sum(static process => process.CpuUsagePercent!.Value);
        var dominant = topProcesses.FirstOrDefault() is { } top
            && top.CpuUsagePercent >= 50
            && totalProcessCpu > 0
            && top.CpuUsagePercent >= totalProcessCpu * 0.6
                ? top
                : null;
        var evidenceItems = sustained
            .Select(sample => Evidence(
                "cpu.usage.percent",
                $"CPU usage was {sample.Value.ToString("F1", CultureInfo.InvariantCulture)}%.",
                sample.Value,
                sample.SampledAtUtc))
            .ToList();
        if (dominant is not null)
        {
            evidenceItems.Add(Evidence(
                "process.cpu",
                $"Process {dominant.ProcessName} (PID {dominant.ProcessId}) used {dominant.CpuUsagePercent!.Value.ToString("F1", CultureInfo.InvariantCulture)}% CPU.",
                dominant.CpuUsagePercent.Value,
                evidence.SampledAtUtc));
        }

        var confidence = Math.Min(100, 60 + (dominant is null ? 0 : 25) + Math.Min(15, (sustained.Count - 5) * 3));
        diagnoses.Add(CreateDiagnosis(
            "cpu_sustained:" + (dominant?.ProcessId.ToString(CultureInfo.InvariantCulture) ?? "host"),
            evidence.SampledAtUtc,
            DiagnosisSeverity.High,
            confidence,
            evidenceItems,
            dominant is null
                ? new ProbableRootCause("cpu_host_saturation", "Sustained host CPU saturation", null)
                : new ProbableRootCause(
                    "cpu_process_saturation",
                    "A single process dominates sustained CPU load",
                    $"{dominant.ProcessName} (PID {dominant.ProcessId})"),
            [],
            new RemediationRecommendation("inspect_cpu_process", "Review the dominant process and confirm that its workload is expected.")));
    }

    private static IReadOnlyList<MetricSample> GetSustainedCpuSamples(IReadOnlyList<MetricSample> samples)
    {
        var suffix = new List<MetricSample>();
        for (var index = samples.Count - 1; index >= 0; index--)
        {
            var sample = samples[index];
            if (sample.Value < SustainedCpuThresholdPercent
                || (!suffix.Count.Equals(0)
                    && suffix[^1].SampledAtUtc - sample.SampledAtUtc > TimeSpan.FromSeconds(90)))
            {
                break;
            }

            suffix.Add(sample);
        }

        suffix.Reverse();
        return suffix;
    }

    private static void AddMemoryDiagnosis(HostEvidence evidence, ICollection<Diagnosis> diagnoses)
    {
        var cpuMemory = evidence.CpuMemory;
        if (cpuMemory is null || cpuMemory.TotalPhysicalMemoryBytes <= 0)
        {
            return;
        }

        var pressure = cpuMemory.MemoryPressurePercent;
        if (pressure < MemoryPressureThresholdPercent)
        {
            return;
        }

        var severity = pressure >= 99 ? DiagnosisSeverity.Critical : DiagnosisSeverity.High;
        diagnoses.Add(CreateDiagnosis(
            "memory_pressure:host",
            evidence.SampledAtUtc,
            severity,
            pressure >= 99 ? 96 : 90,
            [Evidence(
                "memory.pressure",
                $"Physical memory pressure is {pressure.ToString("F1", CultureInfo.InvariantCulture)}%.",
                pressure,
                evidence.SampledAtUtc)],
            new ProbableRootCause("memory_pressure", "Available physical memory is critically low", null),
            ["low_available_memory"],
            new RemediationRecommendation("inspect_memory_consumers", "Review top memory consumers and current workload.")));
    }

    private static void AddDiskDiagnoses(HostEvidence evidence, ICollection<Diagnosis> diagnoses)
    {
        if (evidence.Disk is null)
        {
            return;
        }

        foreach (var volume in evidence.Disk.Volumes
                     .Where(volume => volume.UsedPercent >= DiskHighThresholdPercent
                         || volume.FreeBytes < CriticallyLowDiskBytes)
                     .OrderByDescending(static volume => volume.UsedPercent))
        {
            var relatedEvents = evidence.EventLog?.Events
                .Where(entry => IsRelatedDiskError(entry, evidence.SampledAtUtc))
                .ToArray() ?? [];
            var secondary = new List<string>();
            foreach (var entry in relatedEvents)
            {
                if (IsWindowsUpdateProvider(entry.Provider))
                {
                    secondary.Add("windows_update_errors");
                }

                if (IsPagefileProvider(entry.Provider))
                {
                    secondary.Add("pagefile_errors");
                }
            }

            var evidenceItems = new List<DiagnosticEvidence>
            {
                Evidence(
                    "disk.capacity",
                    $"Volume {volume.RootPath} is {volume.UsedPercent.ToString("F1", CultureInfo.InvariantCulture)}% full with {volume.FreeBytes} bytes free.",
                    volume.UsedPercent,
                    evidence.SampledAtUtc)
            };
            evidenceItems.AddRange(relatedEvents.Select(entry => Evidence(
                "eventlog." + entry.Provider,
                $"{entry.LogName} event {entry.EventId} from {entry.Provider} has level {entry.Level}.",
                null,
                entry.OccurredAtUtc ?? evidence.SampledAtUtc)));

            var critical = volume.UsedPercent >= DiskCriticalThresholdPercent
                || volume.FreeBytes < CriticallyLowDiskBytes;
            diagnoses.Add(CreateDiagnosis(
                "disk_capacity:" + volume.RootPath.TrimEnd('\\').ToUpperInvariant(),
                evidence.SampledAtUtc,
                critical ? DiagnosisSeverity.Critical : DiagnosisSeverity.High,
                relatedEvents.Length == 0 ? 82 : 94,
                evidenceItems,
                new ProbableRootCause(
                    "disk_capacity",
                    "Insufficient free space on a host volume",
                    volume.RootPath),
                secondary.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                new RemediationRecommendation("review_disk_capacity", "Review disk usage and identify approved cleanup options.")));
        }
    }

    private static bool IsRelatedDiskError(EventLogEntryMetadata entry, DateTimeOffset sampledAtUtc)
    {
        var isError = entry.Level.Equals("Error", StringComparison.OrdinalIgnoreCase)
            || entry.Level.Equals("Critical", StringComparison.OrdinalIgnoreCase);
        var recent = entry.OccurredAtUtc is null
            || (entry.OccurredAtUtc <= sampledAtUtc
                && sampledAtUtc - entry.OccurredAtUtc <= EventCorrelationWindow);
        return isError && recent && (IsWindowsUpdateProvider(entry.Provider) || IsPagefileProvider(entry.Provider));
    }

    private static bool IsWindowsUpdateProvider(string provider) =>
        provider.Contains("WindowsUpdateClient", StringComparison.OrdinalIgnoreCase);

    private static bool IsPagefileProvider(string provider) =>
        provider.Contains("volmgr", StringComparison.OrdinalIgnoreCase)
        || provider.Contains("pagefile", StringComparison.OrdinalIgnoreCase)
        || provider.Contains("paging", StringComparison.OrdinalIgnoreCase);

    private static void AddNetworkDiagnoses(HostEvidence evidence, ICollection<Diagnosis> diagnoses)
    {
        var probes = evidence.Network?.Probes;
        if (probes is null || probes.Count == 0)
        {
            return;
        }

        var gatewayFailed = probes.Any(static probe => probe.Kind == NetworkProbeKind.Gateway
            && probe.Status is NetworkProbeStatus.Failed or NetworkProbeStatus.TimedOut);
        var gatewaySucceeded = probes.Any(static probe => probe.Kind == NetworkProbeKind.Gateway
            && probe.Status == NetworkProbeStatus.Succeeded);
        var ipFailed = probes.Any(static probe => probe.Kind == NetworkProbeKind.InternetIp
            && probe.Status is NetworkProbeStatus.Failed or NetworkProbeStatus.TimedOut);
        var ipSucceeded = probes.Any(static probe => probe.Kind == NetworkProbeKind.InternetIp
            && probe.Status == NetworkProbeStatus.Succeeded);
        var dnsFailed = probes.Any(static probe => probe.Kind == NetworkProbeKind.DnsResolution
            && probe.Status is NetworkProbeStatus.Failed or NetworkProbeStatus.TimedOut);

        if (gatewayFailed)
        {
            diagnoses.Add(CreateNetworkDiagnosis(
                evidence,
                probes,
                "gateway_failure",
                "The configured gateway did not respond to the reachability probe",
                "check_gateway",
                90,
                dnsFailed
                    ? ["internet_unreachable", "dns_resolution_failed"]
                    : ["internet_unreachable"]));
        }
        else if (gatewaySucceeded && ipSucceeded && dnsFailed)
        {
            diagnoses.Add(CreateNetworkDiagnosis(
                evidence,
                probes,
                "dns_failure",
                "IP connectivity works while DNS resolution fails",
                "check_dns_resolution",
                90,
                ["name_resolution_unavailable"]));
        }
        else if (gatewaySucceeded && ipFailed)
        {
            diagnoses.Add(CreateNetworkDiagnosis(
                evidence,
                probes,
                "internet_connectivity_failure",
                "The gateway responds but external IP connectivity fails",
                "check_internet_connectivity",
                80,
                dnsFailed ? ["dns_resolution_failed"] : []));
        }
    }

    private static Diagnosis CreateNetworkDiagnosis(
        HostEvidence hostEvidence,
        IReadOnlyList<NetworkProbeResult> probes,
        string causeCode,
        string summary,
        string recommendationCode,
        int confidence,
        IReadOnlyList<string> secondarySymptoms)
    {
        var evidence = probes
            .Where(static probe => probe.Kind is NetworkProbeKind.Gateway or NetworkProbeKind.InternetIp or NetworkProbeKind.DnsResolution)
            .Select(probe => Evidence(
                "network." + probe.Kind,
                $"{probe.Kind} probe to {probe.Target} {probe.Status}.",
                probe.LatencyMilliseconds,
                hostEvidence.SampledAtUtc))
            .ToArray();
        return CreateDiagnosis(
            "network:" + causeCode,
            hostEvidence.SampledAtUtc,
            DiagnosisSeverity.High,
            confidence,
            evidence,
            new ProbableRootCause(causeCode, summary, null),
            secondarySymptoms,
            new RemediationRecommendation(recommendationCode, "Review adapter configuration and repeat a bounded connectivity check."));
    }

    private static void AddServiceDiagnoses(HostEvidence evidence, ICollection<Diagnosis> diagnoses)
    {
        if (evidence.ProcessServices is not { } services)
        {
            return;
        }

        var failedChanges = services.ServiceChanges
            .Where(change => change.PreviousStatus == HostServiceStatus.Running
                && change.CurrentStatus == HostServiceStatus.Stopped
                && change.ObservedAtUtc <= evidence.SampledAtUtc
                && evidence.SampledAtUtc - change.ObservedAtUtc <= EventCorrelationWindow)
            .GroupBy(static change => change.ServiceName, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderByDescending(change => change.ObservedAtUtc).First());

        foreach (var change in failedChanges)
        {
            var service = services.Services.FirstOrDefault(item =>
                item.ServiceName.Equals(change.ServiceName, StringComparison.OrdinalIgnoreCase));
            if (service is not null && service.Status != HostServiceStatus.Stopped)
            {
                continue;
            }

            diagnoses.Add(CreateDiagnosis(
                "service_failure:" + change.ServiceName.ToUpperInvariant(),
                evidence.SampledAtUtc,
                DiagnosisSeverity.High,
                88,
                [Evidence(
                    "service.state_change",
                    $"Service {change.ServiceName} changed from {change.PreviousStatus} to {change.CurrentStatus}.",
                    null,
                    change.ObservedAtUtc)],
                new ProbableRootCause("service_stopped", "A previously running service stopped unexpectedly", change.ServiceName),
                [],
                new RemediationRecommendation("inspect_service_failure", "Review the service failure and its recent Event Log metadata.")));
        }

        var crashedEvents = evidence.EventLog?.Events
            .Where(entry => entry.Provider.Contains("Service Control Manager", StringComparison.OrdinalIgnoreCase)
                && entry.EventId is 7031 or 7034
                && (entry.Level.Equals("Error", StringComparison.OrdinalIgnoreCase)
                    || entry.Level.Equals("Critical", StringComparison.OrdinalIgnoreCase)))
            .ToArray() ?? [];
        foreach (var crash in crashedEvents)
        {
            if (failedChanges.Any(change => change.ObservedAtUtc == crash.OccurredAtUtc))
            {
                continue;
            }

            diagnoses.Add(CreateDiagnosis(
                "service_crash:event:" + crash.RecordId?.ToString(CultureInfo.InvariantCulture),
                evidence.SampledAtUtc,
                DiagnosisSeverity.High,
                78,
                [Evidence("eventlog.service-crash", $"Service Control Manager event {crash.EventId} was recorded.", null, crash.OccurredAtUtc ?? evidence.SampledAtUtc)],
                new ProbableRootCause("service_crash", "Windows recorded an unexpected service termination", null),
                [],
                new RemediationRecommendation("inspect_service_failure", "Review the affected service and its recovery settings.")));
        }
    }

    private static void AddAdaptiveDiagnoses(HostEvidence evidence, ICollection<Diagnosis> diagnoses)
    {
        foreach (var baseline in evidence.Baselines.Where(static assessment =>
                     assessment.AdaptiveSignal is AdaptiveSignal.Anomaly or AdaptiveSignal.Informational))
        {
            if (baseline.MetricName.StartsWith("cpu.usage", StringComparison.OrdinalIgnoreCase)
                || baseline.MetricName.StartsWith("memory.used", StringComparison.OrdinalIgnoreCase)
                || baseline.MetricName.StartsWith("disk.used", StringComparison.OrdinalIgnoreCase)
                || baseline.MetricName.StartsWith("network.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var informational = baseline.AdaptiveSignal == AdaptiveSignal.Informational;
            diagnoses.Add(CreateDiagnosis(
                $"adaptive:{baseline.MetricName}:{baseline.Scope}",
                evidence.SampledAtUtc,
                informational ? DiagnosisSeverity.Information : DiagnosisSeverity.Warning,
                Math.Clamp(baseline.Confidence, 0, 100),
                [Evidence(
                    "baseline." + baseline.MetricName,
                    $"{baseline.MetricName} for {baseline.Scope} deviated from its robust baseline.",
                    baseline.CurrentValue,
                    baseline.SampledAtUtc)],
                new ProbableRootCause(
                    "adaptive_metric_deviation",
                    "A metric differs from its recent robust baseline",
                    baseline.MetricName + ":" + baseline.Scope),
                [],
                new RemediationRecommendation("review_metric_trend", "Review the metric trend and related host telemetry.")));
        }
    }

    private static Diagnosis CreateDiagnosis(
        string correlationKey,
        DateTimeOffset observedAtUtc,
        DiagnosisSeverity severity,
        int confidence,
        IReadOnlyList<DiagnosticEvidence> evidence,
        ProbableRootCause rootCause,
        IReadOnlyList<string> secondarySymptoms,
        RemediationRecommendation remediation) =>
        new(
            Guid.NewGuid().ToString("N"),
            correlationKey,
            observedAtUtc.ToUniversalTime(),
            severity,
            Math.Clamp(confidence, 0, 100),
            evidence,
            rootCause,
            secondarySymptoms,
            remediation,
            IncidentLifecycleState.Detected);

    private static DiagnosticEvidence Evidence(
        string source,
        string observation,
        double? value,
        DateTimeOffset observedAtUtc) =>
        new(Guid.NewGuid().ToString("N"), source, observation, value, observedAtUtc.ToUniversalTime());
}
