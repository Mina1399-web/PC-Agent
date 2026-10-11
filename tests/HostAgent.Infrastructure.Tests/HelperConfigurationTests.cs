using System.Text;
using HostAgent.Infrastructure.Security;
using HostAgent.Core.Models.Remediation;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class HelperConfigurationTests
{
    [Fact]
    public void Aliases_cannot_register_same_real_target_twice_and_bypass_target_limits()
    {
        Assert.Throws<ArgumentException>(() => new HelperAllowlist([
            new("alias-1", "FixtureService", [HelperOperation.StartService]),
            new("alias-2", "fixtureservice", [HelperOperation.StopService])], []));
        Assert.Throws<ArgumentException>(() => new HelperAllowlist([], [
            new("worker-1", @"C:\Trusted\worker.exe", new string('A', 64), true),
            new("worker-2", @"c:\trusted\worker.exe", new string('A', 64), true)]));
    }
    private const string Configuration = """{"readOnly":false,"shadow":false,"killSwitch":false,"executionTimeoutSeconds":15,"services":[{"id":"test-service","serviceName":"FixtureService","enabledOperations":["RestartService"]}],"processes":[],"approverSids":["S-1-5-21-123-456-789-1002"]}""";
    [Fact]
    public void Strict_configuration_enables_only_registered_capabilities_and_missing_configuration_is_closed()
    {
        var config = HelperConfiguration.Parse(Encoding.UTF8.GetBytes(Configuration));
        Assert.True(config.ConfigurationAvailable); Assert.False(config.ReadOnly); Assert.False(config.Shadow);
        Assert.Equal(HelperOperation.RestartService, Assert.Single(Assert.Single(config.Services).EnabledOperations));
        var unavailable = HelperConfiguration.LoadSafe(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        Assert.True(unavailable.ReadOnly); Assert.True(unavailable.Shadow);
        Assert.False(unavailable.ConfigurationAvailable); Assert.Empty(unavailable.Services); Assert.Empty(unavailable.ApproverSids);
        foreach (var invalid in new[] { "{}", Configuration.Replace("\"readOnly\":false", "\"readOnly\":false,\"readOnly\":true"),
            Configuration.Replace("\"killSwitch\":false", "\"killSwitch\":false,\"command\":\"anything\""),
            Configuration.Replace("\"RestartService\"", "2"), Configuration.Replace("RestartService", "FlushDns"),
            Configuration.Replace("\"executionTimeoutSeconds\":15", "\"executionTimeoutSeconds\":999"),
            Configuration.Replace("FixtureService", "cmd.exe /c"), Configuration.Replace("S-1-5-21-123-456-789-1002", "Everyone") })
            Assert.ThrowsAny<Exception>(() => HelperConfiguration.Parse(Encoding.UTF8.GetBytes(invalid)));
    }
}
