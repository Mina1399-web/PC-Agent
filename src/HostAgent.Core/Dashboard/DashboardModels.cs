namespace HostAgent.Core.Dashboard;

public sealed record ViewMetric(DateTimeOffset AtUtc, double Value);
public sealed record ViewDisk(string Drive, long FreeBytes, long CapacityBytes, DateTimeOffset AtUtc);
public sealed record ViewProcess(int Pid, string Name, double? CpuPercent, long MemoryBytes)
{ public long? StartTimeUtcTicks {get;init;} }
public sealed record ViewService(string Name, string State);
public sealed record ViewIncident(string Id, string State, string Code, int Confidence, string Severity, DateTimeOffset AtUtc, int Recurrences);
public sealed record ViewHealth(string Component, string Code, bool Degraded);
public sealed record ViewEvidence(string Source, double? Value, DateTimeOffset AtUtc);
public sealed record ViewIncidentDetail(ViewIncident Incident, string ConfidenceScope, IReadOnlyList<ViewEvidence> Evidence, bool Truncated);
public sealed record ViewEvent(int EventId, long? RecordId, DateTimeOffset AtUtc, uint? BugCheckCode, string? DumpFileName);
public sealed record ViewInvestigation(DateTimeOffset AtUtc, IReadOnlyList<ViewEvent> Events, IReadOnlyList<ViewIncident> Incidents, string Limitation);
public sealed record DashboardSnapshot(DateTimeOffset GeneratedAtUtc, string Mode, bool ReadOnly, bool Degraded,
    ViewMetric? Cpu, ViewMetric? Memory, ViewMetric? Uptime, IReadOnlyList<ViewDisk> Disks,
    IReadOnlyList<ViewProcess> Processes, IReadOnlyList<ViewService> Services, int? ConnectedAdapters,
    IReadOnlyList<ViewIncident> Incidents, IReadOnlyList<ViewHealth> Health,
    IReadOnlyList<ViewMetric> RecentCpu, IReadOnlyList<ViewMetric> RecentMemory)
{ public bool KillSwitch {get;init;} }
public enum GuidedAction { None, StorageSettings, AppsSettings, StartupSettings, Investigate }
public sealed record Recommendation(string Code, string Title, string Evidence, string Benefit, string Impact,
    string Rollback, GuidedAction Action, string? IncidentId = null);

public static class ViewFreshness
{
    public static bool IsFresh(DateTimeOffset at, DateTimeOffset now) => at <= now && now - at <= TimeSpan.FromMinutes(3);
}
