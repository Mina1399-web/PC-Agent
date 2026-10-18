using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using System.ServiceProcess;

public static class IsolatedScmMemoryWatchdog
{
    private const string ServicePrefix = "HostAgentDevScm_";

    public static string? GetIsolatedStopTarget(double availableMiB, int floorMiB, string runId)
    {
        if (availableMiB >= floorMiB || floorMiB is < 512 or > 1024
            || !Guid.TryParseExact(runId, "N", out var id) || id == Guid.Empty) return null;
        return ServicePrefix + id.ToString("N");
    }

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 5 || !Guid.TryParseExact(args[1], "N", out var id)
            || !int.TryParse(args[4], out var floorMiB) || floorMiB is < 512 or > 1024
            || !IsDevVm()) return 2;

        var serviceName = ServicePrefix + id.ToString("N");
        var marker = Path.GetFullPath(args[2]);
        var stopSignal = Path.GetFullPath(args[3]);
        var evidenceRoot = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HostAgent-DEV-evidence"));
        if (!marker.StartsWith(evidenceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return 2;
        if (!stopSignal.StartsWith(evidenceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetDirectoryName(marker), Path.GetDirectoryName(stopSignal), StringComparison.OrdinalIgnoreCase)) return 2;

        while (true)
        {
            if (File.Exists(stopSignal)) return 0;
            var availableMiB = FreeMiB();
            var isolatedStopTarget = GetIsolatedStopTarget(availableMiB, floorMiB, id.ToString("N"));
            if (isolatedStopTarget is not null)
            {
                var startedAtUtc = DateTimeOffset.UtcNow;
                var initialRecord = new
                {
                    scenario = "isolated_scm_memory_guard",
                    runId = id.ToString("N"),
                    availableMemoryMiB = Math.Round(availableMiB, 1),
                    floorMiB,
                    action = "stop_isolated_test_service",
                    serviceName,
                    stopConfirmed = (bool?)null,
                    observedAtUtc = startedAtUtc
                };
                await WriteMarkerAsync(marker, initialRecord);
                var stop = TryStopExactTestService(isolatedStopTarget);
                var record = new
                {
                    scenario = "isolated_scm_memory_guard",
                    runId = id.ToString("N"),
                    availableMemoryMiB = Math.Round(availableMiB, 1),
                    floorMiB,
                    action = "stop_isolated_test_service",
                    serviceName,
                    stopConfirmed = stop,
                    observedAtUtc = startedAtUtc
                };
                await WriteMarkerAsync(marker, record);
                return stop ? 73 : 74;
            }
            await Task.Delay(250);
        }
    }

    private static async Task WriteMarkerAsync(string marker, object record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        var temporary = marker + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(record));
        File.Move(temporary, marker, overwrite: true);
    }

    private static bool TryStopExactTestService(string serviceName)
    {
        if (!serviceName.StartsWith(ServicePrefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(serviceName[ServicePrefix.Length..], "N", out _)
            || serviceName.Equals("HostAgent", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var service = new ServiceController(serviceName);
            if (service.Status == ServiceControllerStatus.Stopped) return true;
            if (service.Status != ServiceControllerStatus.StopPending) service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            return service.Status == ServiceControllerStatus.Stopped;
        }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (System.TimeoutException) { return false; }
    }

    private static bool IsDevVm()
    {
        using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS", false);
        try
        {
            using var additions = new ServiceController("VBoxService");
            return Environment.MachineName == "AGENTPC"
                && bios?.GetValue("SystemProductName") as string == "VirtualBox"
                && additions.Status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static double FreeMiB()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref memory) ? memory.AvailablePhysical / 1048576d : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
}
