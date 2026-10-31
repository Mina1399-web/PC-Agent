using HostAgent.Core.Models.Telemetry;
using HostAgent.Infrastructure.Collectors;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class WindowsDiskHealthTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_prediction_is_preserved_and_warned_without_claiming_an_unsupported_device_is_healthy(bool failure)
    {
        var reader = new HealthReader([new("PhysicalDrive0", failure ? "FailurePredicted" : "NoFailurePredicted", failure ? "smart_failure_predicted" : null)]);
        var result = await new WindowsDiskCollector(healthReader: reader).CollectAsync(CancellationToken.None);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value.Health);
        Assert.DoesNotContain("smart_health_unavailable", result.Warnings);
        Assert.Equal(failure, result.Warnings.Contains("smart_failure_predicted"));
    }

    [Fact]
    public async Task Missing_prediction_is_unavailable_and_cancellation_is_observed()
    {
        var result = await new WindowsDiskCollector(healthReader: new HealthReader([])).CollectAsync(CancellationToken.None);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value.Health);
        Assert.Contains("smart_health_unavailable", result.Warnings);
        using var token = new CancellationTokenSource(); token.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new WindowsDiskHealthReader().ReadAsync(token.Token));
    }

    private sealed class HealthReader(IReadOnlyList<DiskHealthTelemetry> values) : IDiskHealthReader
    {
        public ValueTask<IReadOnlyList<DiskHealthTelemetry>> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(values);
    }
}
