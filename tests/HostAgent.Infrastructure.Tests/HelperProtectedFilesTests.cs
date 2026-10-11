using System.Security.AccessControl;
using System.Security.Principal;
using HostAgent.Infrastructure.Security;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class HelperProtectedFilesTests
{
    [Fact]
    public void Helper_file_rejects_foreign_writers_and_untrusted_owner()
    {
        var path = Path.Combine(Path.GetTempPath(), "HostAgent-acl-" + Guid.NewGuid() + ".json");
        var sid = WindowsIdentity.GetCurrent().User!;
        try
        {
            File.WriteAllText(path, "{}");
            var acl = new FileSecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(sid);
            acl.AddAccessRule(new(sid, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(acl);
            HelperProtectedFiles.ValidateExisting(path, sid);
            acl.AddAccessRule(new(new SecurityIdentifier(Task3Fixture.CallerSid), FileSystemRights.Write, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(acl);
            Assert.Throws<UnauthorizedAccessException>(() => HelperProtectedFiles.ValidateExisting(path, sid));
            Assert.Throws<UnauthorizedAccessException>(() => HelperProtectedFiles.ValidateExisting(path,
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
