using System;
using System.Collections.Generic;
using System.Linq;
using HostAgent.Core.Dashboard;

namespace HostAgent.Desktop;

internal enum SituationSubsystem { Cpu, Memory, Disks, Network, Services }

internal enum SituationDataState { Unknown, Available, Partial, Limited, Missing, Stale, Offline }

internal sealed record SituationOrbitNode(
    SituationSubsystem Id, string Title, string Status, string Summary, IReadOnlyList<string> Details,
    SituationDataState DataState);

internal sealed record SituationOrbitSnapshot(
    string Title, string Description, string Status, string Source, string SelectedLabel,
    string CloseDetailLabel, string CollapseDetailLabel, string ExpandDetailLabel, string SelectionAnnouncementTemplate,
    IReadOnlyList<SituationOrbitNode> Nodes)
{
    internal static SituationOrbitSnapshot Create(DashboardSnapshot? view, string connection, bool hasRefreshed,
        DesktopLocalization localization, DateTimeOffset now, DateTimeOffset? lastOverviewUtc = null)
    {
        string Text(string key, params object?[] args) => localization.Text(key, args);

        if (view is not null && !ViewFreshness.IsFresh(view.GeneratedAtUtc, now))
        {
            if (view.GeneratedAtUtc <= now && (lastOverviewUtc is null || view.GeneratedAtUtc > lastOverviewUtc.Value))
                lastOverviewUtc = view.GeneratedAtUtc;
            view = null;
            connection = "stale";
        }

        var connectionStatus = !hasRefreshed ? Text("ConnectingToTheLocalService") : connection == "stale"
            ? Text("OrbitStale") : Text("OrbitOffline");
        var diskRows = view?.Disks.Where(disk => ViewFreshness.IsFresh(disk.AtUtc, now)).ToArray() ?? [];
        var network = view?.NetworkChecks;
        var networkFresh = network is not null && ViewFreshness.IsFresh(network.AtUtc, now);
        var networkPartial = view is not null && view.ConnectedAdapters is not null &&
            (!networkFresh || network is null || !network.Enabled || network.Probes.Count == 0);
        var services = view?.Services;
        var bytes = new ByteSizeConverter();
        string Size(long value) => (string)bytes.Convert(value, typeof(string), "", localization.Culture);
        string At(DateTimeOffset time) => Text("OrbitMeasuredAt", time.ToLocalTime());

        SituationOrbitNode Node(SituationSubsystem id, string label, string component, bool available, string? summary,
            IReadOnlyList<string> details, bool stale = false, bool partial = false)
        {
            var health = view?.Health.FirstOrDefault(item => item.Component == component);
            var partialData = partial || health?.Code is "collector_partial" or "optional_smart_unavailable";
            var dataState = view is null
                ? !hasRefreshed ? SituationDataState.Unknown
                : connection == "stale" ? SituationDataState.Stale : SituationDataState.Offline
                : stale ? SituationDataState.Stale
                : partialData ? SituationDataState.Partial
                : health?.Degraded == true ? SituationDataState.Limited
                : available ? SituationDataState.Available : SituationDataState.Missing;
            var status = view is null ? connectionStatus
                : stale ? Text("OrbitStale")
                : partialData ? Text(available ? "OrbitPartial" : "OrbitMissing")
                : health?.Degraded == true ? Text(available ? "OrbitLimited" : "OrbitMissing")
                : Text(available ? "OrbitAvailable" : "OrbitMissing");
            if (health is { Degraded: true } && !available)
                details = [.. details, Text(health.Code == "collector_partial" ? "OrbitPartial" : "OrbitLimited")];
            return new(id, Text(label), status, summary ?? "", details, dataState);
        }

        SituationOrbitNode Metric(SituationSubsystem id, string title, ViewMetric? point)
        {
            var fresh = point is not null && ViewFreshness.IsFresh(point.AtUtc, now);
            var available = fresh && point is { Value: >= 0 and <= 100 };
            return Node(id, title, "cpu-memory", available,
                available ? point!.Value.ToString("0.#", localization.Culture) + " %" : null,
                point is null ? [] : [At(point.AtUtc)], point is not null && !fresh);
        }

        List<string> networkDetails = [];
        if (view?.ConnectedAdapters is not null)
            networkDetails.Add(Text("OrbitAdapterTimeUnknown"));
        if (networkFresh && network is not null)
        {
            networkDetails.Add(Text("OrbitProbeTime", network.AtUtc.ToLocalTime()));
            if (!network.Enabled)
                networkDetails.Add(Text("NetworkChecksDisabled"));
            else if (network.Probes.Count == 0)
                networkDetails.Add(Text("OrbitMissing"));
            else
                networkDetails.AddRange(network.Probes.Select(probe =>
                    TechnicalText.FormatNetworkProbeKind(probe.Kind, localization.Culture) + " · " +
                    TechnicalText.FormatNetworkProbeStatus(probe.Status, localization.Culture)));
        }
        else if (view is not null)
        {
            networkDetails.Add(Text(network is null ? "OrbitProbesUnavailable" : "OrbitProbesStale"));
        }

        return new(Text("OrbitTitle"), Text("OrbitDescription"),
            view is null ? connectionStatus : Text(view.Degraded ? "OrbitLimited" : "OrbitAvailable"),
            view is not null ? Text("OrbitOverviewAt", view.GeneratedAtUtc.ToLocalTime())
                : lastOverviewUtc is { } last && last <= now ? Text("OrbitLastOverviewAt", last.ToLocalTime()) : "",
            Text("OrbitSelected"), Text("OrbitCloseDetail"), Text("OrbitCollapseDetail"), Text("OrbitExpandDetail"),
            Text("OrbitSelectionAnnouncement"),
            [
                Metric(SituationSubsystem.Cpu, "Processor", view?.Cpu),
                Metric(SituationSubsystem.Memory, "RAM", view?.Memory),
                Node(SituationSubsystem.Disks, "Storage", "disk", diskRows.Length > 0,
                    diskRows.Length > 0 ? Text("OrbitShownCount", diskRows.Length) : null,
                    diskRows.Select(disk => Text("OrbitDiskSpace", disk.Drive, Size(disk.FreeBytes),
                        Size(disk.CapacityBytes), disk.AtUtc.ToLocalTime())).ToArray(),
                    view?.Disks.Count > 0 && diskRows.Length == 0),
                Node(SituationSubsystem.Network, "Network", "network",
                    view?.ConnectedAdapters is not null || networkFresh,
                    view?.ConnectedAdapters is { } count
                        ? Text("NetworkActiveAdaptersThisDoesNotVerifyInternetAccess", count) : null,
                    networkDetails, view?.ConnectedAdapters is null && network is not null && !networkFresh,
                    networkPartial),
                Node(SituationSubsystem.Services, "OrbitServices", "process-services", services?.Count > 0,
                    services?.Count > 0 ? Text("OrbitShownCount", services.Count) : null,
                    services is { Count: > 0 }
                        ? [Text("OrbitInventoryTimeUnknown"), .. services.Select(service => service.Name + " · " +
                            TechnicalText.Format(service.State, localization.Culture))]
                        : [])
            ]);
    }
}
