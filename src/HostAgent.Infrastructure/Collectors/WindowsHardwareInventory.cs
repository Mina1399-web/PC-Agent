using System.Buffers.Binary;
using System.Globalization;
using System.Management;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using HostAgent.Core.Models.Inventory;

namespace HostAgent.Infrastructure.Collectors;

public sealed class WindowsHardwareInventory(TimeProvider clock)
{
    public const int MaximumItems = 8;
    private const int MaximumTextLength = 64;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorAccessDenied = 5;
    private const int MaximumTopologyBytes = 1024 * 1024;

    internal delegate object?[][] ReadValues(string category, CancellationToken token);

    public HardwareInventoryCategory Read(string category, CancellationToken token) => Read(category, clock, ReadCategory, token);

    internal static HardwareInventoryCategory Read(string category, TimeProvider clock, ReadValues read, CancellationToken token)
    {
        var source = HardwareInventorySources.ForCategory(category) ?? throw new ArgumentException("inventory_category", nameof(category));
        token.ThrowIfCancellationRequested();
        try
        {
            var rows = read(category, token);
            token.ThrowIfCancellationRequested();
            var at = clock.GetUtcNow();
            var complete = rows.Length <= MaximumItems;
            var observations = rows.Take(MaximumItems).Select(row => Parse(category, row, source, at)).ToArray();
            var duplicateSlots = observations.Where(x => x.SlotId is not null).GroupBy(x => x.SlotId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
            for (var index = 0; index < observations.Length; index++)
            {
                var item = observations[index];
                if (item.SlotId is null || complete && !duplicateSlots.Contains(item.SlotId)) continue;
                observations[index] = item with
                {
                    SlotId = null,
                    SlotEvidence = new(source, at, "unknown", complete ? "slot_collision" : "enumeration_limit")
                };
            }
            var reason = !complete ? "enumeration_limit"
                : observations.Length == 0 ? "no_reported_items"
                : observations.Any(item => item.SlotEvidence.Status == "unknown" && IsSlotted(category)
                    || item.Fields.Any(field => field.Evidence.Status != "available")) ? "incomplete_sample"
                : null;
            return new(category, new(source, at, reason is null ? "available" : "partial", reason),
                Array.AsReadOnly(observations), []);
        }
        catch (Exception error) when (error is ManagementException or COMException or UnauthorizedAccessException
            or SecurityException or TimeoutException or InventoryReadException)
        {
            token.ThrowIfCancellationRequested();
            var reason = error switch
            {
                UnauthorizedAccessException or SecurityException => "access_denied",
                ManagementException { ErrorCode: ManagementStatus.AccessDenied } => "access_denied",
                ManagementException { ErrorCode: ManagementStatus.Timedout } or TimeoutException => "read_timeout",
                COMException { HResult: unchecked((int)0x80070005) or unchecked((int)0x80041003) } => "access_denied",
                InventoryReadException failure => failure.Message,
                _ => "collector_unavailable"
            };
            return new(category, new(source, clock.GetUtcNow(), "unavailable", reason), [], []);
        }
    }

    private static bool IsSlotted(string category) => category is "cpu" or "memory";

    private static InventoryItem Parse(string category, object?[] row, string source, DateTimeOffset at)
    {
        var observed = new InventoryEvidence(source, at, "available", null);
        var unknown = new InventoryEvidence(source, at, "unknown", "missing_or_placeholder");
        InventoryField Text(string name, int index)
        {
            var value = CleanText(row[index] as string);
            return new(name, value, null, value is null ? unknown : observed);
        }
        InventoryField Number(string name, int index)
        {
            var raw = row[index];
            var value = raw switch
            {
                byte number => number,
                ushort number => number,
                uint number => number,
                ulong number when number <= long.MaxValue => (long)number,
                int number => number,
                long number => number,
                _ => 0L
            };
            return value > 0 ? new(name, null, value, observed) : new(name, null, null, unknown);
        }

        var locator = category switch
        {
            "cpu" => CleanLocator(row[0] as string),
            "memory" => MemoryLocator(row[0] as string, row[1] as string),
            _ => null
        };
        var id = locator is null ? null :
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(category + ":" + locator))).ToLowerInvariant();
        InventoryField[] fields = category switch
        {
            "cpu" => [Text("name", 1), Text("manufacturer", 2), Number("wmi_core_count", 3)],
            "board" => [Text("product", 0), Text("manufacturer", 1), Text("version", 2)],
            "bios" => [Text("manufacturer", 0), Text("smbios_bios_version", 1)],
            "memory" => [Number("capacity_bytes", 2), Number("configured_clock_mhz_reported", 3), Number("smbios_type_code", 4)],
            "topology" => [Number("active_core_count", 0), Number("active_logical_processor_count", 1)],
            "firmware" => [Text("boot_firmware_type", 0)],
            _ => throw new InvalidOperationException("inventory_category")
        };
        return new(id, id is not null ? observed : unknown with { Reason = IsSlotted(category) ? "slot_unidentified" : "slot_not_applicable" },
            unknown with { Reason = "device_identity_not_collected" }, Array.AsReadOnly(fields));
    }

    private static string? CleanText(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Any(char.IsControl)) return null;
        if (text.ToUpperInvariant() is "UNKNOWN" or "NONE" or "N/A" or "NA" or "NOT SPECIFIED"
            or "TO BE FILLED BY O.E.M." or "TO BE FILLED BY OEM" or "DEFAULT STRING"
            or "SYSTEM PRODUCT NAME" or "SYSTEM MANUFACTURER" or "SYSTEM VERSION"
            or "BASE BOARD PRODUCT NAME") return null;
        return text.Length <= MaximumTextLength ? text : text[..MaximumTextLength];
    }

    private static string? CleanLocator(string? value)
    {
        if (value?.Trim().Length > MaximumTextLength) return null;
        var text = CleanText(value)?.ToUpperInvariant();
        if (text is null || !text.Any(char.IsLetterOrDigit) || text.All(c => c == '0')
            || text is "DEFAULT" or "NOT AVAILABLE" or "UNDEFINED" or "EMPTY"
            or "BASE BOARD" or "BASEBOARD" or "DIMM" or "BANK") return null;
        return text;
    }

    private static string? MemoryLocator(string? locator, string? bank)
    {
        var slot = CleanLocator(locator);
        if (slot is null) return null;
        var group = CleanLocator(bank) ?? string.Empty;
        // Lengths separate the two vendor-supplied components unambiguously.
        return slot.Length.ToString(CultureInfo.InvariantCulture) + ":" + slot + group;
    }

    private static object?[][] ReadCategory(string category, CancellationToken token) => category switch
    {
        "cpu" => Query("Win32_Processor", ["DeviceID", "Name", "Manufacturer", "NumberOfCores"], token),
        "board" => Query("Win32_BaseBoard", ["Product", "Manufacturer", "Version"], token),
        "bios" => Query("Win32_BIOS", ["Manufacturer", "SMBIOSBIOSVersion"], token),
        "memory" => Query("Win32_PhysicalMemory",
            ["DeviceLocator", "BankLabel", "Capacity", "ConfiguredClockSpeed", "SMBIOSMemoryType"], token),
        "topology" => [ReadTopology()],
        "firmware" => [ReadFirmware()],
        _ => throw new ArgumentException("inventory_category", nameof(category))
    };

    private static object?[][] Query(string className, string[] keys, CancellationToken token)
    {
        var options = new System.Management.EnumerationOptions
        {
            Timeout = TimeSpan.FromSeconds(2),
            ReturnImmediately = true,
            Rewindable = false,
            BlockSize = 1
        };
        using var search = new ManagementObjectSearcher(@"\\.\root\cimv2", "SELECT " + string.Join(", ", keys) + " FROM " + className, options);
        using var results = search.Get();
        var rows = new List<object?[]>(MaximumItems + 1);
        foreach (ManagementObject result in results)
        {
            using (result)
            {
                token.ThrowIfCancellationRequested();
                rows.Add(keys.Select(key => result[key]).ToArray());
            }
            if (rows.Count > MaximumItems) break;
        }
        return rows.ToArray();
    }

    private static object?[] ReadFirmware()
    {
        if (!GetFirmwareType(out var type)) throw NativeFailure();
        return [type switch { 1 => "bios", 2 => "uefi", _ => null }];
    }

    private static object?[] ReadTopology()
    {
        if (!Environment.Is64BitProcess) throw new InventoryReadException("topology_requires_64bit");
        uint size = 0;
        const int relationProcessorCore = 0;
        if (GetLogicalProcessorInformationEx(relationProcessorCore, null, ref size)
            || Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer) throw NativeFailure();
        if (size is < 48 or > MaximumTopologyBytes) throw new InventoryReadException("invalid_sample");
        var buffer = new byte[size];
        if (!GetLogicalProcessorInformationEx(relationProcessorCore, buffer, ref size)) throw NativeFailure();
        if (size > buffer.Length) throw new InventoryReadException("invalid_sample");
        var (cores, logical) = ParseTopology(buffer.AsSpan(0, checked((int)size)));
        return [cores, logical];
    }

    internal static (int Cores, int LogicalProcessors) ParseTopology(ReadOnlySpan<byte> records)
    {
        var cores = 0;
        var logical = 0;
        while (!records.IsEmpty)
        {
            // 8-byte EX header, 24-byte PROCESSOR_RELATIONSHIP prefix,
            // then one 16-byte GROUP_AFFINITY on a 64-bit process.
            if (records.Length < 48 || BinaryPrimitives.ReadInt32LittleEndian(records) != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(records[30..]) != 1)
                throw new InventoryReadException("invalid_sample");
            var size = BinaryPrimitives.ReadInt32LittleEndian(records[4..]);
            var count = BitOperations.PopCount(BinaryPrimitives.ReadUInt64LittleEndian(records[32..]));
            if (size < 48 || size > records.Length || count == 0) throw new InventoryReadException("invalid_sample");
            cores++;
            logical += count;
            records = records[size..];
        }
        if (cores == 0) throw new InventoryReadException("invalid_sample");
        return (cores, logical);
    }

    private static InventoryReadException NativeFailure() =>
        new(Marshal.GetLastPInvokeError() == ErrorAccessDenied ? "access_denied" : "collector_unavailable");

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, [Out] byte[]? buffer, ref uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFirmwareType(out int type);

    private sealed class InventoryReadException(string reason) : Exception(reason);
}
