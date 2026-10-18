using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HostAgent.Infrastructure.Collectors;

// Read only the trigger count. Never retain trigger data, registry values or
// request configuration-change rights from the service control manager.
internal static class ServiceTriggerQuery
{
    internal static bool? HasTriggers(string serviceName)
    {
        using var manager = OpenSCManagerW(null, null, 1); // SC_MANAGER_CONNECT
        if (manager.IsInvalid) return null;
        using var service = OpenServiceW(manager, serviceName, 1); // SERVICE_QUERY_CONFIG
        if (service.IsInvalid) return null;
        QueryServiceConfig2W(service, 8, IntPtr.Zero, 0, out var needed);
        // The documented API buffer limit is 8 KiB.
        if (Marshal.GetLastWin32Error() != 122 || needed < 4 || needed > 8192) return null;
        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            return QueryServiceConfig2W(service, 8, buffer, needed, out _)
                ? Marshal.ReadInt32(buffer) > 0 : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenSCManagerW(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenServiceW(ServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfig2W(ServiceHandle service, uint level, IntPtr buffer, uint size, out uint needed);
    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
