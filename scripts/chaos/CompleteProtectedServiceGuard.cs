using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

// DEV harness only. Fixed protected services are queried with QUERY_STATUS;
// no service mutation, process control, configuration write or name input.
public static class CompleteProtectedServiceGuard {
    public sealed class Snapshot {
        public string State {get;set;}
        public uint ProcessId {get;set;}
    }
    [StructLayout(LayoutKind.Sequential)] struct Status {
        public uint Type,State,Accepted,Win32Exit,ServiceExit,CheckPoint,WaitHint,ProcessId,Flags;
    }
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern IntPtr OpenSCManager(string machine,string database,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern IntPtr OpenService(IntPtr manager,string name,uint access);
    [DllImport("advapi32.dll",SetLastError=true)]
    static extern bool QueryServiceStatusEx(IntPtr service,int level,out Status status,int size,out int needed);
    [DllImport("advapi32.dll")]static extern bool CloseServiceHandle(IntPtr handle);
    public static Snapshot Read() { return ReadFixed("HostAgent","protected_service_"); }
    public static Snapshot ReadHelper() { return ReadFixed("HostAgent.Helper","protected_helper_"); }
    static Win32Exception NativeFailure(string stage) {
        var failure=new Win32Exception(Marshal.GetLastWin32Error());
        failure.Data["IsolatedScmNativeStage"]=stage;
        return failure;
    }
    static Snapshot ReadFixed(string name,string stagePrefix) {
        var manager=OpenSCManager(null,null,1);
        if(manager==IntPtr.Zero)throw NativeFailure(stagePrefix+"open_scm");
        try {
            var service=OpenService(manager,name,4);
            if(service==IntPtr.Zero)throw NativeFailure(stagePrefix+"open_service");
            try {
                Status status;int needed;
                if(!QueryServiceStatusEx(service,0,out status,Marshal.SizeOf(typeof(Status)),out needed))
                    throw NativeFailure(stagePrefix+"query_status");
                return new Snapshot {State=status.State==4 ? "Running" : "NotRunning",ProcessId=status.ProcessId};
            }finally{CloseServiceHandle(service);}
        }finally{CloseServiceHandle(manager);}
    }
}
