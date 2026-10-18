using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class IsolatedRuntimeRecoveryEvidenceTests
{
    [Theory]
    [InlineData(2, 2, 18, 2, true)]
    [InlineData(2, 2, 10, 2, true)]
    [InlineData(2, 2, 9, 2, false)]
    [InlineData(2, 2, 18, 1, false)]
    [InlineData(2, 1, 18, 2, false)]
    [InlineData(1, 1, 18, 1, false)]
    public void Recovery_requires_two_failures_recoveries_views_and_persisted_samples(
        int injectedFailures, int recoveries, int views, int persistedSamples, bool expected)
    {
        Assert.Equal(expected, IsolatedRuntimeProbe.RecoveryProofIsComplete(
            injectedFailures, recoveries, views, persistedSamples));
    }
}
