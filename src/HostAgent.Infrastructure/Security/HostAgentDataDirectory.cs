using System.Security.AccessControl;
using System.Security.Principal;

namespace HostAgent.Infrastructure.Security;

public static class HostAgentDataDirectory
{
    public static string EnsureSecure(string directoryPath, SecurityIdentifier serviceSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(serviceSid);

        var fullPath = Path.GetFullPath(directoryPath);
        var parentPath = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath))
        {
            throw new DirectoryNotFoundException("The parent of the Host Agent data directory must already exist.");
        }

        var directory = new DirectoryInfo(fullPath);
        if (!directory.Exists)
        {
            FileSystemAclExtensions.CreateDirectory(CreateSecurity(serviceSid), fullPath);
            directory.Refresh();
        }

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnauthorizedAccessException("The Host Agent data directory cannot be a reparse point.");
        }

        ValidateSecurity(directory, serviceSid);
        return fullPath;
    }

    private static DirectorySecurity CreateSecurity(SecurityIdentifier serviceSid)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddFullControlRule(security, serviceSid);
        AddFullControlRule(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        AddFullControlRule(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        return security;
    }

    private static void ValidateSecurity(DirectoryInfo directory, SecurityIdentifier serviceSid)
    {
        var security = directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (!security.AreAccessRulesProtected)
        {
            throw new UnauthorizedAccessException(
                "The Host Agent data directory has inherited access rules; provision it with the Host Agent ACL first.");
        }

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            serviceSid.Value,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value
        };
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        foreach (var rule in rules)
        {
            var sid = (SecurityIdentifier)rule.IdentityReference;
            var requiredInheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            if (rule.IsInherited
                || !expected.Contains(sid.Value)
                || rule.AccessControlType != AccessControlType.Allow
                || (rule.FileSystemRights & FileSystemRights.FullControl) != FileSystemRights.FullControl
                || (rule.InheritanceFlags & requiredInheritance) != requiredInheritance
                || rule.PropagationFlags != PropagationFlags.None)
            {
                throw new UnauthorizedAccessException(
                    "The Host Agent data directory contains an unexpected or insufficient access rule.");
            }
        }

        if (expected.Any(sid => !rules.Any(rule =>
                string.Equals(((SecurityIdentifier)rule.IdentityReference).Value, sid, StringComparison.OrdinalIgnoreCase))))
        {
            throw new UnauthorizedAccessException(
                "The Host Agent data directory ACL must grant access to the service, SYSTEM, and Administrators.");
        }

        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is null || !expected.Contains(owner.Value))
        {
            throw new UnauthorizedAccessException("The Host Agent data directory owner is not a trusted principal.");
        }
    }

    private static void AddFullControlRule(DirectorySecurity security, SecurityIdentifier sid) =>
        security.AddAccessRule(new FileSystemAccessRule(
            sid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
}
