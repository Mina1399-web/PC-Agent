using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class IsolatedScmStartupAclTests
{
    [Fact]
    public void Service_startup_checks_only_app_when_package_root_acl_is_inaccessible()
    {
        const string app = @"C:\isolated-scm-fixture\app";
        var sid = new SecurityIdentifier("S-1-5-80-1-2-3-4-5");
        var reads = new List<string>();
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(sid,
            FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        var canaryChecks = 0;
        var valid = false;
        var error = Record.Exception(() => valid = IsolatedScmServiceProbe.VerifyStartupAcl(app, sid,
            path =>
            {
                reads.Add(path);
                if (path != app) throw new UnauthorizedAccessException("fixture_root_acl_denied");
                return security;
            },
            (path, identity) =>
            {
                Assert.Equal(app, path);
                Assert.Equal(sid, identity);
                canaryChecks++;
                return true;
            }));
        Assert.Null(error);
        Assert.True(valid);
        Assert.Equal(new[] { app }, reads);
        Assert.Equal(1, canaryChecks);
    }

    [Fact]
    public void Inaccessible_app_acl_fails_closed_without_throwing_or_checking_canaries()
    {
        var sid = new SecurityIdentifier("S-1-5-80-1-2-3-4-5");
        var valid = true;
        var canaryChecks = 0;
        var error = Record.Exception(() => valid = IsolatedScmServiceProbe.VerifyStartupAcl(
            @"C:\isolated-scm-fixture\app", sid, _ => throw new UnauthorizedAccessException(),
            (_, _) => { canaryChecks++; return true; }));
        Assert.Null(error);
        Assert.False(valid);
        Assert.Equal(0, canaryChecks);
    }

    [Fact]
    public void Writable_app_acl_fails_closed_before_canary_checks()
    {
        const string app = @"C:\isolated-scm-fixture\app";
        var sid = new SecurityIdentifier("S-1-5-80-1-2-3-4-5");
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        var canaryChecks = 0;
        Assert.False(IsolatedScmServiceProbe.VerifyStartupAcl(app, sid,
            path => { Assert.Equal(app, path); return security; },
            (_, _) => { canaryChecks++; return true; }));
        Assert.Equal(0, canaryChecks);
    }

    [Fact]
    public void Failed_app_canaries_reject_startup()
    {
        const string app = @"C:\isolated-scm-fixture\app";
        var sid = new SecurityIdentifier("S-1-5-80-1-2-3-4-5");
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        var canaryChecks = 0;
        Assert.False(IsolatedScmServiceProbe.VerifyStartupAcl(app, sid,
            path => { Assert.Equal(app, path); return security; },
            (_, _) => { canaryChecks++; return false; }));
        Assert.Equal(1, canaryChecks);
    }
}
