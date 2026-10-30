using System.Globalization;
using HostAgent.Desktop;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class DashboardDisplayTests
{
    [Theory]
    [InlineData("en", "Active")]
    [InlineData("de", "Aktiv")]
    public void Incident_state_uses_the_selected_interface_language(string language, string expected)
        => Assert.Equal(expected, new IncidentStateConverter().Convert("Active", typeof(string), null!, CultureInfo.GetCultureInfo(language)));

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1024L, "1 KiB")]
    [InlineData(1610612736L, "1,5 GiB")]
    [InlineData(-1L, "—")]
    public void Byte_sizes_use_explicit_binary_units_and_czech_numbers(long bytes, string expected)
    {
        Assert.Equal(expected, new ByteSizeConverter().Convert(bytes, typeof(string), null!, CultureInfo.GetCultureInfo("cs-CZ")));
    }

    [Fact]
    public void Unknown_or_missing_values_do_not_become_measured_zero_or_resolved()
    {
        Assert.Equal("—", new ByteSizeConverter().Convert(null!, typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.Equal("Remediation in progress", new IncidentStateConverter().Convert("Remediating", typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.Equal("Technical code: new_state", new IncidentStateConverter().Convert("new_state", typeof(string), null!, CultureInfo.InvariantCulture));
    }
}
