using System;
using System.Globalization;
using HostAgent.Core.Dashboard;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class SituationOrbitSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("en", "Processor", "RAM", "Storage", "Network", "Services")]
    [InlineData("cs-CZ", "Procesor", "Paměť RAM", "Úložiště", "Síť", "Služby")]
    [InlineData("de", "Prozessor", "Arbeitsspeicher", "Speicher", "Netzwerk", "Dienste")]
    public void Node_titles_are_localized_on_startup(string language, string cpu, string memory,
        string disks, string network, string services)
    {
        using var fixture = new DesktopLocalizationTests.PreferencesFixture();
        var localization = new DesktopLocalization(fixture.Preferences, CultureInfo.GetCultureInfo(language));
        var result = SituationOrbitSnapshot.Create(null, "disconnected", false, localization, Now);

        Assert.Equal(cpu, Node(result, SituationSubsystem.Cpu).Title);
        Assert.Equal(memory, Node(result, SituationSubsystem.Memory).Title);
        Assert.Equal(disks, Node(result, SituationSubsystem.Disks).Title);
        Assert.Equal(network, Node(result, SituationSubsystem.Network).Title);
        Assert.Equal(services, Node(result, SituationSubsystem.Services).Title);
    }

    [Fact]
    public void Fresh_zero_is_visible_and_missing_memory_has_no_value()
    {
        var result = Project(Overview() with { Cpu = new(Now, 0) });
        Assert.Equal("0 %", Node(result, SituationSubsystem.Cpu).Summary);
        Assert.Equal("Data unavailable", Node(result, SituationSubsystem.Memory).Status);
        Assert.Empty(Node(result, SituationSubsystem.Memory).Summary);
    }

    [Theory]
    [InlineData(-4, 12, "Data out of date")]
    [InlineData(1, 12, "Data out of date")]
    [InlineData(0, double.NaN, "Data unavailable")]
    [InlineData(0, 101, "Data unavailable")]
    [InlineData(0, -1, "Data unavailable")]
    public void Invalid_or_old_measurement_is_never_displayed(int offset, double value, string status)
    {
        var result = Project(Overview() with { Cpu = new(Now.AddMinutes(offset), value), Memory = new(Now.AddMinutes(offset), value) });
        foreach (var id in new[] { SituationSubsystem.Cpu, SituationSubsystem.Memory })
        {
            Assert.Equal(status, Node(result, id).Status);
            Assert.Empty(Node(result, id).Summary);
        }
    }

    [Theory]
    [InlineData("collector_partial", "Partial data")]
    [InlineData("collector_timeout", "Collection limited")]
    public void Collector_limitation_is_visible_even_with_recent_samples(string code, string status)
    {
        var result = Project(Overview() with { Cpu = new(Now, 15), Health = [new("cpu-memory", code, true)] });
        Assert.Equal(status, Node(result, SituationSubsystem.Cpu).Status);
        Assert.Equal("15 %", Node(result, SituationSubsystem.Cpu).Summary);
        Assert.Contains(status, Node(result, SituationSubsystem.Memory).Details);
    }

    [Fact]
    public void Offline_and_stale_overviews_hide_values()
    {
        Assert.Equal("Local service unavailable", Project(null, "disconnected").Status);
        var stale = Project(Overview() with { GeneratedAtUtc = Now.AddMinutes(-4), Cpu = new(Now, 15) });
        Assert.StartsWith("Last known local Host Agent overview:", stale.Source);
        Assert.All(stale.Nodes, node =>
        {
            Assert.Equal("Data out of date", node.Status);
            Assert.Empty(node.Summary);
        });
        var offline = Project(null, "disconnected", Now.AddMinutes(-5));
        Assert.StartsWith("Last known local Host Agent overview:", offline.Source);
        Assert.All(offline.Nodes, node => Assert.Empty(node.Summary));
    }

    [Fact]
    public void Adapter_count_and_probe_status_have_independent_time_labels()
    {
        var overview = Overview() with { ConnectedAdapters = 0 };
        var missing = Node(Project(overview), SituationSubsystem.Network);
        Assert.Contains("0", missing.Summary);
        Assert.Contains("Network check results are unavailable.", missing.Details);
        Assert.Contains("The adapter count has no separate measurement time.", missing.Details);

        var disabled = Node(Project(overview with { NetworkChecks = new(Now, false, []) }), SituationSubsystem.Network);
        Assert.Contains(disabled.Details, line => line.StartsWith("Network checks measured:", StringComparison.Ordinal));
        Assert.Contains(disabled.Details, line => line.StartsWith("Active network checks are disabled.", StringComparison.Ordinal));

        var stale = Node(Project(overview with { NetworkChecks = new(Now.AddMinutes(-4), true, [new("Gateway", "Passed")]) }), SituationSubsystem.Network);
        Assert.Equal("Data available", stale.Status);
        Assert.Equal(missing.Summary, stale.Summary);
        Assert.Contains("Network check results are out of date.", stale.Details);
        Assert.DoesNotContain(stale.Details, line => line.StartsWith("Network checks measured:", StringComparison.Ordinal));
        Assert.DoesNotContain(stale.Details, line => line.Contains("Gateway", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Gateway", "Succeeded", "Gateway responded")]
    [InlineData("Gateway", "Failed", "Gateway did not respond")]
    [InlineData("Gateway", "TimedOut", "Gateway check timed out")]
    [InlineData("Gateway", "Unavailable", "Gateway check unavailable")]
    [InlineData("InternetIp", "Succeeded", "Public IP responded")]
    [InlineData("InternetIp", "Failed", "Public IP did not respond")]
    [InlineData("InternetIp", "TimedOut", "Public IP check timed out")]
    [InlineData("InternetIp", "Unavailable", "Public IP check unavailable")]
    [InlineData("DnsResolution", "Succeeded", "DNS name resolved")]
    [InlineData("DnsResolution", "Failed", "DNS resolution failed")]
    [InlineData("DnsResolution", "TimedOut", "DNS resolution timed out")]
    [InlineData("DnsResolution", "Unavailable", "DNS resolution unavailable")]
    public void Network_probe_detail_distinguishes_probe_kind_and_result(
        string kind, string status, string expected)
    {
        var overview = Overview() with { NetworkChecks = new(Now, true, [new(kind, status)]) };
        var network = Node(Project(overview), SituationSubsystem.Network);

        Assert.Contains(expected, network.Details);
        Assert.DoesNotContain(network.Details, line => line.Contains("Unclassified diagnostic", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("en", "Gateway responded")]
    [InlineData("cs-CZ", "Brána odpovídá")]
    [InlineData("de", "Gateway antwortet")]
    public void Network_probe_labels_follow_selected_language(string language, string expected)
    {
        var overview = Overview() with { NetworkChecks = new(Now, true, [new("Gateway", "Succeeded")]) };
        var network = Node(Project(overview, language: language), SituationSubsystem.Network);

        Assert.Contains(expected, network.Details);
    }

    [Fact]
    public void Unknown_network_probe_values_remain_distinct_and_control_characters_are_removed()
    {
        var overview = Overview() with
        {
            NetworkChecks = new(Now, true,
            [
                new("Future\nProbe", "Future\tStatus"),
                new("OtherProbe", "OtherStatus")
            ])
        };
        var details = Node(Project(overview), SituationSubsystem.Network).Details;

        Assert.Contains("Unrecognized network check (FutureProbe) · Unrecognized result (FutureStatus)", details);
        Assert.Contains("Unrecognized network check (OtherProbe) · Unrecognized result (OtherStatus)", details);
        Assert.Equal(2, details.Count(line => line.Contains("Unrecognized network check", StringComparison.Ordinal)));
        Assert.Equal(2, details.Count(line => line.Contains("Unrecognized result", StringComparison.Ordinal)));
    }

    [Fact]
    public void Services_show_reported_inventory_with_no_implied_total_or_timestamp()
    {
        var empty = Node(Project(Overview()), SituationSubsystem.Services);
        Assert.Equal("Data unavailable", empty.Status);
        Assert.Empty(empty.Summary);

        var limited = Project(Overview() with
        {
            Services = [new("ExampleService", "Running")],
            Health = [new("process-services", "collector_partial", true)]
        });
        var node = Node(limited, SituationSubsystem.Services);
        Assert.Equal("Partial data", node.Status);
        Assert.Equal("Entries shown: 1", node.Summary);
        Assert.Contains("The service inventory has no separate measurement time.", node.Details);
        Assert.Contains(node.Details, line => line.StartsWith("ExampleService", StringComparison.Ordinal));
    }

    private static DashboardSnapshot Overview() =>
        new(Now, "Shadow", true, false, null, null, null, [], [], [], null, [], [], [], []);

    private static SituationOrbitSnapshot Project(DashboardSnapshot? snapshot, string connection = "connected",
        DateTimeOffset? lastOverviewUtc = null, string language = "en-US")
    {
        using var fixture = new DesktopLocalizationTests.PreferencesFixture();
        var localization = new DesktopLocalization(fixture.Preferences, CultureInfo.GetCultureInfo(language));
        return SituationOrbitSnapshot.Create(snapshot, connection, true, localization, Now, lastOverviewUtc);
    }

    private static SituationOrbitNode Node(SituationOrbitSnapshot snapshot, SituationSubsystem id) =>
        Assert.Single(snapshot.Nodes, node => node.Id == id);
}
