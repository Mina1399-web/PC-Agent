using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using HostAgent.Core.Models.Inventory;

namespace HostAgent.Infrastructure.Collectors;

/// <summary>Reads the display controllers reported by the local Windows installation.</summary>
public sealed class WindowsGpuInventory
{
    private const string Category = "gpu";
    private const string Source = "Win32_VideoController";
    private const int MaximumInstanceIdLength = 199;
    private const int MaximumNameLength = 64;

    internal const int MaximumAdapters = WindowsHardwareInventory.MaximumItems;

    private readonly TimeProvider clock;
    private readonly Func<AdapterRow[]> read;
    private readonly TimeSpan deadline;
    private int reading;

    public WindowsGpuInventory(TimeProvider clock)
        : this(clock, Query, TimeSpan.FromSeconds(3))
    {
    }

    internal WindowsGpuInventory(TimeProvider clock, Func<AdapterRow[]> read, TimeSpan deadline)
    {
        this.clock = clock;
        this.read = read;
        this.deadline = deadline;
    }

    public async Task<HardwareInventoryCategory> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref reading, 1, 0) != 0)
            return Unavailable("read_in_progress");

        // A timed-out caller must not start another query while WMI is still blocked.
        var work = Task.Run(ReadOnce);
        try
        {
            var result = await work.WaitAsync(deadline, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (TimeoutException)
        {
            return Unavailable("read_timeout");
        }
    }

    private HardwareInventoryCategory ReadOnce()
    {
        try
        {
            var rows = read();
            return Project(rows, clock.GetUtcNow());
        }
        catch (Exception error) when (error is ManagementException or COMException or
            UnauthorizedAccessException or SecurityException or TimeoutException)
        {
            var reason = error switch
            {
                UnauthorizedAccessException or SecurityException => "access_denied",
                ManagementException { ErrorCode: ManagementStatus.AccessDenied } => "access_denied",
                COMException { HResult: unchecked((int)0x80070005) or unchecked((int)0x80041003) } => "access_denied",
                ManagementException { ErrorCode: ManagementStatus.Timedout } or TimeoutException => "read_timeout",
                ManagementException { ErrorCode: ManagementStatus.InvalidClass } or
                COMException { HResult: unchecked((int)0x80041010) } => "source_unavailable",
                _ => "collector_unavailable"
            };

            return Unavailable(reason);
        }
        finally
        {
            Volatile.Write(ref reading, 0);
        }
    }

    private static HardwareInventoryCategory Project(AdapterRow[] rows, DateTimeOffset observedAt)
    {
        var truncated = rows.Length > MaximumAdapters;
        var observed = new InventoryEvidence(Source, observedAt, "available", null);
        var physicalIdentity = new InventoryEvidence(Source, observedAt, "unknown", "physical_identity_not_verified");
        var visible = rows.Take(MaximumAdapters)
            .Select(row => (Name: CleanName(row.Name), InstanceId: NormalizeInstanceId(row.InstanceId)))
            .ToArray();
        var duplicateIds = visible.Where(row => row.InstanceId is not null)
            .GroupBy(row => row.InstanceId!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        var items = new List<InventoryItem>(visible.Length);
        foreach (var row in visible)
        {
            var instanceId = row.InstanceId;
            var enumerator = instanceId is null ? null : instanceId[..instanceId.IndexOf('\\')];
            var locatorReason = truncated ? "enumeration_limit"
                : instanceId is null ? "instance_id_unidentified"
                : duplicateIds.Contains(instanceId) ? "instance_id_collision"
                : enumerator == "SWD" ? "software_device_identity_unstable"
                : null;

            // This opaque locator refers only to the reported Windows PnP instance.
            var slotId = instanceId is not null && locatorReason is null
                ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("gpu:pnp:" + instanceId)))
                : null;
            var slotEvidence = locatorReason is null
                ? observed
                : new InventoryEvidence(Source, observedAt, "unknown", locatorReason);
            var enumeration = enumerator switch
            {
                null => null,
                "SWD" => "software_enumerated",
                "ROOT" => "root_enumerated",
                "PCI" => "pci_enumerated",
                _ => "other_enumerator"
            };

            items.Add(new InventoryItem(slotId, slotEvidence, physicalIdentity,
                [ObservedText("name", row.Name, observed), ObservedText("pnp_enumeration", enumeration, observed)]));
        }

        var reason = truncated ? "enumeration_limit"
            : items.Count == 0 ? "no_reported_items"
            : items.Any(item => item.SlotId is null || item.Fields.Any(field => field.Text is null))
                ? "incomplete_sample"
                : null;

        return new(Category, new(Source, observedAt, reason is null ? "available" : "partial", reason), items.AsReadOnly(), []);
    }

    private static InventoryField ObservedText(string key, string? value, InventoryEvidence evidence) =>
        new(key, value, null, value is null ? evidence with { Status = "unknown", Reason = "missing_or_invalid" } : evidence);

    private HardwareInventoryCategory Unavailable(string reason) =>
        new(Category, new(Source, clock.GetUtcNow(), "unavailable", reason), [], []);

    private static string? NormalizeInstanceId(string? value)
    {
        if (value is null or { Length: 0 or > MaximumInstanceIdLength } ||
            value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
            return null;

        // A class ID or a display name alone does not uniquely identify a Windows devnode.
        var components = value.Split('\\');
        return components.Length == 3 && components.All(component => component.Any(char.IsAsciiLetterOrDigit))
            ? value.ToUpperInvariant()
            : null;
    }

    private static string? CleanName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name) || name.Any(char.IsControl))
            return null;

        if (name.Length <= MaximumNameLength)
            return name;

        var length = char.IsHighSurrogate(name[MaximumNameLength - 1]) ? MaximumNameLength - 1 : MaximumNameLength;
        return name[..length];
    }

    private static AdapterRow[] Query()
    {
        var options = new System.Management.EnumerationOptions
        {
            Timeout = TimeSpan.FromSeconds(2),
            ReturnImmediately = true,
            Rewindable = false,
            BlockSize = 1
        };
        using var search = new ManagementObjectSearcher(@"\\.\root\cimv2", "SELECT Name, PNPDeviceID FROM " + Source, options);
        using var results = search.Get();
        var rows = new List<AdapterRow>(MaximumAdapters + 1);
        foreach (ManagementObject result in results)
        {
            using (result)
            {
                rows.Add(new(result["Name"] as string, result["PNPDeviceID"] as string));
            }

            if (rows.Count > MaximumAdapters)
                break;
        }

        return rows.ToArray();
    }

    internal sealed record AdapterRow(string? Name, string? InstanceId);
}
