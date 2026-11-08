using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace HostAgent.Desktop;

internal enum WindowsTool
{
    ComputerManagement, TaskManager, Services, TaskScheduler, EventViewer, PerformanceMonitor,
    ResourceMonitor, SystemInformation, Settings, Explorer, DiskManagement, Storage, DeviceManager,
    Power, Network, AdvancedNetwork, Vpn, InstalledApps, StartupApps, OptionalFeatures, Accounts,
    SignIn, WindowsUpdate, UpdateHistory, WindowsSecurity, Recovery, Troubleshoot
}

internal enum WindowsToolGroup { System, Hardware, Network, AppsAndAccounts, UpdatesAndRecovery }

internal sealed record WindowsToolEntry(WindowsTool Tool, WindowsToolGroup Group)
{
    public string LabelKey => "WinTool" + Tool;
}

internal static class WindowsAdministration
{
    private enum Destination { Settings, SystemExecutable, WindowsExecutable, Console }
    private sealed record Target(WindowsToolEntry Entry, Destination Destination, string Name, bool Elevate = false);
    private static Target Settings(WindowsTool tool, WindowsToolGroup group, string page) => new(new(tool, group), Destination.Settings, page);
    private static Target SystemExe(WindowsTool tool, WindowsToolGroup group, string name) => new(new(tool, group), Destination.SystemExecutable, name);
    private static Target Console(WindowsTool tool, WindowsToolGroup group, string name, bool elevate) => new(new(tool, group), Destination.Console, name, elevate);

    private static readonly Target[] Targets =
    [
        Console(WindowsTool.ComputerManagement, WindowsToolGroup.System, "compmgmt.msc", true),
        SystemExe(WindowsTool.TaskManager, WindowsToolGroup.System, "Taskmgr.exe"),
        Console(WindowsTool.Services, WindowsToolGroup.System, "services.msc", true),
        Console(WindowsTool.TaskScheduler, WindowsToolGroup.System, "taskschd.msc", true),
        Console(WindowsTool.EventViewer, WindowsToolGroup.System, "eventvwr.msc", false),
        Console(WindowsTool.PerformanceMonitor, WindowsToolGroup.System, "perfmon.msc", false),
        SystemExe(WindowsTool.ResourceMonitor, WindowsToolGroup.System, "resmon.exe"),
        SystemExe(WindowsTool.SystemInformation, WindowsToolGroup.System, "msinfo32.exe"),
        Settings(WindowsTool.Settings, WindowsToolGroup.System, ""),
        new(new(WindowsTool.Explorer, WindowsToolGroup.Hardware), Destination.WindowsExecutable, "explorer.exe"),
        Console(WindowsTool.DiskManagement, WindowsToolGroup.Hardware, "diskmgmt.msc", true),
        Settings(WindowsTool.Storage, WindowsToolGroup.Hardware, "storagesense"),
        Console(WindowsTool.DeviceManager, WindowsToolGroup.Hardware, "devmgmt.msc", true),
        Settings(WindowsTool.Power, WindowsToolGroup.Hardware, "powersleep"),
        Settings(WindowsTool.Network, WindowsToolGroup.Network, "network-status"),
        Settings(WindowsTool.AdvancedNetwork, WindowsToolGroup.Network, "network-advancedsettings"),
        Settings(WindowsTool.Vpn, WindowsToolGroup.Network, "network-vpn"),
        Settings(WindowsTool.InstalledApps, WindowsToolGroup.AppsAndAccounts, "appsfeatures"),
        Settings(WindowsTool.StartupApps, WindowsToolGroup.AppsAndAccounts, "startupapps"),
        Settings(WindowsTool.OptionalFeatures, WindowsToolGroup.AppsAndAccounts, "optionalfeatures"),
        Settings(WindowsTool.Accounts, WindowsToolGroup.AppsAndAccounts, "otherusers"),
        Settings(WindowsTool.SignIn, WindowsToolGroup.AppsAndAccounts, "signinoptions"),
        Settings(WindowsTool.WindowsUpdate, WindowsToolGroup.UpdatesAndRecovery, "windowsupdate"),
        Settings(WindowsTool.UpdateHistory, WindowsToolGroup.UpdatesAndRecovery, "windowsupdate-history"),
        Settings(WindowsTool.WindowsSecurity, WindowsToolGroup.UpdatesAndRecovery, "windowsdefender"),
        Settings(WindowsTool.Recovery, WindowsToolGroup.UpdatesAndRecovery, "recovery"),
        Settings(WindowsTool.Troubleshoot, WindowsToolGroup.UpdatesAndRecovery, "troubleshoot")
    ];

    internal static IReadOnlyList<WindowsToolEntry> Entries { get; } = Array.AsReadOnly(Targets.Select(x => x.Entry).ToArray());
    private static readonly Dictionary<WindowsTool, Target> ByTool = Targets.ToDictionary(x => x.Entry.Tool);

    internal static bool RequestsElevation(WindowsTool tool) => ByTool[tool].Elevate;

    internal static ProcessStartInfo CreateStartInfo(WindowsTool tool)
    {
        if (!ByTool.TryGetValue(tool, out var target))
            throw new ArgumentOutOfRangeException(nameof(tool));

        if (target.Destination == Destination.Settings)
            return new("ms-settings:" + target.Name) { UseShellExecute = true };

        var directory = target.Destination == Destination.WindowsExecutable
            ? Environment.GetFolderPath(Environment.SpecialFolder.Windows) : Environment.SystemDirectory;
        var path = Path.Combine(directory, target.Destination == Destination.Console ? "mmc.exe" : target.Name);
        var start = new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = directory };
        if (target.Destination == Destination.Console)
            start.ArgumentList.Add(Path.Combine(directory, target.Name));
        if (target.Elevate)
            start.Verb = "runas";
        return start;
    }

    internal static void Open(WindowsTool tool)
    {
        var start = CreateStartInfo(tool);
        if (!start.FileName.StartsWith("ms-settings:", StringComparison.Ordinal))
        {
            if (!File.Exists(start.FileName) || start.ArgumentList.Any(path => !File.Exists(path)))
                throw new FileNotFoundException();
        }

        // Existing Settings/Explorer windows can handle the request without creating a process.
        using var process = Process.Start(start);
    }
}
