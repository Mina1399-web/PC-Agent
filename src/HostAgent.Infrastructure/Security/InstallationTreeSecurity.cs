using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
namespace HostAgent.Infrastructure.Security
{
    // Also compiled into the embedded .NET Framework MSI preflight action.
    public static class InstallationTreeSecurity
    {
        public static void Validate(string root, string[] trustedSids)
        {
            var trusted = new HashSet<string>(trustedSids, StringComparer.OrdinalIgnoreCase);
            var directory = new DirectoryInfo(Path.GetFullPath(root));
            // Reject junctions in every ancestor. Validate the nearest parent ACL before
            // accepting a missing directory, so an untrusted user cannot win creation.
            for (var ancestor = directory; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("installation_reparse_point");
            if (!directory.Exists)
            {
                var parent = directory.Parent;
                if (parent == null || !parent.Exists) throw new UnauthorizedAccessException("installation_parent_missing");
                ValidateObject(parent, trusted);
                return;
            }
            var pending = new Stack<FileSystemInfo>(); pending.Push(directory); var count = 0;
            while (pending.Count != 0)
            {
                var entry = pending.Pop();
                if (++count > 10000) throw new UnauthorizedAccessException("installation_tree_limit");
                ValidateObject(entry, trusted);
                var childDirectory = entry as DirectoryInfo;
                if (childDirectory != null)
                    foreach (var child in childDirectory.EnumerateFileSystemInfos()) pending.Push(child);
            }
            // The parent must not grant untrusted deletion/replacement of this tree.
            if (directory.Parent != null) ValidateObject(directory.Parent, trusted);
        }
        private static void ValidateObject(FileSystemInfo entry, HashSet<string> trusted)
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("installation_reparse_point");
            FileSystemSecurity security;
            var directory = entry as DirectoryInfo;
            if (directory != null) security = directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            else security = new FileInfo(entry.FullName).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner == null || !trusted.Contains(owner.Value)) throw new UnauthorizedAccessException("installation_untrusted_owner");
            const FileSystemRights mutation = FileSystemRights.Write | FileSystemRights.Delete |
                FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                var sid = ((SecurityIdentifier)rule.IdentityReference).Value;
                var inheritOnly = (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0;
                var reachesDescendants = directory != null && rule.InheritanceFlags != InheritanceFlags.None;
                // CreatorOwner is replaced by the trusted creator when MSI creates
                // an object. Other inherit-only mutation grants reach future payload.
                var trustedCreator = sid == "S-1-3-0" && inheritOnly;
                if (rule.AccessControlType == AccessControlType.Allow && (!inheritOnly || reachesDescendants) &&
                    !trusted.Contains(sid) && !trustedCreator && (rule.FileSystemRights & mutation) != 0)
                    throw new UnauthorizedAccessException("installation_untrusted_write");
            }
        }
    }
}
