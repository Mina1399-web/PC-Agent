namespace HostAgent.Core.Models.Baseline;

public enum BaselineLearningMode
{
    Informational,
    ReducedConfidence,
    Full
}

public enum AdaptiveSignal
{
    Normal,
    Informational,
    Anomaly
}

public sealed record BaselineAssessment(
    string MetricName,
    string Scope,
    DateTimeOffset SampledAtUtc,
    double CurrentValue,
    int SampleCount,
    double TypicalValue,
    double Median,
    double MedianAbsoluteDeviation,
    double RobustDeviation,
    double FifthPercentile,
    double NinetyFifthPercentile,
    double RobustZScore,
    double TrendPerDay,
    TimeSpan AnomalousDuration,
    int RecurrenceCount,
    BaselineLearningMode LearningMode,
    AdaptiveSignal AdaptiveSignal,
    int Confidence);
