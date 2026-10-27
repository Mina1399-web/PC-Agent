using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;
using System.IO;

// FixtureTargets is generated at PRIVATE build time. No runtime target selection.
// This client never controls a service or kills Windows Installer processes.
// API contracts: learn.microsoft.com/windows/win32/api/msi/nf-msi-msisetexternaluiw
// and learn.microsoft.com/windows/win32/msi/-msiexecute-mutex.
public static class CounterReadFixturePolicy
{
    public static string DiskFailure(ulong availableBytes)
    {
        return availableBytes < 10UL * 1024 * 1024 * 1024 ? "disk_floor_10_gib" : null;
    }
    public static string Evaluate(double freeMiB, double seconds, bool cancelled)
    {
        if(Double.IsNaN(freeMiB)||Double.IsInfinity(freeMiB)||freeMiB<=512) return "hard_memory_floor_512";
        if(cancelled) return "cancel_requested";
        if(seconds>=90) return "msi_cooperative_deadline";
        return null;
    }
    public static string Warning(double freeMiB)
    {
        return freeMiB>512 && freeMiB<768 ? "soft_memory_floor_768" : null;
    }
}
public static class CounterReadMsiFixtureController
{
    static string failure;
    static int callbacks;
    static int lowMemoryWarning;
    static int intentionalDeferredFaults,intentionalCommitFaults;
    static readonly ManualResetEvent done=new ManualResetEvent(false);
    [UnmanagedFunctionPointer(CallingConvention.Winapi,CharSet=CharSet.Unicode)]
    delegate int UiCallback(IntPtr context,uint kind,string ignoredText);
    static int Ui(IntPtr context,uint kind,string ignoredText)
    {
        Interlocked.Increment(ref callbacks);
        if((kind&0xFF000000u)==0x04000000u)
        {
            if(String.Equals(ignoredText,"HostAgent.GuidFixture.Fault.Deferred.v1",StringComparison.Ordinal))
                Interlocked.Increment(ref intentionalDeferredFaults);
            else if(String.Equals(ignoredText,"HostAgent.GuidFixture.Fault.Commit.v1",StringComparison.Ordinal))
                Interlocked.Increment(ref intentionalCommitFaults);
        }
        // IDCANCEL is returned only for MSI progress messages. Only the two
        // fixed INFO literals above are recognized; raw text is never saved.
        // The delegate stays rooted through install.
        return (kind&0xFF000000u)==0x0A000000u && Volatile.Read(ref failure)!=null?2:1;
    }
    static string Hash(string path)
    {
        using(var stream=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read))
        using(var hash=SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","");
    }
    static string Quiescence()
    {
        try
        {
            using(var mutex=Mutex.OpenExisting(@"Global\_MSIExecute"))
            {
                try
                {
                    if(!mutex.WaitOne(0))return "busy";
                    mutex.ReleaseMutex(); return "observed_free";
                }
                catch(AbandonedMutexException) {mutex.ReleaseMutex();return "abandoned_unconfirmed";}
            }
        }
        catch(WaitHandleCannotBeOpenedException){return "observed_absent";}
        catch{return "access_or_probe_unconfirmed";}
    }
    public static bool ContainsMember()
    {
        IntPtr resume=IntPtr.Zero;
        int examined=0;
        do
        {
            IntPtr buffer;int count,total;
            int status=NetLocalGroupGetMembers(null,FixtureTargets.GroupName,0,out buffer,65536,out count,out total,ref resume);
            try
            {
                if(status!=0&&status!=234)throw new Win32Exception(status);
                examined+=count;if(count<0||examined>4096)throw new InvalidOperationException();
                for(int i=0;i<count;i++)
                    if(new SecurityIdentifier(Marshal.ReadIntPtr(buffer,i*IntPtr.Size)).Value==FixtureTargets.MemberSid)return true;
                if(status==0)return false;
                if(count==0)throw new InvalidOperationException();
            }
            finally {if(buffer!=IntPtr.Zero)NetApiBufferFree(buffer);}
        }while(true);
    }
    public static void Seed(bool add)
    {
        byte[] bytes=new byte[new SecurityIdentifier(FixtureTargets.MemberSid).BinaryLength];
        new SecurityIdentifier(FixtureTargets.MemberSid).GetBinaryForm(bytes,0);
        var pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);
        try
        {
            var info=new MemberInfo{Sid=pinned.AddrOfPinnedObject()};
            int code=add?NetLocalGroupAddMembers(null,FixtureTargets.GroupName,0,ref info,1):NetLocalGroupDelMembers(null,FixtureTargets.GroupName,0,ref info,1);
            if(code!=0&&code!=(add?1378:1377))throw new Win32Exception(code);
        }
        finally {pinned.Free();}
    }
    public static string Proof()
    {
        bool owned=false;string journal="absent",beforeMember="null",beforeOwned="null",afterMember="null",afterOwned="null";
        using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
        using(var key=machine.OpenSubKey(FixtureTargets.RegistryRoot,false))
        {
            if(key!=null)
            {
                foreach(var name in key.GetValueNames())if(name!="OwnedMember"&&name!="TransactionJournal")throw new InvalidOperationException();
                var owner=key.GetValue("OwnedMember",null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                if(owner!=null){if(!(owner is string)||(string)owner!=FixtureTargets.MemberSid)throw new InvalidOperationException();owned=true;}
                var raw=key.GetValue("TransactionJournal",null,RegistryValueOptions.DoNotExpandEnvironmentNames);
                if(raw!=null)
                {
                    var text=raw as string;if(text==null||text.Length>256)throw new InvalidOperationException();
                    var p=text.Split('|');Guid attempt;
                    if(p.Length!=9||p[0].Length!=33||(p[0][0]!='g'&&p[0][0]!='r')||!Guid.TryParseExact(p[0].Substring(1),"N",out attempt)||p[1]!=FixtureTargets.MemberSid||p[2]!="1")throw new InvalidOperationException();
                    for(int i=3;i<9;i++)if(p[i]!="0"&&p[i]!="1")throw new InvalidOperationException();
                    beforeMember=p[3]=="1"?"true":"false";beforeOwned=p[4]=="1"?"true":"false";
                    afterMember=p[5]=="1"?"true":"false";afterOwned=p[6]=="1"?"true":"false";
                    journal=p[7]=="1"&&p[8]=="1"?"committed":p[7]=="1"?"applied_pending":"intent_pending";
                }
            }
        }
        return "{\"member\":"+(ContainsMember()?"true":"false")+",\"owned\":"+(owned?"true":"false")+",\"journal\":\""+journal+"\",\"journalBeforeMember\":"+beforeMember+",\"journalBeforeOwned\":"+beforeOwned+",\"journalAfterMember\":"+afterMember+",\"journalAfterOwned\":"+afterOwned+"}";
    }
    static int Main(string[] args)
    {
        try
        {
            // Pure policy branch supports non-elevated, no-Windows-call unit tests.
            if(args.Length==4&&args[0]=="--policy")
            {
                double free=Double.Parse(args[1],CultureInfo.InvariantCulture),seconds=Double.Parse(args[2],CultureInfo.InvariantCulture);
                Console.WriteLine(CounterReadFixturePolicy.Evaluate(free,seconds,args[3]=="true")??"continue");return 0;
            }
            using(var identity=WindowsIdentity.GetCurrent())
                if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new InvalidOperationException();
            Guid id;
            if(!Guid.TryParseExact(FixtureTargets.RunId,"N",out id)||id==Guid.Empty||FixtureTargets.GroupName!="HostAgentFixture_"+FixtureTargets.RunId||FixtureTargets.MemberSid=="S-1-5-80-2080860389-2647237053-3465382480-4241646294-1393942697"||FixtureTargets.GroupSid=="S-1-5-32-558"||FixtureTargets.RegistryRoot!="SOFTWARE\\HostAgent.CounterReadFixture."+FixtureTargets.RunId)throw new InvalidOperationException();
            if(args.Length==1&&args[0]=="--proof"){Console.WriteLine(Proof());return 0;}
            if(args.Length==1&&(args[0]=="--seed-add"||args[0]=="--seed-remove"))
            {
                Seed(args[0]=="--seed-add");Console.WriteLine(Proof());return 0;
            }
            if(args.Length!=2||(args[0]!="install"&&args[0]!="uninstall")||
                (args[1]!="none"&&args[1]!="deferred_grant"&&args[1]!="deferred_release"&&args[1]!="commit_grant"))throw new InvalidOperationException();
            if(Hash(FixtureTargets.MsiPath)!=FixtureTargets.MsiSha256)throw new InvalidOperationException();
            string initial=Quiescence();
            if(initial!="observed_free"&&initial!="observed_absent")throw new InvalidOperationException();
            var started=System.Diagnostics.Stopwatch.StartNew();
            var guard=new Thread(delegate()
            {
                do
                {
                    var m=new MemoryStatus();m.Length=(uint)Marshal.SizeOf(typeof(MemoryStatus));
                    var cancel=File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(FixtureTargets.MsiPath)),"cancel.flag"));
                    var freeMiB=GlobalMemoryStatusEx(ref m)?m.AvailablePhysical/1048576d:Double.NaN;
                    if(CounterReadFixturePolicy.Warning(freeMiB)!=null)Interlocked.Exchange(ref lowMemoryWarning,1);
                    var reason=CounterReadFixturePolicy.Evaluate(freeMiB,started.Elapsed.TotalSeconds,cancel);
                    if(reason==null)
                    {
                        ulong available,total,space;
                        if(!GetDiskFreeSpaceEx("C:\\",out available,out total,out space))reason="disk_probe_failed";
                        else reason=CounterReadFixturePolicy.DiskFailure(available);
                    }
                    if(reason!=null)Interlocked.CompareExchange(ref failure,reason,null);
                }while(!done.WaitOne(250));
            });
            guard.IsBackground=true;guard.Start();
            UiCallback callback=Ui;uint oldLevel=MsiSetInternalUI(2,IntPtr.Zero);
            MsiSetExternalUI(callback,0x410u,IntPtr.Zero); // progress + fixed INFO receipts
            uint code;
            try
            {
                // Fixed property grammar only; paths/commands are not accepted.
                string properties="REBOOT=ReallySuppress FAIL_PHASE="+args[1]+(args[0]=="uninstall"?" REMOVE=ALL":"");
                code=MsiInstallProduct(FixtureTargets.MsiPath,properties);
            }
            finally
            {
                MsiSetExternalUI(null,0,IntPtr.Zero);MsiSetInternalUI(oldLevel,IntPtr.Zero);
                GC.KeepAlive(callback);done.Set();if(!guard.Join(3000))Interlocked.CompareExchange(ref failure,"monitor_shutdown_unconfirmed",null);
            }
            string quiet=Quiescence();bool confirmed=quiet=="observed_free"||quiet=="observed_absent";
            Console.WriteLine("{\"msiExitCode\":"+code+",\"quiescent\":"+(confirmed?"true":"false")+",\"quiescence\":\""+quiet+"\",\"uiMessageCount\":"+callbacks+",\"intentionalDeferredFaults\":"+intentionalDeferredFaults+",\"intentionalCommitFaults\":"+intentionalCommitFaults+",\"lowMemoryWarningObserved\":"+(lowMemoryWarning!=0?"true":"false")+",\"failure\":"+(failure==null?"null":"\""+failure+"\"")+",\"proof\":"+(confirmed?Proof():"null")+"}");
            return confirmed&&failure==null?0:3;
        }
        catch {Console.WriteLine("{\"failure\":\"fixture_controller_failed\",\"quiescent\":false}");return 4;}
    }
    [StructLayout(LayoutKind.Sequential)]struct MemberInfo{public IntPtr Sid;}
    [StructLayout(LayoutKind.Sequential)]struct MemoryStatus{public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,Extended;}
    [DllImport("kernel32.dll")]static extern bool GlobalMemoryStatusEx(ref MemoryStatus m);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool GetDiskFreeSpaceEx(string path,out ulong available,out ulong total,out ulong free);
    [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiInstallProductW")]static extern uint MsiInstallProduct(string path,string properties);
    [DllImport("msi.dll")]static extern uint MsiSetInternalUI(uint level,IntPtr owner);
    [DllImport("msi.dll",CharSet=CharSet.Unicode,EntryPoint="MsiSetExternalUIW")]static extern IntPtr MsiSetExternalUI(UiCallback callback,uint filter,IntPtr context);
    [DllImport("netapi32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int NetLocalGroupGetMembers(string server,string group,int level,out IntPtr buffer,int preferred,out int read,out int total,ref IntPtr resume);
    [DllImport("netapi32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int NetLocalGroupAddMembers(string server,string group,int level,ref MemberInfo info,int count);
    [DllImport("netapi32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int NetLocalGroupDelMembers(string server,string group,int level,ref MemberInfo info,int count);
    [DllImport("netapi32.dll")]static extern int NetApiBufferFree(IntPtr buffer);
}
