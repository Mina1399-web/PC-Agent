using System.Text.Json.Serialization;

namespace HostAgent.Core.Models.Inventory;

public static class HardwareInventorySources
{
    public static string? ForCategory(string? category) => category switch
    {
        "cpu" => "Win32_Processor",
        "topology" => "GetLogicalProcessorInformationEx",
        "board" => "Win32_BaseBoard",
        "bios" => "Win32_BIOS",
        "firmware" => "GetFirmwareType",
        "memory" => "Win32_PhysicalMemory",
        _ => null
    };
}

public sealed record InventoryEvidence(string Source, DateTimeOffset? AtUtc, string Status, string? Reason);

// A field has a fixed name and either a bounded text or a numeric value.
public sealed record InventoryField(string Name, string? Text, long? Number, InventoryEvidence Evidence);

// SlotId identifies a reported location within this Windows installation. It is
// never a claim about the identity of the physical part occupying that location.
public sealed record InventoryItem(
    string? SlotId, InventoryEvidence SlotEvidence,
    InventoryEvidence DeviceIdentity, IReadOnlyList<InventoryField> Fields);

// Prior observations retain their original field timestamps. They never become
// current values simply because a later attempt failed or returned partial data.
public sealed record HardwareInventoryCategory(
    string Category, InventoryEvidence Evidence, IReadOnlyList<InventoryItem> Current,
    IReadOnlyList<InventoryItem> Prior)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PriorOmitted { get; init; }
}
