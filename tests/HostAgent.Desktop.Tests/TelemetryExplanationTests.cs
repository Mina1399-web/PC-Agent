using System.Globalization;
using HostAgent.Desktop;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class TelemetryExplanationTests
{
    [Theory]
    [InlineData("cs-CZ", "Pád Windows", "Nedostatek volné paměti")]
    [InlineData("en", "Windows crash", "Low available memory")]
    [InlineData("de", "Windows-Absturz", "Wenig verfügbarer Arbeitsspeicher")]
    public void Incident_names_are_human_readable_and_hide_unknown_codes_by_default(string language, string crash, string memory)
    {
        var culture=CultureInfo.GetCultureInfo(language);
        Assert.Equal(crash,TechnicalText.Format("windows_bugcheck",culture));
        Assert.Equal(memory,TechnicalText.Format("memory_pressure",culture));
        Assert.Contains("windows_bugcheck",TechnicalText.Format("windows_bugcheck",culture,true));
        Assert.DoesNotContain("future_incident_code",TechnicalText.Format("future_incident_code",culture));
        Assert.Contains("future_incident_code",TechnicalText.Format("future_incident_code",culture,true));
    }

    private static string Explain(object reasons) => (string)new TelemetryExplanationConverter()
        .Convert(reasons, typeof(string), null!, CultureInfo.GetCultureInfo("cs-CZ"));

    [Fact]
    public void Missing_reasons_do_not_claim_that_measurements_are_healthy()
    {
        Assert.Equal("Podrobné důvody nebyly předány.", Explain(null!));
        Assert.Equal(Explain(null!), Explain(Array.Empty<string>()));
    }

    [Fact]
    public void Mixed_known_and_future_reasons_hide_unknown_codes_but_preserve_input()
    {
        string[] reasons = ["process_cpu_time_access_denied", "future_collector_reason"];
        var text = Explain(reasons);
        Assert.Contains("Čas CPU některých procesů", text);
        Assert.DoesNotContain("future_collector_reason", text);
        Assert.Contains("další omezení sběru dat", text);
        Assert.Equal(new[] { "process_cpu_time_access_denied", "future_collector_reason" }, reasons);
    }

    [Fact]
    public void Missing_system_measurement_is_distinct_from_partial_process_access()
    {
        Assert.Contains("Celkové využití CPU", Explain(new[] { "system_cpu_counter_unavailable" }));
        Assert.DoesNotContain("Celkové využití CPU", Explain(new[] { "process_cpu_time_access_denied" }));
    }

    [Theory]
    [InlineData("en", "CPU time for some processes")]
    [InlineData("de", "CPU-Zeit einiger Prozesse")]
    public void Explanations_follow_the_selected_interface_language(string language, string expected)
    {
        var text = (string)new TelemetryExplanationConverter().Convert(
            new[] { "process_cpu_time_access_denied" }, typeof(string), null!, CultureInfo.GetCultureInfo(language));
        Assert.Contains(expected, text);
        Assert.DoesNotContain("Čas CPU některých procesů", text);
    }
}
