using HostAgent.Core.Models.Baseline;
using HostAgent.Core.Models.Incidents;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Core.Models.Diagnosis;

public enum DiagnosisSeverity
{
    Information,
    Warning,
    High,
    Critical
}

public sealed record DiagnosticEvidence(
    string EvidenceId,
    string Source,
    string Observation,
    double? NumericValue,
    DateTimeOffset ObservedAtUtc);

public sealed record ProbableRootCause(
    string Code,
    string Summary,
    string? Subject);

public sealed record RemediationRecommendation(
    string RecommendationCode,
    string Summary);

public sealed record HostEvidence(
    DateTimeOffset SampledAtUtc,
    SystemTelemetry? System,
    CpuMemoryTelemetry? CpuMemory,
    DiskTelemetry? Disk,
    ProcessServiceTelemetry? ProcessServices,
    NetworkTelemetry? Network,
    EventLogTelemetry? EventLog,
    IReadOnlyList<BaselineAssessment> Baselines,
    IReadOnlyList<MetricSample> RecentMetrics);

public sealed record Diagnosis(
    string DiagnosisId,
    string CorrelationKey,
    DateTimeOffset ObservedAtUtc,
    DiagnosisSeverity Severity,
    int Confidence,
    IReadOnlyList<DiagnosticEvidence> Evidence,
    ProbableRootCause ProbableRootCause,
    IReadOnlyList<string> SecondarySymptoms,
    RemediationRecommendation RecommendedRemediation,
    IncidentLifecycleState IncidentState);
