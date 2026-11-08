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

namespace HostAgent.Desktop;

internal enum WindowsArea { Processes, Services, Disks, Network, Events }
internal sealed record WindowsInventoryRow(string Name, string Detail, string State, string Value, string? StateKey = null);
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
                var id = process.Id.ToString(CultureInfo.InvariantCulture);
                string name;
                try { name = process.ProcessName; }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception) { partial = true; continue; }
                string ram;
                try { ram = FormatMiB(process.WorkingSet64); }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception) { partial = true; ram = "—"; }
                rows.Add(new(name, id, "", ram));
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

    private static string FormatMiB(long bytes) => (bytes / 1048576d).ToString("0.0", CultureInfo.CurrentCulture) + " MiB";
}
