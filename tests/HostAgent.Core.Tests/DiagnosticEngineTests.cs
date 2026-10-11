using System.Collections;
using System.Reflection;
using HostAgent.Core.Models.Telemetry;
using Xunit;

namespace HostAgent.Core.Tests;

public sealed class DiagnosticEngineTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Isolated_cpu_spike_is_ignored_but_sustained_load_identifies_dominant_process()
    {
        var spikeEvidence = CreateEvidence(
            cpuMemory: CreateCpu(96, new ProcessResourceUsage(17, "Updater", 90, 500_000_000, null)),
            recentMetrics: [CpuSample(Now, 96)]);
        Assert.Empty(await EvaluateAsync(spikeEvidence));

        var sustainedMetrics = Enumerable.Range(0, 5)
            .Select(index => CpuSample(Now.AddMinutes(index - 4), 95))
            .ToArray();
        var sustainedEvidence = CreateEvidence(
            cpuMemory: CreateCpu(97,
                new ProcessResourceUsage(17, "Indexer", 83, 500_000_000, null),
                new ProcessResourceUsage(18, "Browser", 12, 700_000_000, null),
                new ProcessResourceUsage(19, "Service", 5, 100_000_000, null)),
            recentMetrics: sustainedMetrics);

        var diagnosis = Assert.Single(await EvaluateAsync(sustainedEvidence));
        Assert.Equal("High", ReadProperty(diagnosis, "Severity")?.ToString());
        Assert.Equal("cpu_process_saturation", ReadProperty(ReadProperty(diagnosis, "ProbableRootCause")!, "Code"));
        Assert.Equal("Indexer (PID 17)", ReadProperty(ReadProperty(diagnosis, "ProbableRootCause")!, "Subject"));
        Assert.Equal(85, ReadInt32(diagnosis, "Confidence"));
    }

    [Fact]
    public async Task Full_disk_with_update_error_is_one_correlated_disk_root_cause()
    {
        var evidence = CreateEvidence(
            disk: new DiskTelemetry(
                [new DiskVolumeTelemetry("C:\\", "System", "NTFS", 100_000, 98_000, 2_000, 98)],
                null,
                []),
            eventLog: new EventLogTelemetry(
                Now,
                [new EventLogEntryMetadata("System", "WindowsUpdateClient", 42, 20, "Error", Now, "host")],
                []));

        var diagnosis = Assert.Single(await EvaluateAsync(evidence));
        var root = ReadProperty(diagnosis, "ProbableRootCause")!;
        Assert.Equal("disk_capacity", ReadProperty(root, "Code"));
        Assert.Equal("Critical", ReadProperty(diagnosis, "Severity")?.ToString());
        Assert.Contains("windows_update_errors", ReadStrings(diagnosis, "SecondarySymptoms"));
    }

    [Fact]
    public async Task Network_rules_distinguish_dns_failure_from_gateway_failure()
    {
        var dnsDiagnosis = Assert.Single(await EvaluateAsync(CreateEvidence(network: new NetworkTelemetry(
            [],
            [Probe(NetworkProbeKind.Gateway, "192.0.2.1", NetworkProbeStatus.Succeeded),
             Probe(NetworkProbeKind.InternetIp, "203.0.113.1", NetworkProbeStatus.Succeeded),
             Probe(NetworkProbeKind.DnsResolution, "example.test", NetworkProbeStatus.Failed)]))));
        Assert.Equal("dns_failure", ReadProperty(ReadProperty(dnsDiagnosis, "ProbableRootCause")!, "Code"));

        var gatewayDiagnosis = Assert.Single(await EvaluateAsync(CreateEvidence(network: new NetworkTelemetry(
            [],
            [Probe(NetworkProbeKind.Gateway, "192.0.2.1", NetworkProbeStatus.Failed),
             Probe(NetworkProbeKind.InternetIp, "203.0.113.1", NetworkProbeStatus.Failed),
             Probe(NetworkProbeKind.DnsResolution, "example.test", NetworkProbeStatus.Failed)]))));
        Assert.Equal("gateway_failure", ReadProperty(ReadProperty(gatewayDiagnosis, "ProbableRootCause")!, "Code"));
    }

    [Fact]
    public async Task Fixed_memory_pressure_and_service_stop_produce_deterministic_diagnoses()
    {
        var evidence = CreateEvidence(
            cpuMemory: CreateCpu(5, [new ProcessResourceUsage(1, "Worker", 1, 970, null)], availableBytes: 30),
            processServices: new ProcessServiceTelemetry(
                [],
                [new ServiceTelemetry("Spooler", "Print Spooler", HostServiceStatus.Stopped)],
                [new ServiceStateChange("Spooler", HostServiceStatus.Running, HostServiceStatus.Stopped, Now)]));

        var diagnoses = await EvaluateAsync(evidence);
        Assert.Equal(2, diagnoses.Count);
        Assert.Contains(diagnoses, diagnosis =>
            Equals(ReadProperty(ReadProperty(diagnosis, "ProbableRootCause")!, "Code"), "memory_pressure")
            && ReadProperty(diagnosis, "Severity")?.ToString() == "High");
        Assert.Contains(diagnoses, diagnosis =>
            Equals(ReadProperty(ReadProperty(diagnosis, "ProbableRootCause")!, "Code"), "service_stopped")
            && Equals(ReadProperty(ReadProperty(diagnosis, "ProbableRootCause")!, "Subject"), "Spooler"));
    }

    private static MetricSample CpuSample(DateTimeOffset sampledAtUtc, double value) =>
        new(Guid.NewGuid(), "cpu.usage.percent", "host", sampledAtUtc, value);

    private static CpuMemoryTelemetry CreateCpu(
        double cpu,
        params ProcessResourceUsage[] topProcesses) =>
        CreateCpu(cpu, topProcesses, availableBytes: 500);

    private static CpuMemoryTelemetry CreateCpu(
        double cpu,
        ProcessResourceUsage[] topProcesses,
        long availableBytes) =>
        new(cpu, 1_000, availableBytes, null, null, topProcesses, topProcesses);

    private static NetworkProbeResult Probe(
        NetworkProbeKind kind,
        string target,
        NetworkProbeStatus status) =>
        new(kind, target, status, TimeSpan.FromMilliseconds(2), status == NetworkProbeStatus.Succeeded ? 2 : null, null, null);

    private static object CreateEvidence(
        SystemTelemetry? system = null,
        CpuMemoryTelemetry? cpuMemory = null,
        DiskTelemetry? disk = null,
        ProcessServiceTelemetry? processServices = null,
        NetworkTelemetry? network = null,
        EventLogTelemetry? eventLog = null,
        IReadOnlyList<MetricSample>? recentMetrics = null)
    {
        var assembly = typeof(CollectorResult<>).Assembly;
        var evidenceType = assembly.GetType("HostAgent.Core.Models.Diagnosis.HostEvidence");
        Assert.NotNull(evidenceType);
        var assessmentType = assembly.GetType("HostAgent.Core.Models.Baseline.BaselineAssessment");
        Assert.NotNull(assessmentType);
        var baselineList = Activator.CreateInstance(typeof(List<>).MakeGenericType(assessmentType!));
        var constructor = evidenceType!.GetConstructors().Single();
        var evidence = constructor.Invoke(
        [
            Now,
            system,
            cpuMemory,
            disk,
            processServices,
            network,
            eventLog,
            baselineList,
            recentMetrics ?? Array.Empty<MetricSample>()
        ]);
        return evidence;
    }

    private static async Task<IReadOnlyList<object>> EvaluateAsync(object evidence)
    {
        var engineType = typeof(CollectorResult<>).Assembly.GetType("HostAgent.Core.Diagnostics.DiagnosticEngine");
        Assert.NotNull(engineType);
        var engine = Activator.CreateInstance(engineType!);
        Assert.NotNull(engine);
        var evaluate = engineType!.GetMethod("EvaluateAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(evaluate);
        var boxedValueTask = evaluate!.Invoke(engine, [evidence, CancellationToken.None]);
        Assert.NotNull(boxedValueTask);
        var asTask = boxedValueTask!.GetType().GetMethod("AsTask", Type.EmptyTypes);
        Assert.NotNull(asTask);
        var task = Assert.IsAssignableFrom<Task>(asTask!.Invoke(boxedValueTask, null));
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task) as IEnumerable;
        Assert.NotNull(result);
        return result!.Cast<object>().ToArray();
    }

    private static object? ReadProperty(object instance, string name) =>
        instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance);

    private static int ReadInt32(object instance, string name) =>
        Assert.IsType<int>(ReadProperty(instance, name));

    private static IReadOnlyList<string> ReadStrings(object instance, string name) =>
        Assert.IsAssignableFrom<IEnumerable<string>>(ReadProperty(instance, name)).ToArray();
}
