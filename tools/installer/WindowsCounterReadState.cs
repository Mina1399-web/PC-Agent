using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
namespace HostAgent.Installer
{
    // Installer-private, fixed targets. No caller-selected account/group/key.
    internal sealed class WindowsCounterReadState : ICounterReadState, ICounterReadMembership, IDisposable
    {
        // Service SIDs are name-derived, not machine-account SIDs. Pinned for the
        // fixed SCM name HostAgent; validated against Windows before any Apply.
        const string MonitorSid="S-1-5-80-2080860389-2647237053-3465382480-4241646294-1393942697";
        readonly RegistryKey key;
        readonly SecurityIdentifier member;
        readonly string group;
        public WindowsCounterReadState()
        {
            using (var identity=WindowsIdentity.GetCurrent())
                if (!identity.IsSystem) throw new UnauthorizedAccessException("counter_read_requires_system");
            member=new SecurityIdentifier(MonitorSid);
            var translated=((NTAccount)new SecurityIdentifier("S-1-5-32-558").Translate(typeof(NTAccount))).Value;
            group=translated.Substring(translated.LastIndexOf('\\')+1);
            using (var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
            {
                const string path="SOFTWARE\\HostAgent.InstallerCounterRead";
                key=machine.OpenSubKey(path,true);
                if (key==null)
                {
                    var acl=new RegistrySecurity(); acl.SetAccessRuleProtection(true,false);
                    var system=new SecurityIdentifier("S-1-5-18"); acl.SetOwner(system);
                    foreach (var sid in new[]{system,new SecurityIdentifier("S-1-5-32-544")})
                        acl.AddAccessRule(new RegistryAccessRule(sid,RegistryRights.FullControl,InheritanceFlags.ContainerInherit,PropagationFlags.None,AccessControlType.Allow));
                    key=machine.CreateSubKey(path,RegistryKeyPermissionCheck.ReadWriteSubTree,RegistryOptions.None,acl);
                }
            }
            try { ValidateAcl(key.GetAccessControl()); }
            catch { key.Dispose(); throw; }
        }
        public void ValidateRegisteredMember()
        {
            var registered=(SecurityIdentifier)new NTAccount("NT SERVICE","HostAgent").Translate(typeof(SecurityIdentifier));
            if(!member.Equals(registered))throw new InvalidOperationException("counter_read_service_identity_mismatch");
        }
        static void ValidateAcl(RegistrySecurity acl)
        {
            var owner=((SecurityIdentifier)acl.GetOwner(typeof(SecurityIdentifier))).Value;
            if (owner!="S-1-5-18" && owner!="S-1-5-32-544") throw new UnauthorizedAccessException("counter_read_state_owner");
            var writes=RegistryRights.SetValue|RegistryRights.CreateSubKey|RegistryRights.Delete|RegistryRights.ChangePermissions|RegistryRights.TakeOwnership;
            foreach (RegistryAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier)))
                if (rule.AccessControlType==AccessControlType.Allow && (rule.RegistryRights&writes)!=0 &&
                    rule.IdentityReference.Value!="S-1-5-18" && rule.IdentityReference.Value!="S-1-5-32-544")
                    throw new UnauthorizedAccessException("counter_read_state_writable");
        }
        public bool ReadOwned()
        {
            var value=key.GetValue("OwnedMember",null,RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value==null) return false;
            if (!(value is string) || (string)value!=member.Value) throw new InvalidOperationException("counter_read_ownership_invalid");
            return true;
        }
        public bool HasOtherJournal(string id)
        {
            CounterReadTransaction.ValidateId(id);
            foreach(var name in key.GetValueNames())
                if(name!="OwnedMember" && name!="TransactionJournal")throw new InvalidOperationException("counter_read_state_unknown");
            string previousId; var previous=ReadStored(out previousId);
            return previous!=null && previousId!=id && (!previous.Applied || !previous.Committed);
        }
        public void WriteOwned(bool owned)
        {
            if (owned) key.SetValue("OwnedMember",member.Value,RegistryValueKind.String);
            else key.DeleteValue("OwnedMember",false);
            key.Flush();
        }
        public CounterReadJournal ReadJournal(string id)
        {
            CounterReadTransaction.ValidateId(id);
            string previousId; var previous=ReadStored(out previousId);
            return previousId==id?previous:null;
        }
        CounterReadJournal ReadStored(out string id)
        {
            id=null;
            var value=key.GetValue("TransactionJournal",null,RegistryValueOptions.DoNotExpandEnvironmentNames);
            if(value==null)return null;
            var text=value as string;
            if(text==null)throw new InvalidOperationException("counter_read_journal_invalid");
            return CounterReadJournalCodec.Decode(text,member.Value,out id);
        }
        public void WriteJournal(string id,CounterReadJournal journal,bool create)
        {
            CounterReadTransaction.ValidateId(id);
            string previousId; var previous=ReadStored(out previousId);
            if(create && previous!=null && (previousId==id || !previous.Applied || !previous.Committed))throw new InvalidOperationException("counter_read_journal_exists");
            if(!create && (previous==null || previousId!=id))throw new InvalidOperationException("counter_read_journal_missing");
            // One bounded slot: only a subsequent serialized MSI attempt may
            // replace a completed older attempt. Current rollback keeps its ID.
            key.SetValue("TransactionJournal",CounterReadJournalCodec.Encode(id,member.Value,journal),RegistryValueKind.String);
            key.Flush();
        }
        public void DeleteJournal(string id)
        {
            CounterReadTransaction.ValidateId(id);
            string previousId; ReadStored(out previousId);
            if(previousId==id){key.DeleteValue("TransactionJournal",false);key.Flush();}
        }
        public bool Contains()
        {
            IntPtr resume=IntPtr.Zero; var examined=0;
            do
            {
                IntPtr buffer; int read,total;
                var result=NetLocalGroupGetMembers(null,group,0,out buffer,65536,out read,out total,ref resume);
                try
                {
                    if(result!=0 && result!=234)throw new Win32Exception(result);
                    examined+=read; if(read<0 || examined>4096)throw new InvalidOperationException("counter_read_membership_limit");
                    for(var i=0;i<read;i++) if(member.Equals(new SecurityIdentifier(Marshal.ReadIntPtr(buffer,i*IntPtr.Size))))return true;
                    if(result==0)return false;
                    if(read==0)throw new InvalidOperationException("counter_read_membership_progress");
                }
                finally {if(buffer!=IntPtr.Zero)NetApiBufferFree(buffer);}
            }while(true);
        }
        public bool Add() {return Change(true);}
        public bool Remove() {return Change(false);}
        bool Change(bool add)
        {
            var bytes=new byte[member.BinaryLength]; member.GetBinaryForm(bytes,0);
            var pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);
            try
            {
                var info=new MemberInfo{Sid=pinned.AddrOfPinnedObject()};
                var result=add?NetLocalGroupAddMembers(null,group,0,ref info,1):NetLocalGroupDelMembers(null,group,0,ref info,1);
                if(result==0)return true;
                if(result==(add?1378:1377))return false;
                throw new Win32Exception(result);
            }
            finally {pinned.Free();}
        }
        public void Dispose() {key.Dispose();}
        [StructLayout(LayoutKind.Sequential)] struct MemberInfo {public IntPtr Sid;}
        [DllImport("netapi32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] static extern int NetLocalGroupGetMembers(string server,string group,int level,out IntPtr buffer,int preferred,out int read,out int total,ref IntPtr resume);
        [DllImport("netapi32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] static extern int NetLocalGroupAddMembers(string server,string group,int level,ref MemberInfo member,int entries);
        [DllImport("netapi32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] static extern int NetLocalGroupDelMembers(string server,string group,int level,ref MemberInfo member,int entries);
        [DllImport("netapi32.dll",ExactSpelling=true)] static extern int NetApiBufferFree(IntPtr buffer);
    }
}
