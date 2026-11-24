using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using HostAgent.Core.Models.Inventory;
using HostAgent.Infrastructure.Collectors;

namespace HostAgent.Desktop;

public partial class MainWindow
{
    private static readonly (string Id, string Label)[] InventoryCategories =
    [
        ("cpu", "Processor"), ("topology", "HardwareInventoryTopology"),
        ("board", "HardwareInventoryBoard"), ("bios", "HardwareInventoryBios"),
        ("firmware", "HardwareInventoryFirmware"), ("memory", "RAM")
    ];

    private readonly Dictionary<string, HardwareInventoryCategory?> _hardwareInventory = new();
    private bool _hardwareInventoryLoading;

    private async void ReadHardwareInventoryClick(object sender, RoutedEventArgs e)
        => await LoadHardwareInventory();

    private async Task LoadHardwareInventory()
    {
        // Leave the button focusable while reading so keyboard focus does not move.
        if (_hardwareInventoryLoading || _closing.IsCancellationRequested) return;
        _hardwareInventoryLoading = true;
        RenderHardwareInventory();
        try
        {
            var results = new Dictionary<string, HardwareInventoryCategory?>();
            foreach (var (category, _) in InventoryCategories)
            {
                _closing.Token.ThrowIfCancellationRequested();
                var result = await _controller.ReadAsync<HardwareInventoryCategory>(
                    "hardware-inventory", new { category }, _closing.Token);
                results[category] = IsInventoryResponse(category, result) ? result : null;
            }
            _closing.Token.ThrowIfCancellationRequested();
            foreach (var pair in results) _hardwareInventory[pair.Key] = pair.Value;
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        finally
        {
            _hardwareInventoryLoading = false;
            if (!_closing.IsCancellationRequested) RenderHardwareInventory();
        }
    }

    private static bool IsInventoryResponse(string category, HardwareInventoryCategory? response)
        => response is { Evidence: not null,
            Current.Count: <= WindowsHardwareInventory.MaximumItems,
            Prior.Count: <= WindowsHardwareInventory.MaximumItems }
            && response.Category == category
            && response.Evidence.Source == HardwareInventorySources.ForCategory(category)
            && response.PriorOmitted >= 0
            && response.PriorOmitted <= WindowsHardwareInventory.MaximumItems - response.Prior.Count
            && response.Current.All(HasInventoryFields)
            && response.Prior.All(HasInventoryFields);

    private static bool HasInventoryFields(InventoryItem? item)
        => item is { Fields.Count: <= 3, SlotEvidence: not null } &&
            item.Fields.All(field => field is { Evidence: not null });

    private void RenderHardwareInventory()
    {
        if (HardwareInventoryText is null) return;
        var now = DateTimeOffset.UtcNow;
        var lines = new List<string>();
        if (_hardwareInventoryLoading) lines.Add(T("HardwareInventoryLoading"));
        else if (_hardwareInventory.Count == 0) lines.Add(T("HardwareInventoryPrompt"));
        else
        {
            foreach (var (category, label) in InventoryCategories)
            {
                if (lines.Count > 0) lines.Add("");
                lines.Add(T(label));
                if (_hardwareInventory[category] is not { } report)
                {
                    lines.Add(T("DataTrustUnavailable"));
                    continue;
                }

                var evidence = CurrentInventoryEvidence(report.Evidence, now);
                lines.Add(DescribeInventoryEvidence(evidence));
                if (evidence.Status is "available" or "partial")
                    AppendInventoryItems(lines, report.Current, report.Evidence, now, historical: false);
                if (report.Prior.Count > 0 || evidence.Status == "stale" && report.Current.Count > 0
                    || report.PriorOmitted > 0)
                {
                    lines.Add(T("HardwareInventoryPrior"));
                    if (evidence.Status == "stale")
                        AppendInventoryItems(lines, report.Current, report.Evidence, now, historical: true);
                    AppendInventoryItems(lines, report.Prior, report.Evidence, now, historical: true);
                    if (report.PriorOmitted > 0) lines.Add(T("HardwareInventoryPriorOmitted", report.PriorOmitted));
                }
            }
        }
        var text = string.Join(Environment.NewLine, lines);
        if (HardwareInventoryText.Text != text) HardwareInventoryText.Text = text;
    }

    // Match the inventory cache window, which is longer than live telemetry freshness.
    private static InventoryEvidence CurrentInventoryEvidence(InventoryEvidence evidence, DateTimeOffset now)
    {
        if (evidence.Status is not ("available" or "partial")) return evidence;
        if (evidence.AtUtc is not { } at || at > now)
            return evidence with { Status = "unknown", Reason = "invalid_sample_time" };
        return now - at > TimeSpan.FromHours(2)
            ? evidence with { Status = "stale", Reason = "sample_expired" }
            : evidence;
    }

    private string DescribeInventoryEvidence(InventoryEvidence evidence)
    {
        var reasonKey = evidence.Reason switch
        {
            "waiting_for_sample" => "HardwareInventoryWaiting",
            "no_reported_items" => "HardwareInventoryNoItems",
            "enumeration_limit" => "HardwareInventoryLimit",
            "missing_or_placeholder" => "HardwareInventoryMissingField",
            "invalid_sample_time" => "HardwareInventoryInvalidTime",
            "topology_requires_64bit" => "HardwareInventoryTopologyNeeds64Bit",
            "slot_collision" => "HardwareInventorySlotCollision",
            "slot_unidentified" => "HardwareInventorySlotUnidentified",
            "read_timeout" => "NetDetailTimeout",
            _ => null
        };
        var description = DataTrustDisplay.Describe(Localization,
            new(evidence.Source, evidence.AtUtc, evidence.Status, reasonKey is null ? evidence.Reason : null));
        return reasonKey is null ? description : T(reasonKey) + Environment.NewLine + description;
    }

    private void AppendInventoryItems(List<string> lines, IReadOnlyList<InventoryItem> items,
        InventoryEvidence categoryEvidence, DateTimeOffset now, bool historical)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            lines.Add(T("HardwareInventoryEntry", i + 1));
            if (item.SlotEvidence.Reason is "slot_collision" or "slot_unidentified" or "enumeration_limit")
                lines.Add(DescribeInventoryEvidence(item.SlotEvidence));
            foreach (var field in item.Fields)
            {
                var label = field.Name switch
                {
                    "name" => "HardwareInventoryModel",
                    "manufacturer" => "HardwareInventoryManufacturer",
                    "wmi_core_count" => "HardwareInventoryWmiCores",
                    "active_core_count" => "HardwareInventoryActiveCores",
                    "active_logical_processor_count" => "HardwareInventoryLogical",
                    "product" => "HardwareInventoryProduct",
                    "version" => "HardwareInventoryVersion",
                    "smbios_bios_version" => "HardwareInventoryVersion",
                    "boot_firmware_type" => "HardwareInventoryBootType",
                    "capacity_bytes" => "HardwareInventoryCapacity",
                    "configured_clock_mhz_reported" => "HardwareInventoryClock",
                    "smbios_type_code" => "HardwareInventoryMemoryType",
                    _ => null
                };
                if (label is null) continue;
                var state = historical ? field.Evidence : CurrentInventoryEvidence(field.Evidence, now);
                var canShow = state.Status == "available" && state.AtUtc is { } at && at <= now;
                var value = canShow ? FormatInventoryValue(field) : null;
                lines.Add(T("HardwareInventoryField", T(label), value ?? T("DataTrustUnknown")));
                if (historical || state != categoryEvidence || value is null)
                    lines.Add(DescribeInventoryEvidence(state));
            }
        }
    }

    private string? FormatInventoryValue(InventoryField field)
    {
        if (field.Name == "boot_firmware_type")
        {
            return field.Text switch { "bios" => "BIOS", "uefi" => "UEFI", _ => null };
        }
        if (field.Number is { } number && number > 0)
        {
            return field.Name switch
            {
                "capacity_bytes" => T("HardwareInventoryGiB", number / 1073741824d),
                "configured_clock_mhz_reported" => T("HardwareInventoryMHz", number),
                _ => number.ToString("N0", Localization.Culture)
            };
        }
        return field.Text is { Length: > 0 and <= 64 } && !field.Text.Any(char.IsControl) ? field.Text : null;
    }
}
