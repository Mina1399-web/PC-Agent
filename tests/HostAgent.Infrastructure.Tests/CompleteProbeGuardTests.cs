using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class CompleteProbeGuardTests
{
    [Theory]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("{00112233-4455-6677-8899-aabbccddeeff}")]
    [InlineData("00112233445566778899AABBCCDDEEFF")]
    public void Only_nonempty_canonical_lowercase_scope_can_reach_live_test_host(string text)
        => Assert.Throws<ArgumentException>(()=>CompleteProbeGuard.Parse(text));
}
