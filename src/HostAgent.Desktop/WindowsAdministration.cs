using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using Microsoft.Win32;

namespace HostAgent.Desktop;

internal enum WindowsArea { Processes, Services, Disks, Network, Events, Applications, Startup }
internal sealed record WindowsInventoryRow(string Name, string Detail, string State, string Value,
    string? StateKey = null, int? ProcessId = null, long? ProcessStartTimeUtcTicks = null);
internal sealed record WindowsInventory(IReadOnlyList<WindowsInventoryRow> Rows, bool Partial = false);

internal static class WindowsAdministration
{
    internal static WindowsInventory Read(WindowsArea area) => area switch
    {
        WindowsArea.Processes => ReadProcesses(),
        WindowsArea.Services => ReadServices(),
        WindowsArea.Disks => ReadDisks(),
        WindowsArea.Network => new(NetworkInterface.GetAllNetworkInterfaces().Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderBy(n => n.Name).Select(n => new WindowsInventoryRow(n.Name, n.Description, n.OperationalStatus.ToString(), n.NetworkInterfaceType.ToString())).ToArray()),
        WindowsArea.Events => ReadEvents(),
        WindowsArea.Applications => ReadApplications(),
        WindowsArea.Startup => ReadStartupEntries(),
        _ => throw new ArgumentOutOfRangeException(nameof(area))
    };

    private static WindowsInventory ReadProcesses()
    {
        var rows = new List<WindowsInventoryRow>();
        var partial = false;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                int processId;
                try { processId = process.Id; }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception) { partial = true; continue; }
                var id = processId.ToString(CultureInfo.InvariantCulture);
                string name;
                try { name = process.ProcessName; }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception) { partial = true; continue; }
                string ram;
                try { ram = FormatMiB(process.WorkingSet64); }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception) { partial = true; ram = "—"; }
                long? startTicks;
                try { startTicks = process.StartTime.ToUniversalTime().Ticks; }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
                { partial = true; startTicks = null; }
                rows.Add(new(name, id, "", ram, ProcessId: processId, ProcessStartTimeUtcTicks: startTicks));
            }
        }
        return new(rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), partial);
    }

    private static WindowsInventory ReadServices()
    {
        var rows = new List<WindowsInventoryRow>();
        var partial = false;
        foreach (var service in ServiceController.GetServices())
        {
            using (service)
            {
                try { rows.Add(new(service.ServiceName, service.DisplayName, service.Status.ToString(), "")); }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception) { partial = true; }
            }
        }
        return new(rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), partial);
    }

    private static WindowsInventory ReadDisks()
    {
        var rows = new List<WindowsInventoryRow>();
        var partial = false;
        // Local fixed volumes only; reading mapped volumes can block on a remote host.
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            try
            {
                if (drive.IsReady)
                    rows.Add(new(drive.Name, drive.DriveFormat, "", $"{FormatMiB(drive.AvailableFreeSpace)} / {FormatMiB(drive.TotalSize)}"));
                else
                    rows.Add(new(drive.Name, "", "", "—", "WinInventoryUnavailable"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { partial = true; rows.Add(new(drive.Name, "", "", "—", "WinInventoryUnavailable")); }
        }
        return new(rows, partial);
    }

    private static WindowsInventory ReadEvents()
    {
        var query = new EventLogQuery("System", PathType.LogName,
            "*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= 86400000]]]")
        { ReverseDirection = true };
        using var reader = new EventLogReader(query);
        var rows = new List<WindowsInventoryRow>();
        while (rows.Count < 50)
        {
            using var entry = reader.ReadEvent();
            if (entry is null) break;
            rows.Add(new(entry.ProviderName ?? "—", entry.Id.ToString(CultureInfo.InvariantCulture), "",
                entry.TimeCreated?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "—",
                entry.Level switch { 1 => "WinInventoryCritical", 2 => "WinInventoryError", 3 => "WinInventoryWarning", _ => null }));
        }
        return new(rows);
    }

    private static WindowsInventory ReadApplications()
    {
        var rows = new List<WindowsInventoryRow>();
        var partial = false;
        foreach (var (hive, view) in new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default)
        })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var app = uninstall.OpenSubKey(subKeyName);
                        var displayName = app?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;
                        var publisher = app?.GetValue("Publisher") as string ?? "";
                        var version = app?.GetValue("DisplayVersion") as string ?? "";
                        rows.Add(new(displayName.Trim(), publisher.Trim(), "", version.Trim(), "WindowsApplicationsReadOnly"));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    { partial = true; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
            { partial = true; }
        }
        return new(rows.GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
            .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), partial);
    }

    private static WindowsInventory ReadStartupEntries()
    {
        var rows = new List<WindowsInventoryRow>();
        var partial = false;
        foreach (var (hive, view) in new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32)
        })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var run = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                if (run is null) continue;
                foreach (var name in run.GetValueNames())
                    if (!string.IsNullOrWhiteSpace(name))
                        rows.Add(new(name, hive == RegistryHive.CurrentUser ? "Current user" : "All users", "Enabled", "Run entry", "WindowsStartupReadOnly"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
            { partial = true; }
        }

        foreach (var folder in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder).Take(256))
                    rows.Add(new(Path.GetFileNameWithoutExtension(file), folder == Environment.GetFolderPath(Environment.SpecialFolder.Startup)
                        ? "Current user" : "All users", "Enabled", "Startup folder item", "WindowsStartupReadOnly"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { partial = true; }
        }
        return new(rows.GroupBy(row => row.Name + "\0" + row.Detail, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
            .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), partial);
    }

    private static string FormatMiB(long bytes) => (bytes / 1048576d).ToString("0.0", CultureInfo.CurrentCulture) + " MiB";
}
