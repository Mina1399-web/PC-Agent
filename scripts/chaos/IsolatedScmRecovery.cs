using System;
using System.Runtime.InteropServices;
// Operator-harness only. No arbitrary service name or reboot action is accepted.
public static class IsolatedScmRecovery {
    public static string Name(string run) {
        Guid id;
        if (!Guid.TryParseExact(run,"N",out id) || id == Guid.Empty || run != id.ToString("N"))
            throw new ArgumentException("invalid_isolated_recovery_id");
        return "HostAgentDevScm_" + run;
    }
    public static bool IsBounded(int[] types, int[] delays) {
        return types != null && delays != null && types.Length == 2 && delays.Length == 2
            && types[0] == 1 && delays[0] == 5000 && types[1] == 0 && delays[1] == 0;
    }
    [StructLayout(LayoutKind.Sequential)] struct Actions {
        public uint Reset; public IntPtr Reboot, Command; public uint Count; public IntPtr Items;
    }
    [StructLayout(LayoutKind.Sequential)] struct Action {public int Type; public uint Delay;}
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenSCManager(string machine,string database,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenService(IntPtr manager,string name,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ChangeServiceConfig2(IntPtr service,uint level,IntPtr info);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryServiceConfig2(IntPtr service,uint level,IntPtr buffer,uint size,out uint needed);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ChangeServiceConfig(IntPtr service,uint type,uint start,uint error,string path,string group,IntPtr tag,string deps,string account,string password,string display);
    [DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr handle);
    static Exception NativeFailure(string stage) {
        var failure=new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        failure.Data["IsolatedScmNativeStage"]=stage;
        return failure;
    }
    static IntPtr Open(string run, uint access, out IntPtr manager) {
        var name=Name(run); manager=OpenSCManager(null,null,1);
        if(manager==IntPtr.Zero) throw NativeFailure("open_scm");
        var service=OpenService(manager,name,access);
        if(service==IntPtr.Zero){var failure=NativeFailure("open_service");CloseServiceHandle(manager);throw failure;}
        return service;
    }
    static int[][] Read(IntPtr service) {
        uint needed;
        QueryServiceConfig2(service,2,IntPtr.Zero,0,out needed);
        if(needed==0 || needed>65536) throw new InvalidOperationException("isolated_scm_policy_size_invalid");
        var buffer=Marshal.AllocHGlobal((int)needed);
        try {
            if(!QueryServiceConfig2(service,2,buffer,needed,out needed)) throw NativeFailure("failure_policy_query");
            var data=(Actions)Marshal.PtrToStructure(buffer,typeof(Actions));
            if(data.Count>4 || data.Command!=IntPtr.Zero || data.Reboot!=IntPtr.Zero || data.Reset!=86400)
                throw new InvalidOperationException("isolated_scm_unexpected_failure_policy");
            var types=new int[data.Count];var delays=new int[data.Count];
            for(int i=0;i<data.Count;i++) {
                var action=(Action)Marshal.PtrToStructure(IntPtr.Add(data.Items,i*Marshal.SizeOf(typeof(Action))),typeof(Action));
                types[i]=action.Type;delays[i]=(int)action.Delay;
            }
            return new[]{types,delays};
        } finally {Marshal.FreeHGlobal(buffer);}
    }
    public static bool IsDisabled(string run) {
        IntPtr manager;var service=Open(run,1U,out manager);
        try {var policy=Read(service);return policy[0].Length==1 && policy[0][0]==0 && policy[1][0]==0;}
        finally{CloseServiceHandle(service);CloseServiceHandle(manager);}
    }
    public static void Set(string run, bool enable) {
        // SC_ACTION_RESTART requires SERVICE_START as well as QUERY/CHANGE_CONFIG.
        // Disabling recovery needs no start access. No service is started here.
        IntPtr manager;var service=Open(run,enable ? 19U : 3U,out manager);
        var items=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Action))*2);
        var buffer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Actions)));
        var flag=Marshal.AllocHGlobal(4);
        try {
            Marshal.StructureToPtr(new Action{Type=enable?1:0,Delay=enable?5000U:0},items,false);
            Marshal.StructureToPtr(new Action{Type=0,Delay=0},IntPtr.Add(items,Marshal.SizeOf(typeof(Action))),false);
            Marshal.StructureToPtr(new Actions{Reset=86400,Count=enable?2U:1U,Items=items},buffer,false);
            if(!ChangeServiceConfig2(service,2,buffer))throw NativeFailure("failure_policy_write");
            Marshal.WriteInt32(flag,0); // Crashes only; no recovery for clean service stops.
            if(!ChangeServiceConfig2(service,4,flag))throw NativeFailure("failure_flag_write");
            if(!enable && !ChangeServiceConfig(service,0xffffffff,4,0xffffffff,null,null,IntPtr.Zero,null,null,null,null))
                throw NativeFailure("disable_start");
            var policy=Read(service);
            if(enable ? !IsBounded(policy[0],policy[1]) : !(policy[0].Length==1 && policy[0][0]==0))
                throw new InvalidOperationException("isolated_scm_policy_readback_failed");
        } finally {Marshal.FreeHGlobal(flag);Marshal.FreeHGlobal(buffer);Marshal.FreeHGlobal(items);CloseServiceHandle(service);CloseServiceHandle(manager);}
    }
}
