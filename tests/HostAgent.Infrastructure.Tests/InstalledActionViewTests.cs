using System.Text.Json;
using Xunit;
using HostAgent.Core.Dashboard;
using HostAgent.Infrastructure.Administration;

namespace HostAgent.Infrastructure.Tests;

public sealed class InstalledActionViewTests
{
    private const string Id = "aabbccddeeff00112233445566778899";
    private const string Name = "0HostAgentDevAcceptance_" + Id;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-27T10:00:00Z");

    private static JsonDocument Reply(string services, string mode = "Confirmation", bool readOnly = false,
        string generated = "2026-10-27T09:59:59Z") => JsonSerializer.SerializeToDocument(new
        {
            version = 1, ok = true, code = "overview",
            data = new { generatedAtUtc = generated, mode, readOnly,
                services = JsonSerializer.Deserialize<JsonElement>(services),
                processes = new[] { new { name = "do-not-export" } } }
        });

    [Fact]
    public void Readback_returns_only_the_exact_owned_service_from_a_fresh_writable_overview()
    {
        using var reply = Reply($$"""[{"name":"HostAgent","state":"Running"},{"name":"{{Name}}","state":"Running"}]""");
        var evidence = InstalledActionViewProbe.Read(reply.RootElement, Id, Now);
        Assert.Equal(Name, evidence.Name);
        Assert.Equal("Running", evidence.State);
        Assert.Equal(Now.AddSeconds(-1), evidence.GeneratedAtUtc);
        Assert.DoesNotContain("do-not-export", JsonSerializer.Serialize(evidence));
        Assert.DoesNotContain("HostAgent\"", JsonSerializer.Serialize(evidence));
    }

    [Theory]
    [InlineData("Shadow", true, "2026-10-27T09:59:59Z")]
    [InlineData("Confirmation", false, "2026-10-27T09:56:59Z")]
    [InlineData("Confirmation", false, "2026-10-27T10:00:01Z")]
    public void Readback_rejects_read_only_stale_or_future_evidence(string mode, bool readOnly, string generated)
    {
        using var reply = Reply($$"""[{"name":"{{Name}}","state":"Running"}]""", mode, readOnly, generated);
        Assert.Throws<InvalidDataException>(() => InstalledActionViewProbe.Read(reply.RootElement, Id, Now));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"name\":\"HostAgent\",\"state\":\"Running\"}]")]
    public void Readback_does_not_substitute_an_unrelated_service_for_the_owned_target(string services)
    {
        using var reply = Reply(services);
        Assert.Throws<InvalidDataException>(() => InstalledActionViewProbe.Read(reply.RootElement, Id, Now));
    }

    [Fact]
    public void Readback_rejects_duplicate_targets_and_noncanonical_run_ids()
    {
        using var reply = Reply($$"""[{"name":"{{Name}}","state":"Running"},{"name":"{{Name}}","state":"Stopped"}]""");
        Assert.Throws<InvalidDataException>(() => InstalledActionViewProbe.Read(reply.RootElement, Id, Now));
        Assert.Throws<InvalidDataException>(() => InstalledActionViewProbe.Read(reply.RootElement, Id.ToUpperInvariant(), Now));
    }

    [Fact]
    public void Readback_accepts_the_actual_product_dashboard_serialization_contract()
    {
        var snapshot = new DashboardSnapshot(Now, "Confirmation", false, false, null, null, null,
            [], [], [new ViewService(Name, "Running")], 0, [], [], [], []);
        using var response = JsonDocument.Parse(AdminProtocol.Reply(true, "overview", snapshot));
        Assert.Equal("Running", InstalledActionViewProbe.Read(response.RootElement, Id, Now).State);
    }
}
