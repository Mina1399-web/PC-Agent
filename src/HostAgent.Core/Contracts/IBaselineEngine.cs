using HostAgent.Core.Models.Baseline;

namespace HostAgent.Core.Contracts;

public interface IBaselineEngine
{
    ValueTask<BaselineAssessment> ObserveAsync(
        string metricName,
        string scope,
        DateTimeOffset sampledAtUtc,
        double value,
        CancellationToken cancellationToken = default);
}
