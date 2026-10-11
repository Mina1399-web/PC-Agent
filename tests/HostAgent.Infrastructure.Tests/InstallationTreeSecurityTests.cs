using System.Security.AccessControl;
using System.Security.Principal;
using HostAgent.Infrastructure.Security;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class InstallationTreeSecurityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inherit_only_untrusted_write_cannot_reach_future_payload(bool missingChild)
    {
        var root=Path.Combine(Path.GetTempPath(),"install-inherit-"+Guid.NewGuid().ToString("N"));var sid=WindowsIdentity.GetCurrent().User!;
        HostAgentDataDirectory.EnsureSecure(root,sid);
        try
        {
            var acl=new DirectoryInfo(root).GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.InheritOnly,AccessControlType.Allow));
            new DirectoryInfo(root).SetAccessControl(acl);
            Assert.Throws<UnauthorizedAccessException>(()=>InstallationTreeSecurity.Validate(missingChild?Path.Combine(root,"HostAgent"):root,[sid.Value,"S-1-5-18","S-1-5-32-544"]));
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void Creator_owner_inherit_only_rule_remains_safe_with_trusted_creators()
    {
        var root=Path.Combine(Path.GetTempPath(),"install-creator-"+Guid.NewGuid().ToString("N"));var sid=WindowsIdentity.GetCurrent().User!;
        HostAgentDataDirectory.EnsureSecure(root,sid);
        try
        {
            var acl=new DirectoryInfo(root).GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-3-0"),FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.InheritOnly,AccessControlType.Allow));
            new DirectoryInfo(root).SetAccessControl(acl);
            InstallationTreeSecurity.Validate(Path.Combine(root,"HostAgent"),[sid.Value,"S-1-5-18","S-1-5-32-544"]);
            var child=Path.Combine(root,"Service");Directory.CreateDirectory(child);File.WriteAllText(Path.Combine(child,"synthetic.exe"),"never-executed");
            InstallationTreeSecurity.Validate(root,[sid.Value,"S-1-5-18","S-1-5-32-544"]);
        }
        finally{Directory.Delete(root,true);}
    }
    [Theory]
    [InlineData("root")]
    [InlineData("directory")]
    [InlineData("file")]
    public void Writable_existing_installation_is_refused_without_changing_files_or_acl(string scope)
    {
        var root=Path.Combine(Path.GetTempPath(),"install-tree-"+Guid.NewGuid().ToString("N"));
        var sid=WindowsIdentity.GetCurrent().User!;
        HostAgentDataDirectory.EnsureSecure(root,sid);
        try
        {
            var child=Path.Combine(root,"Service");Directory.CreateDirectory(child);
            var file=Path.Combine(child,"payload.exe");File.WriteAllText(file,"synthetic-never-executed");
            var target=scope=="root"?root:scope=="directory"?child:file;
            var users=new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null);
            FileSystemSecurity acl=scope=="file"?new FileInfo(target).GetAccessControl():new DirectoryInfo(target).GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(users,FileSystemRights.Modify,AccessControlType.Allow));
            if(scope=="file")new FileInfo(target).SetAccessControl((FileSecurity)acl);else new DirectoryInfo(target).SetAccessControl((DirectorySecurity)acl);
            // Windows canonicalizes inherited-control flags when persisting an ACL.
            var before=scope=="file"?new FileInfo(target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All):new DirectoryInfo(target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
            Assert.Throws<UnauthorizedAccessException>(()=>InstallationTreeSecurity.Validate(root,[sid.Value,"S-1-5-18","S-1-5-32-544"]));
            var after=scope=="file"?new FileInfo(target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All):new DirectoryInfo(target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
            Assert.Equal(before,after);Assert.Equal("synthetic-never-executed",File.ReadAllText(file));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void Protected_tree_accepts_read_only_users_and_validates_missing_child_against_parent()
    {
        var root=Path.Combine(Path.GetTempPath(),"install-tree-"+Guid.NewGuid().ToString("N"));var sid=WindowsIdentity.GetCurrent().User!;
        HostAgentDataDirectory.EnsureSecure(root,sid);
        try
        {
            var acl=new DirectoryInfo(root).GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.ReadAndExecute,AccessControlType.Allow));new DirectoryInfo(root).SetAccessControl(acl);
            InstallationTreeSecurity.Validate(root,[sid.Value,"S-1-5-18","S-1-5-32-544"]);
            InstallationTreeSecurity.Validate(Path.Combine(root,"missing"),[sid.Value,"S-1-5-18","S-1-5-32-544"]);
            Assert.False(Directory.Exists(Path.Combine(root,"missing")));
        }
        finally{Directory.Delete(root,true);}
    }
}
