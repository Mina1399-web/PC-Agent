namespace HostAgent.Core.Models.Baseline;

public sealed record BaselineOrigin(string MetricName, string Scope, DateTimeOffset StartedAtUtc);
