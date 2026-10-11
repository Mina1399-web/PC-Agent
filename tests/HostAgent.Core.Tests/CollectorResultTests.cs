using HostAgent.Core.Models.Telemetry;
using Xunit;

namespace HostAgent.Core.Tests;

public sealed class CollectorResultTests
{
    private static readonly DateTimeOffset SampleTime =
        new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Unavailable_result_has_no_fabricated_payload_and_preserves_failure_code()
    {
        var result = CollectorResult<SystemTelemetry>.Unavailable(
            "system",
            SampleTime,
            "access_denied");

        Assert.Equal(CollectorStatus.Unavailable, result.Status);
        Assert.Null(result.Value);
        Assert.Equal("access_denied", result.ErrorCode);
        Assert.Equal(SampleTime, result.SampledAtUtc);
    }

    [Fact]
    public void Partial_result_keeps_available_data_and_marks_degraded_fields()
    {
        var telemetry = new SystemTelemetry(
            "Windows",
            "10.0.26220",
            "build-26220",
            SampleTime.AddMinutes(-15),
            TimeSpan.FromMinutes(15),
            null);

        var result = CollectorResult<SystemTelemetry>.Partial(
            "system",
            SampleTime,
            telemetry,
            ["shutdown_history_unavailable"]);

        Assert.Equal(CollectorStatus.Partial, result.Status);
        Assert.Same(telemetry, result.Value);
        Assert.Contains("shutdown_history_unavailable", result.Warnings);
        Assert.Null(result.ErrorCode);
    }
}
