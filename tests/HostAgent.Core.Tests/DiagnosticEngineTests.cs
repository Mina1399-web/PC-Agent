using System.Collections;
using System.Reflection;
using System.Text.Json;
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
        Assert.Equal(94, ReadInt32(diagnosis, "Confidence"));
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
        Assert.Equal(90, ReadInt32(dnsDiagnosis, "Confidence"));

        var gatewayDiagnosis = Assert.Single(await EvaluateAsync(CreateEvidence(network: new NetworkTelemetry(
            [],
            [Probe(NetworkProbeKind.Gateway, "192.0.2.1", NetworkProbeStatus.Failed),
             Probe(NetworkProbeKind.InternetIp, "203.0.113.1", NetworkProbeStatus.Failed),
             Probe(NetworkProbeKind.DnsResolution, "example.test", NetworkProbeStatus.Failed)]))));
        Assert.Equal("gateway_failure", ReadProperty(ReadProperty(gatewayDiagnosis, "ProbableRootCause")!, "Code"));
        Assert.Equal(90, ReadInt32(gatewayDiagnosis, "Confidence"));
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
        Assert.Equal(90, ReadInt32(diagnoses.Single(item => Equals(ReadProperty(ReadProperty(item, "ProbableRootCause")!, "Code"), "memory_pressure")), "Confidence"));
        Assert.Equal(88, ReadInt32(diagnoses.Single(item => Equals(ReadProperty(ReadProperty(item, "ProbableRootCause")!, "Code"), "service_stopped")), "Confidence"));
    }

    [Fact]
    public async Task Manual_demand_start_service_stopping_without_crash_evidence_is_not_a_high_confidence_failure()
    {
        // Numeric enum values keep this regression compilable before ServiceTelemetry
        // gains the StartMode field; currently JSON ignores that field and the test is RED.
        var service = JsonSerializer.Deserialize<ServiceTelemetry>(
            """{"ServiceName":"FrameServer","DisplayName":"Windows Camera Frame Server","Status":1,"StartMode":2}""");
        Assert.NotNull(service);

        var evidence = CreateEvidence(
            processServices: new ProcessServiceTelemetry(
                [],
                [service],
                [new ServiceStateChange("FrameServer", HostServiceStatus.Running, HostServiceStatus.Stopped, Now)]),
            eventLog: new EventLogTelemetry(Now, [], []));

        Assert.Empty(await EvaluateAsync(evidence));
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(false, false, 88)]
    [InlineData(true, true, 78)]
    public async Task Automatic_trigger_service_stop_requires_crash_evidence_for_failure(bool hasTriggers, bool crash, int confidence)
    {
        var service = JsonSerializer.Deserialize<ServiceTelemetry>(
            $$"""{"ServiceName":"SyntheticTrigger","DisplayName":"Synthetic trigger","Status":1,"StartMode":1,"HasTriggers":{{hasTriggers.ToString().ToLowerInvariant()}}}""")!;
        var diagnoses = await EvaluateAsync(CreateEvidence(
            processServices: new ProcessServiceTelemetry([], [service],
                [new ServiceStateChange(service.ServiceName, HostServiceStatus.Running, HostServiceStatus.Stopped, Now)]),
            eventLog: new EventLogTelemetry(Now, crash ? [Crash(42, service.ServiceName)] : [], [])));
        if (confidence == 0) Assert.Empty(diagnoses);
        else Assert.Equal(confidence, ReadInt32(Assert.Single(diagnoses), "Confidence"));
    }

    [Theory]
    [InlineData(-1441, 0)]
    [InlineData(-1440, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public async Task Scm_crash_respects_exact_24_hour_window(int minutes, int expectedCount)
    {
        var diagnoses = await EvaluateAsync(CreateEvidence(
            processServices: new ProcessServiceTelemetry([], [], []),
            eventLog: new EventLogTelemetry(Now,
                [new EventLogEntryMetadata("System", "Service Control Manager", 42, 7031, "Error", Now.AddMinutes(minutes), "host")], [])));
        Assert.Equal(expectedCount, diagnoses.Count);
        if (expectedCount == 1) Assert.Equal(78, ReadInt32(Assert.Single(diagnoses), "Confidence"));
    }

    [Fact]
    public async Task Scm_crash_without_timestamp_is_not_current_evidence()
    {
        Assert.Empty(await EvaluateAsync(CreateEvidence(
            processServices: new ProcessServiceTelemetry([], [], []),
            eventLog: new EventLogTelemetry(Now,
                [new EventLogEntryMetadata("System", "Service Control Manager", 42, 7034, "Error", null, "host")], []))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(42L)]
    public async Task Crash_subjects_and_keys_separate_services_even_with_missing_or_equal_record_ids(long? recordId)
    {
        var first = Crash(recordId, "Spooler");
        var second = Crash(recordId, "OtherService");
        var diagnoses = await EvaluateAsync(CreateEvidence(
            processServices: new ProcessServiceTelemetry([], [], []),
            eventLog: new EventLogTelemetry(Now, [first, second], [])));
        Assert.Equal(2, diagnoses.Count);
        Assert.Equal(2, diagnoses.Select(item => ReadProperty(item, "CorrelationKey")).Distinct().Count());
        Assert.Contains(diagnoses, item => Equals(ReadProperty(ReadProperty(item, "ProbableRootCause")!, "Subject"), "Spooler"));
        Assert.All(diagnoses, item => Assert.Equal(78, ReadInt32(item, "Confidence")));
    }

    [Fact]
    public async Task Crash_for_same_service_has_stable_key_without_process_collector_and_does_not_hide_other_stop()
    {
        var events = new EventLogTelemetry(Now, [Crash(null, "Spooler")], []);
        var first = Assert.Single(await EvaluateAsync(CreateEvidence(eventLog: events)));
        var second = Assert.Single(await EvaluateAsync(CreateEvidence(eventLog: events with { Events = [Crash(99, "spooler")] })));
        Assert.Equal(ReadProperty(first, "CorrelationKey"), ReadProperty(second, "CorrelationKey"));
        var mixed = await EvaluateAsync(CreateEvidence(eventLog: events,
            processServices: new ProcessServiceTelemetry([], [],
                [new ServiceStateChange("OtherService", HostServiceStatus.Running, HostServiceStatus.Stopped, Now)])));
        Assert.Equal(2, mixed.Count);
    }

    [Fact]
    public async Task Unidentified_crashes_without_ids_get_stable_distinct_event_keys()
    {
        var one = new EventLogEntryMetadata("System", "Service Control Manager", null, 7031, "Error", Now, "host");
        var two = one with { OccurredAtUtc = Now.AddMinutes(-1) };
        var host = CreateEvidence(processServices: new ProcessServiceTelemetry([], [], []),
            eventLog: new EventLogTelemetry(Now, [one, two], []));
        var first = await EvaluateAsync(host);
        var second = await EvaluateAsync(host);
        Assert.Equal(2, first.Select(item => ReadProperty(item, "CorrelationKey")).Distinct().Count());
        Assert.Equal(first.Select(item => ReadProperty(item, "CorrelationKey")), second.Select(item => ReadProperty(item, "CorrelationKey")));
        Assert.All(first, item => Assert.NotNull(ReadProperty(ReadProperty(item, "ProbableRootCause")!, "Subject")));
        var reversed = await EvaluateAsync(CreateEvidence(processServices: new ProcessServiceTelemetry([], [], []),
            eventLog: new EventLogTelemetry(Now, [two, one], [])));
        Assert.Equal(first.Select(item => ReadProperty(item, "CorrelationKey")).OrderBy(item => item),
            reversed.Select(item => ReadProperty(item, "CorrelationKey")).OrderBy(item => item));
    }

    private static EventLogEntryMetadata Crash(long? id, string service)
    {
        var metadata = new EventLogEntryMetadata("System", "Service Control Manager", id, 7031, "Error", Now, "host");
        var property = typeof(EventLogEntryMetadata).GetProperty("AffectedServiceName");
        Assert.NotNull(property);
        property!.SetValue(metadata, service);
        return metadata;
    }

    [Theory]
    [InlineData(null, 82)]
    [InlineData(-1441, 82)]
    [InlineData(1, 82)]
    [InlineData(-1440, 94)]
    [InlineData(0, 94)]
    public async Task Disk_corroboration_requires_known_contemporaneous_timestamp(int? minutes, int confidence)
    {
        var diagnosis = Assert.Single(await EvaluateAsync(CreateEvidence(
            disk: new DiskTelemetry([new DiskVolumeTelemetry("C:\\", "System", "NTFS", 100_000, 98_000, 2_000, 98)], null, []),
            eventLog: new EventLogTelemetry(Now,
                [new EventLogEntryMetadata("System", "WindowsUpdateClient", 42, 20, "Error", minutes is { } value ? Now.AddMinutes(value) : null, "host")], []))));
        Assert.Equal(confidence, ReadInt32(diagnosis, "Confidence"));
        Assert.Equal(confidence == 94, ReadStrings(diagnosis, "SecondarySymptoms").Contains("windows_update_errors"));
    }

    [Fact]
    public async Task Internet_failure_is_distinct_from_dns_and_gateway_failure_with_exact_confidence()
    {
        var diagnosis = Assert.Single(await EvaluateAsync(CreateEvidence(network: new NetworkTelemetry([],
            [Probe(NetworkProbeKind.Gateway, "192.0.2.1", NetworkProbeStatus.Succeeded),
             Probe(NetworkProbeKind.InternetIp, "203.0.113.1", NetworkProbeStatus.Failed),
             Probe(NetworkProbeKind.DnsResolution, "example.test", NetworkProbeStatus.Failed)]))));
        Assert.Equal("internet_connectivity_failure", ReadProperty(ReadProperty(diagnosis, "ProbableRootCause")!, "Code"));
        Assert.Equal(80, ReadInt32(diagnosis, "Confidence"));
        Assert.Contains("dns_resolution_failed", ReadStrings(diagnosis, "SecondarySymptoms"));
    }

    [Fact]
    public async Task Wer_bugcheck_is_classified_without_claiming_a_faulty_driver_and_recommends_dump_analysis()
    {
        var eventLog = new EventLogTelemetry(Now,
            [new EventLogEntryMetadata("System", "Microsoft-Windows-WER-SystemErrorReporting", 15000, 1001,
                "Error", Now, "host")], []);

        var diagnosis = Assert.Single(await EvaluateAsync(CreateEvidence(eventLog: eventLog)));

        var root = ReadProperty(diagnosis, "ProbableRootCause")!;
        Assert.Equal("windows_bugcheck", ReadProperty(root, "Code"));
        Assert.Null(ReadProperty(root, "Subject"));
        Assert.Contains("faulty driver is not identified", (string)ReadProperty(root, "Summary")!);
        Assert.Equal(70, ReadInt32(diagnosis, "Confidence"));
        var recommendation = ReadProperty(diagnosis, "RecommendedRemediation")!;
        Assert.Equal("analyze_crash_dump", ReadProperty(recommendation, "RecommendationCode"));
        Assert.Contains("Open the corresponding crash dump in WinDbg", (string)ReadProperty(recommendation, "Summary")!);
        Assert.Contains("driver_attribution_pending_dump_analysis", ReadStrings(diagnosis, "SecondarySymptoms"));
    }

    [Fact]
    public async Task Irql_not_less_or_equal_explains_stop_code_parameters_and_dump_without_identifying_a_driver()
    {
        var crash = new EventLogEntryMetadata("System", "Microsoft-Windows-WER-SystemErrorReporting", 15000, 1001,
            "Error", Now, "host")
        {
            BugCheckCode = 0xA,
            BugCheckParameters = [0xffff9280d9724024, 0xff, 0x26, 0xfffff802bd4b77e0],
            CrashDumpFileName = "101226-15000-01.dmp"
        };

        var diagnosis = Assert.Single(await EvaluateAsync(CreateEvidence(eventLog: new EventLogTelemetry(Now, [crash], []))));

        var root = ReadProperty(diagnosis, "ProbableRootCause")!;
        Assert.Equal("bugcheck_irql_not_less_or_equal", ReadProperty(root, "Code"));
        Assert.Null(ReadProperty(root, "Subject"));
        Assert.Contains("faulty driver is not identified", (string)ReadProperty(root, "Summary")!);
        Assert.Equal(88, ReadInt32(diagnosis, "Confidence"));
        var evidence = Assert.IsAssignableFrom<IEnumerable>(ReadProperty(diagnosis, "Evidence"))!.Cast<object>().ToArray();
        Assert.Contains(evidence, item => ((string)ReadProperty(item, "Observation")!).Contains("0xA", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(evidence, item => ((string)ReadProperty(item, "Observation")!).Contains("0xffff9280d9724024", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(evidence, item => ((string)ReadProperty(item, "Observation")!).Contains("101226-15000-01.dmp", StringComparison.Ordinal));
        var recommendation = ReadProperty(diagnosis, "RecommendedRemediation")!;
        Assert.Equal("analyze_crash_dump", ReadProperty(recommendation, "RecommendationCode"));
        Assert.Contains("!analyze -v", (string)ReadProperty(recommendation, "Summary")!);
        Assert.Contains("driver_attribution_pending_dump_analysis", ReadStrings(diagnosis, "SecondarySymptoms"));
    }

    [Theory]
    [InlineData(-1441, 0)]
    [InlineData(-1440, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public async Task Bugcheck_correlation_uses_only_timestamped_events_inside_the_24_hour_window(int minutes, int expectedCount)
    {
        var crash = new EventLogEntryMetadata("System", "Microsoft-Windows-WER-SystemErrorReporting", 42, 1001,
            "Error", Now.AddMinutes(minutes), "host") { BugCheckCode = 0xA };

        var diagnoses = await EvaluateAsync(CreateEvidence(eventLog: new EventLogTelemetry(Now, [crash], [])));

        Assert.Equal(expectedCount, diagnoses.Count);
        if (expectedCount == 1) Assert.Equal(88, ReadInt32(Assert.Single(diagnoses), "Confidence"));
    }

    [Fact]
    public async Task Repeated_same_stop_code_is_one_diagnosis_with_stable_correlation_and_recurrence_evidence()
    {
        var first = new EventLogEntryMetadata("System", "Microsoft-Windows-WER-SystemErrorReporting", 42, 1001,
            "Error", Now.AddHours(-4), "host") { BugCheckCode = 0xA };
        var second = first with { RecordId = 43, OccurredAtUtc = Now };

        var diagnosis = Assert.Single(await EvaluateAsync(CreateEvidence(eventLog: new EventLogTelemetry(Now, [first, second], []))));

        Assert.Equal("bugcheck:0xA", ReadProperty(diagnosis, "CorrelationKey"));
        Assert.Equal("bugcheck:0xA", ReadProperty(Assert.Single(await EvaluateAsync(CreateEvidence(
            eventLog: new EventLogTelemetry(Now, [second], [])))), "CorrelationKey"));
        Assert.Contains("repeated_bugcheck_events", ReadStrings(diagnosis, "SecondarySymptoms"));
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable>(ReadProperty(diagnosis, "Evidence"))!.Cast<object>().Count());
    }

    [Theory]
    [InlineData(7, 25, "Information")]
    [InlineData(8, 56, "Warning")]
    [InlineData(31, 80, "Warning")]
    public async Task Adaptive_diagnosis_preserves_learning_mode_confidence(int day, int confidence, string severity)
    {
        var engine = new HostAgent.Core.Diagnostics.BaselineEngine();
        var start = Now.AddDays(-day);
        for (var index = 0; index <= 5; index++)
            await engine.ObserveAsync("disk.latency.ms", "C:\\", start.AddDays(index), 10);
        var assessment = await engine.ObserveAsync("disk.latency.ms", "C:\\", Now, 80);
        var evidence = new HostAgent.Core.Models.Diagnosis.HostEvidence(Now, null, null, null, null, null, null, [assessment], []);
        var diagnosis = Assert.Single(await EvaluateAsync(evidence));
        Assert.Equal("adaptive_metric_deviation", ReadProperty(ReadProperty(diagnosis, "ProbableRootCause")!, "Code"));
        Assert.Equal(confidence, ReadInt32(diagnosis, "Confidence"));
        Assert.Equal(severity, ReadProperty(diagnosis, "Severity")!.ToString());
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
