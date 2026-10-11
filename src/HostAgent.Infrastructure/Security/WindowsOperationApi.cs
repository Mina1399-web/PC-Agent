using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HostAgent.Infrastructure.Security;

public sealed class WindowsOperationApi : IWindowsOperationApi
{
    public async ValueTask SetServiceRunningAsync(string serviceName, bool running, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var service = new ServiceController(serviceName, ".");
        service.Refresh();
        var desired = running ? ServiceControllerStatus.Running : ServiceControllerStatus.Stopped;
        if (service.Status == desired) return;
        cancellationToken.ThrowIfCancellationRequested();
        if (running) service.Start(); else service.Stop(stopDependentServices: false);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            service.Refresh();
            if (service.Status == desired) return;
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }
    public IProcessLease OpenProcess(int pid)
    {
        if (pid <= 4 || pid == Environment.ProcessId) throw new UnauthorizedAccessException("protected_process");
        return new NativeLease(pid);
    }
    private sealed class NativeLease : IProcessLease
    {
        private readonly SafeProcessHandle _handle;
        private readonly FileStream _image;
        public ProcessIdentity Identity { get; }
        public NativeLease(int pid)
        {
            _handle = Native.OpenProcess(0x00100000 | 0x1000 | 0x0001, false, pid);
            if (_handle.IsInvalid) { _handle.Dispose(); throw new UnauthorizedAccessException("process_access_denied"); }
            FileStream? image = null;
            try
            {
                if (!Native.GetProcessTimes(_handle, out var creation, out _, out _, out _) ||
                    !Native.IsProcessCritical(_handle, out var critical)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var buffer = new StringBuilder(32768); var length = buffer.Capacity;
                if (!Native.QueryFullProcessImageName(_handle, 0, buffer, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var path = Path.GetFullPath(buffer.ToString());
                image = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var digest = Convert.ToHexString(SHA256.HashData(image));
                Identity = new(pid, DateTime.FromFileTimeUtc(creation).Ticks, path, digest, critical);
                _image = image;
            }
            catch { image?.Dispose(); _handle.Dispose(); throw; }
        }
        public void Terminate()
        {
            // Recheck critical status immediately; use the same pinned kernel handle, never reopen a PID.
            if (!Native.IsProcessCritical(_handle, out var critical) || critical) throw new UnauthorizedAccessException("protected_process");
            if (!Native.TerminateProcess(_handle, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public void Dispose() { _image.Dispose(); _handle.Dispose(); }
    }
    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsProcessCritical(SafeProcessHandle process, [MarshalAs(UnmanagedType.Bool)] out bool critical);
        [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder imageName, ref int size);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    }
}
