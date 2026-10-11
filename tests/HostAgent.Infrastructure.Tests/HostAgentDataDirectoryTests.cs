using System.Security.AccessControl;
using System.Security.Principal;
using HostAgent.Infrastructure.Security;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class HostAgentDataDirectoryTests
{
    [Fact]
    public void Creates_directory_with_protected_service_system_and_administrator_acl()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var currentSid = Assert.IsType<SecurityIdentifier>(identity.User);
        var directoryPath = CreateDirectoryPath();

        try
        {
            var actualPath = HostAgentDataDirectory.EnsureSecure(directoryPath, currentSid);
            var info = new DirectoryInfo(actualPath);
            var security = info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToArray();
            var principals = rules.Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value).ToHashSet();

            Assert.True(security.AreAccessRulesProtected);
            Assert.Contains(currentSid.Value, principals);
            Assert.Contains(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value, principals);
            Assert.Contains(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value, principals);
            Assert.DoesNotContain(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null).Value,
                principals);
            Assert.Equal(currentSid, security.GetOwner(typeof(SecurityIdentifier)));
            Assert.False((info.Attributes & FileAttributes.ReparsePoint) != 0);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath);
            }
        }
    }

    [Fact]
    public void Rejects_an_existing_directory_with_inherited_access_rules()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var currentSid = Assert.IsType<SecurityIdentifier>(identity.User);
        var directoryPath = CreateDirectoryPath();
        Directory.CreateDirectory(directoryPath);

        try
        {
            Assert.Throws<UnauthorizedAccessException>(
                () => HostAgentDataDirectory.EnsureSecure(directoryPath, currentSid));
        }
        finally
        {
            Directory.Delete(directoryPath);
        }
    }

    [Fact]
    public void Rejects_acl_rules_that_apply_only_to_child_files_and_directories()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var currentSid = Assert.IsType<SecurityIdentifier>(identity.User);
        var directoryPath = CreateDirectoryPath();
        Directory.CreateDirectory(directoryPath);
        var directory = new DirectoryInfo(directoryPath);
        var originalSecurity = directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);

        try
        {
            var security = directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var childOnly = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(
                currentSid,
                FileSystemRights.FullControl,
                childOnly,
                PropagationFlags.None,
                AccessControlType.Allow));
            foreach (var sid in new[]
            {
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
            })
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    sid,
                    FileSystemRights.FullControl,
                    childOnly,
                    PropagationFlags.InheritOnly,
                    AccessControlType.Allow));
            }

            directory.SetAccessControl(security);
            Assert.Throws<UnauthorizedAccessException>(
                () => HostAgentDataDirectory.EnsureSecure(directoryPath, currentSid));
        }
        finally
        {
            directory.SetAccessControl(originalSecurity);
            Directory.Delete(directoryPath);
        }
    }

    private static string CreateDirectoryPath() =>
        Path.Combine(Path.GetTempPath(), $"host-agent-acl-{Guid.NewGuid():N}");
}
