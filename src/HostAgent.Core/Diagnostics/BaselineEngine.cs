using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Baseline;

namespace HostAgent.Core.Diagnostics;

public sealed class BaselineEngine : IBaselineEngine
{
    private const int MinimumSamplesForAnomaly = 5;
    private const int MaximumSeries = 4096;
    private const int MaximumSamplesPerSeries = 50_000;
    private const int MaximumTrendPoints = 64;
    private static readonly TimeSpan FullWindow = TimeSpan.FromDays(30);
    private readonly object _gate = new();
    private readonly Dictionary<SeriesKey, List<BaselinePoint>> _series = [];

    public ValueTask<BaselineAssessment> ObserveAsync(
        string metricName,
        string scope,
        DateTimeOffset sampledAtUtc,
        double value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metricName);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (metricName.Length > 128 || scope.Length > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(metricName), "Baseline keys exceed the configured bounds.");
        }

        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Baseline values must be finite.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var sampledAt = sampledAtUtc.ToUniversalTime();
        var key = new SeriesKey(metricName, scope);
        lock (_gate)
        {
            if (!_series.TryGetValue(key, out var points))
            {
                if (_series.Count >= MaximumSeries)
                {
                    throw new InvalidOperationException("The baseline engine reached its bounded series capacity.");
                }

                points = [];
                _series.Add(key, points);
            }

            var sameTimestampIndex = points.FindIndex(point => point.SampledAtUtc == sampledAt);
            var prior = points
                .Where(point => point.SampledAtUtc < sampledAt)
                .OrderBy(static point => point.SampledAtUtc)
                .ToArray();
            var startedAt = points.Count == 0
                ? sampledAt
                : points.Min(static point => point.SampledAtUtc);
            var learningMode = GetLearningMode(sampledAt - startedAt);
            var windowStart = learningMode == BaselineLearningMode.Full
                ? sampledAt - FullWindow
                : startedAt;
            var baseline = prior.Where(point => point.SampledAtUtc >= windowStart).ToArray();

            var values = baseline.Select(static point => point.Value).Order().ToArray();
            var median = values.Length == 0 ? value : Median(values);
            var deviations = values.Select(sample => Math.Abs(sample - median)).Order().ToArray();
            var mad = deviations.Length == 0 ? 0 : Median(deviations);
            var robustScale = Math.Max(mad * 1.4826, Math.Max(0.5, Math.Abs(median) * 0.05));
            var robustZScore = values.Length == 0 ? 0 : 0.6745 * (value - median) / madOrScale(mad, median);
            var fifthPercentile = values.Length == 0 ? value : Percentile(values, 0.05);
            var ninetyFifthPercentile = values.Length == 0 ? value : Percentile(values, 0.95);
            var isAnomalyCandidate = values.Length >= MinimumSamplesForAnomaly
                && ((value > ninetyFifthPercentile && robustZScore >= 3.5)
                    || (value < fifthPercentile && robustZScore <= -3.5));
            var signal = !isAnomalyCandidate
                ? AdaptiveSignal.Normal
                : learningMode == BaselineLearningMode.Informational
                    ? AdaptiveSignal.Informational
                    : AdaptiveSignal.Anomaly;
            var confidence = signal switch
            {
                AdaptiveSignal.Informational => 25,
                AdaptiveSignal.Anomaly when learningMode == BaselineLearningMode.ReducedConfidence =>
                    50 + Math.Min(20, values.Length),
                AdaptiveSignal.Anomaly => 75 + Math.Min(25, values.Length),
                _ => 0
            };
            var trend = CalculateTrend(baseline);
            var outlierBoundary = value >= median
                ? Math.Max(ninetyFifthPercentile, median + 3.5 * robustScale)
                : Math.Min(fifthPercentile, median - 3.5 * robustScale);
            var anomalousDuration = signal == AdaptiveSignal.Normal
                ? TimeSpan.Zero
                : CalculateAnomalousDuration(points, sampledAt, value, outlierBoundary);
            var recurrenceCount = signal == AdaptiveSignal.Normal
                ? 0
                : baseline.Count(point => value >= median
                    ? point.Value > outlierBoundary
                    : point.Value < outlierBoundary) + 1;

            var assessment = new BaselineAssessment(
                metricName,
                scope,
                sampledAt,
                value,
                values.Length,
                median,
                median,
                mad,
                robustScale,
                fifthPercentile,
                ninetyFifthPercentile,
                robustZScore,
                trend,
                anomalousDuration,
                recurrenceCount,
                learningMode,
                signal,
                confidence);

            var current = new BaselinePoint(sampledAt, value);
            if (sameTimestampIndex >= 0)
            {
                points[sameTimestampIndex] = current;
            }
            else
            {
                var insertionIndex = points.FindIndex(point => point.SampledAtUtc > sampledAt);
                if (insertionIndex < 0)
                {
                    points.Add(current);
                }
                else
                {
                    points.Insert(insertionIndex, current);
                }
            }

            if (points.Count > MaximumSamplesPerSeries)
            {
                points.RemoveRange(0, points.Count - MaximumSamplesPerSeries);
            }

            return ValueTask.FromResult(assessment);
        }
    }

    private static BaselineLearningMode GetLearningMode(TimeSpan age) =>
        age < TimeSpan.FromDays(8)
            ? BaselineLearningMode.Informational
            : age <= TimeSpan.FromDays(30)
                ? BaselineLearningMode.ReducedConfidence
                : BaselineLearningMode.Full;

    private static double madOrScale(double mad, double median) =>
        mad > 0 ? mad : Math.Max(0.5, Math.Abs(median) * 0.05);

    private static double Median(IReadOnlyList<double> sortedValues)
    {
        var middle = sortedValues.Count / 2;
        return sortedValues.Count % 2 == 0
            ? (sortedValues[middle - 1] + sortedValues[middle]) / 2
            : sortedValues[middle];
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        var position = (sortedValues.Count - 1) * percentile;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);
        var fraction = position - lowerIndex;
        return sortedValues[lowerIndex] + (sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction;
    }

    private static double CalculateTrend(IReadOnlyList<BaselinePoint> points)
    {
        if (points.Count < 2)
        {
            return 0;
        }

        var selected = SelectTrendPoints(points);
        var slopes = new List<double>(selected.Count * (selected.Count - 1) / 2);
        for (var left = 0; left < selected.Count - 1; left++)
        {
            for (var right = left + 1; right < selected.Count; right++)
            {
                var days = (selected[right].SampledAtUtc - selected[left].SampledAtUtc).TotalDays;
                if (days > 0)
                {
                    slopes.Add((selected[right].Value - selected[left].Value) / days);
                }
            }
        }

        return slopes.Count == 0 ? 0 : Median(slopes.Order().ToArray());
    }

    private static IReadOnlyList<BaselinePoint> SelectTrendPoints(IReadOnlyList<BaselinePoint> points)
    {
        if (points.Count <= MaximumTrendPoints)
        {
            return points;
        }

        var selected = new List<BaselinePoint>(MaximumTrendPoints);
        for (var index = 0; index < MaximumTrendPoints; index++)
        {
            var sourceIndex = (int)Math.Round(
                index * (points.Count - 1d) / (MaximumTrendPoints - 1),
                MidpointRounding.AwayFromZero);
            selected.Add(points[sourceIndex]);
        }

        return selected;
    }

    private static TimeSpan CalculateAnomalousDuration(
        IReadOnlyList<BaselinePoint> points,
        DateTimeOffset sampledAtUtc,
        double currentValue,
        double boundary)
    {
        var highSide = currentValue >= boundary;
        var firstTime = sampledAtUtc;
        var expectedPreviousTime = sampledAtUtc;
        for (var index = points.Count - 1; index >= 0; index--)
        {
            var point = points[index];
            if (point.SampledAtUtc >= sampledAtUtc)
            {
                continue;
            }

            if (expectedPreviousTime - point.SampledAtUtc > TimeSpan.FromMinutes(2)
                || (highSide ? point.Value <= boundary : point.Value >= boundary))
            {
                break;
            }

            firstTime = point.SampledAtUtc;
            expectedPreviousTime = point.SampledAtUtc;
        }

        return sampledAtUtc - firstTime;
    }

    private sealed record SeriesKey(string MetricName, string Scope);
    private sealed record BaselinePoint(DateTimeOffset SampledAtUtc, double Value);
}
