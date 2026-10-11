using System.Security.Principal;
using System.Security.AccessControl;
namespace HostAgent.Infrastructure.Security;
public static class HelperProtectedFiles
{
    public static void ValidateExisting(string path, SecurityIdentifier helperSid)
    {
        if (!File.Exists(path)) return;
        var file = new FileInfo(path);
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("helper_file_reparse_point");
        var security = file.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        var trusted = new HashSet<string>(StringComparer.Ordinal) { helperSid.Value,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value };
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !trusted.Contains(owner.Value))
            throw new UnauthorizedAccessException("helper_file_untrusted_owner");
        foreach (var rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>())
            if (rule.AccessControlType == AccessControlType.Allow && !trusted.Contains(rule.IdentityReference.Value))
                throw new UnauthorizedAccessException("helper_file_untrusted_access");
    }
}
