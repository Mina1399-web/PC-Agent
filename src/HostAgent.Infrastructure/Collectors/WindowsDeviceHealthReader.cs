using System.Runtime.InteropServices;
using HostAgent.Core.Models.Inventory;
using Microsoft.Win32.SafeHandles;

namespace HostAgent.Infrastructure.Collectors;

public sealed class WindowsDeviceHealthReader(TimeProvider clock)
{
    internal const int DeviceLimit = 1024;
    internal const int FindingLimit = 32;
    private const uint HasProblem = 0x00000400;
    private const uint Disabled = 22;

    internal readonly record struct Status(Guid Class, uint Result, uint Flags, uint Problem);
    private sealed class EnumerationFailure(bool denied) : Exception { internal bool Denied { get; } = denied; }

    public DeviceHealthReport Read(CancellationToken token) => Project(ReadPresent(token), clock, token);

    internal static DeviceHealthReport Project(IEnumerable<Status> statuses, TimeProvider clock, CancellationToken token)
    {
        var findings = new List<DeviceHealthFinding>();
        var enumerated = 0;
        var checkedCount = 0;
        var problems = 0;
        var denied = 0;
        var unavailable = 0;
        string? reason = null;
        try
        {
            foreach (var device in statuses)
            {
                token.ThrowIfCancellationRequested();
                enumerated++;
                var kind = Classify(device);
                if (kind is "healthy" or "unhealthy" or "disabled") checkedCount++;
                else reason ??= kind == "access_denied" ? "access_denied" : "incomplete_sample";
                if (kind == "unhealthy") problems++;
                if (kind == "access_denied") denied++;
                if (kind == "unavailable") unavailable++;
                if (kind != "healthy")
                {
                    if (findings.Count < FindingLimit)
                        findings.Add(new(device.Class == Guid.Empty ? null : device.Class,
                            kind is "unhealthy" or "disabled" ? device.Problem : null, kind, kind));
                    else reason ??= "finding_limit";
                }
                if (enumerated == DeviceLimit) { reason = "enumeration_limit"; break; }
            }
        }
        catch (EnumerationFailure error)
        {
            reason = error.Denied ? "access_denied" : "collector_unavailable";
            if (error.Denied && enumerated == 0) denied++;
        }
        token.ThrowIfCancellationRequested();
        reason ??= enumerated == 0 ? "missing_sample" : null;
        var coverage = reason is null ? "available" : enumerated > 0 ? "partial"
            : reason == "missing_sample" ? "unknown" : "unavailable";
        var outcome = problems > 0 ? "unhealthy" : checkedCount == 0
            ? denied > 0 ? "access_denied" : (unavailable > 0 || reason == "collector_unavailable") ? "unavailable" : "unknown"
            : reason is null && findings.Count == 0 ? "healthy" : "unknown";
        return new(outcome, new(DeviceHealthReport.Source, clock.GetUtcNow(), coverage, reason), enumerated,
            checkedCount, findings.AsReadOnly());
    }

    private static string Classify(Status device) => device.Result switch
    {
        0 when (device.Flags & HasProblem) == 0 => "healthy",
        0 when device.Problem == Disabled => "disabled",
        0 when device.Problem > 0 => "unhealthy",
        0 => "unknown",
        0x33 => "access_denied",
        0x05 or 0x0D or 0x24 => "unavailable",
        _ => "unknown"
    };

    private static IEnumerable<Status> ReadPresent(CancellationToken token)
    {
        const uint presentAllClasses = 0x02 | 0x04;
        using var devices = SetupDiGetClassDevsW(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, presentAllClasses);
        if (devices.IsInvalid) throw new EnumerationFailure(Marshal.GetLastWin32Error() == 5);
        for (uint index = 0; index < DeviceLimit; index++)
        {
            token.ThrowIfCancellationRequested();
            var info = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
            if (!SetupDiEnumDeviceInfo(devices, index, ref info))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 259) yield break;
                throw new EnumerationFailure(error == 5);
            }
            var result = CM_Get_DevNode_Status(out var flags, out var problem, info.Instance, 0);
            yield return new(info.Class, result, flags, problem);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfo
    {
        public uint Size;
        public Guid Class;
        public uint Instance;
        public IntPtr Reserved;
    }

    private sealed class DeviceSetHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => SetupDiDestroyDeviceInfoList(handle);
    }

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    private static extern DeviceSetHandle SetupDiGetClassDevsW(IntPtr classGuid, IntPtr enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(DeviceSetHandle set, uint index, ref DeviceInfo info);
    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint instance, uint flags);
}
