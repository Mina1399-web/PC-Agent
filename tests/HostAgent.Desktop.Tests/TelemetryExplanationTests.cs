using System.Globalization;
using HostAgent.Desktop;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class TelemetryExplanationTests
{
    private static string Explain(object reasons) => (string)new TelemetryExplanationConverter()
        .Convert(reasons, typeof(string), null!, CultureInfo.GetCultureInfo("cs-CZ"));

    [Fact]
    public void Missing_reasons_do_not_claim_that_measurements_are_healthy()
    {
        Assert.Equal("Podrobné důvody nebyly předány.", Explain(null!));
        Assert.Equal(Explain(null!), Explain(Array.Empty<string>()));
    }

    [Fact]
    public void Mixed_known_and_future_reasons_preserve_unknown_diagnostics()
    {
        string[] reasons = ["process_cpu_time_access_denied", "future_collector_reason"];
        var text = Explain(reasons);
        Assert.Contains("Čas CPU některých procesů", text);
        Assert.Contains("future_collector_reason", text);
        Assert.Equal(new[] { "process_cpu_time_access_denied", "future_collector_reason" }, reasons);
    }

    [Fact]
    public void Missing_system_measurement_is_distinct_from_partial_process_access()
    {
        Assert.Contains("Celkové využití CPU", Explain(new[] { "system_cpu_counter_unavailable" }));
        Assert.DoesNotContain("Celkové využití CPU", Explain(new[] { "process_cpu_time_access_denied" }));
    }
}
