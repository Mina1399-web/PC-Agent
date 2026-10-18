using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;

public static class CompleteProbeGuard
{
    private static string? resourceFailureCode;
    public static string? ResourceFailureCode => Volatile.Read(ref resourceFailureCode);
    private static void ResourceAbort(string code)
    { Interlocked.CompareExchange(ref resourceFailureCode,code,null); throw new InvalidOperationException(code); }
    public static Guid Parse(string text)
    {
        if(!Guid.TryParseExact(text,"N",out var id)||id==Guid.Empty||text!=id.ToString("N"))
            throw new ArgumentException("canonical_nonempty_guid_required");
        return id;
    }
    public static void ValidateVm(bool requireAdmin=false)
    {
        using var bios=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS",false);
        using var additions=new ServiceController("VBoxService");
        using var identity=WindowsIdentity.GetCurrent();
        if(Environment.MachineName!="AGENTPC" || bios?.GetValue("SystemProductName") as string!="VirtualBox"
            || additions.Status!=ServiceControllerStatus.Running) throw new InvalidOperationException("approved_vm_required");
        var uuidOk=false;
        object? locator=null, services=null, products=null;
        try
        {
            locator=Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator",true)!);
            services=((dynamic)locator!).ConnectServer(".",@"root\cimv2");
            // SWbemObjectSet's forward-only enumerator is not compatible with
            // the CLR IDispatch IEnumerable adapter on this Windows/.NET host.
            // Request an indexable result set and use ItemIndex explicitly.
            products=((dynamic)services).ExecQuery("SELECT UUID FROM Win32_ComputerSystemProduct","WQL",16);
            var productCount=(int)((dynamic)products).Count;
            for(var index=0;index<productCount;index++)
            {
                object? product=null, properties=null, uuidProperty=null;
                try
                {
                    product=((dynamic)products).ItemIndex(index);
                    properties=((dynamic)product).Properties_;
                    uuidProperty=((dynamic)properties).Item("UUID");
                    var uuid=(string?)((dynamic)uuidProperty).Value;
                    uuidOk |= string.Equals(uuid,"46543236-8C5E-49DA-953C-DB17F9B1E402",StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    foreach(var item in new[]{uuidProperty,properties,product})
                        if(item is not null&&Marshal.IsComObject(item))Marshal.FinalReleaseComObject(item);
                }
            }
        }
        finally { foreach(var item in new[]{products,services,locator})if(item is not null&&Marshal.IsComObject(item))Marshal.FinalReleaseComObject(item); }
        if(!uuidOk)throw new InvalidOperationException("approved_machine_uuid_required");
        var operatorAccount=identity.Name.Equals(@"AgentPC\AgentPCdevtest",StringComparison.OrdinalIgnoreCase);
        var testService=identity.Name.StartsWith(@"NT SERVICE\HostAgentDevScm_",StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(identity.Name["NT SERVICE\\HostAgentDevScm_".Length..],"N",out _);
        if(!operatorAccount && !testService)throw new InvalidOperationException("approved_test_identity_required");
        if(requireAdmin && (!operatorAccount || !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)))
            throw new InvalidOperationException("administrator_required");
        CheckLimits();
    }
    public static string NewDirectory(Guid id,string prefix)
    {
        if(id==Guid.Empty || prefix is not ("complete-storage" or "complete-soak" or "complete-wpf" or "complete-helper"))
            throw new ArgumentException("invalid_fixture_scope");
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HostAgent-DEV-evidence",prefix+"-"+id.ToString("N"));
        AssertPlainPath(root);
        if(Directory.Exists(root) || File.Exists(root))throw new InvalidOperationException("fixture_already_exists");
        Directory.CreateDirectory(root);AssertPlainPath(root);return root;
    }
    public static void AssertPlainPath(string path)
    {
        var current=Path.GetFullPath(path);
        while(current is not null)
        {
            if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                throw new InvalidOperationException("reparse_path_rejected");
            current=Path.GetDirectoryName(current);
        }
    }
    public static void CheckLimits()
    {
        var memory=FreeMiB();
        if(!double.IsFinite(memory)||memory<=512)ResourceAbort("hard_free_memory_floor_512_mib");
        if(memory<768)ResourceAbort("soft_free_memory_floor_768_mib");
        if(new DriveInfo("C:\\").AvailableFreeSpace<20L*1024*1024*1024)ResourceAbort("disk_free_below_20_gib");
    }
    public static double FreeMiB()
    {
        var status=new MemoryStatus {Length=(uint)Marshal.SizeOf<MemoryStatus>()};
        return GlobalMemoryStatusEx(ref status)?status.AvailablePhysical/1048576d:double.NaN;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length,Load;
        public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,Extended;
    }
    [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
