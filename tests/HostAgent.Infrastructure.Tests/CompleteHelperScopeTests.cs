using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class CompleteHelperScopeTests
{
    [Fact]
    public void Only_exact_guid_service_virtual_account_and_closed_command_are_accepted()
    {
        var id = Guid.ParseExact("a91dfc8957ae429e9b0e9c5cadf9bb92", "N");
        var name = "HostAgentDevScm_" + id.ToString("N");
        var sid = "S-1-5-21-111-222-333-1001";
        var image = $"\"C:\\ProgramData\\HostAgent-DEV-SCM-{id:N}\\app\\HostAgent.PipeProbe.exe\" isolated-complete-target {id:N} {sid}";
        Assert.True(CompleteHelperScope.IsExpectedTarget(id, sid, name, image, "NT SERVICE\\" + name, 16));
        Assert.False(CompleteHelperScope.IsExpectedTarget(id, sid, "HostAgent", image, "NT SERVICE\\" + name, 16));
        Assert.False(CompleteHelperScope.IsExpectedTarget(id, sid, name, image + " arbitrary", "NT SERVICE\\" + name, 16));
        Assert.False(CompleteHelperScope.IsExpectedTarget(id, sid, name, image, "LocalSystem", 16));
        Assert.False(CompleteHelperScope.IsExpectedTarget(id, sid, name, image, "NT SERVICE\\" + name, 32));
        Assert.False(CompleteHelperScope.IsExpectedTarget(id, "S-1-5-21-111-222-333-1002", name, image, "NT SERVICE\\" + name, 16));
        Assert.False(CompleteHelperScope.IsExpectedTarget(id, sid, name, image.Replace("\\app\\", "\\data\\"), "NT SERVICE\\" + name, 16));
        Assert.False(CompleteHelperScope.IsExpectedTarget(Guid.NewGuid(), sid, name, image, "NT SERVICE\\" + name, 16));
        Assert.False(CompleteHelperScope.IsExpectedTarget(Guid.Empty, sid, name, image, "NT SERVICE\\" + name, 16));
    }
}
