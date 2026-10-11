using HostAgent.Core.Metrics;
using HostAgent.Core.Models.Telemetry;
using Xunit;

namespace HostAgent.Core.Tests;

public sealed class NetworkMetricTests
{
    // Defect: disabled/unavailable probes create fabricated zero-success samples and contaminate baselines.
    [Fact]
    public void Unavailable_probes_emit_no_metrics_while_observed_failure_and_success_remain_distinct()
    {
        var time = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        var projector = new NetworkTelemetryMetricProjector();
        var unavailable = new NetworkProbeResult(
            NetworkProbeKind.InternetIp, "", NetworkProbeStatus.Unavailable,
            TimeSpan.Zero, null, null, "network_probes_disabled");
        Assert.Empty(projector.Project(new([], [unavailable]), time));

        var samples = projector.Project(new([], [unavailable,
            new(NetworkProbeKind.Gateway, "192.0.2.1", NetworkProbeStatus.Failed,
                TimeSpan.FromMilliseconds(2), null, 100, null),
            new(NetworkProbeKind.DnsResolution, "example.test", NetworkProbeStatus.Succeeded,
                TimeSpan.FromMilliseconds(3), 3, null, null)]), time);
        Assert.Equal(4, samples.Count);
        Assert.Equal(0, Assert.Single(samples,
            x => x.MetricName == "network.probe.success" && x.Scope == "Gateway").Value);
        Assert.Equal(1, Assert.Single(samples,
            x => x.MetricName == "network.probe.success" && x.Scope == "DnsResolution").Value);
        Assert.DoesNotContain(samples, x => x.Scope == "InternetIp");
    }
}
