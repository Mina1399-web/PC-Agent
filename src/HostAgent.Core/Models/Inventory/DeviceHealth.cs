using HostAgent.Core.Dashboard;

namespace HostAgent.Core.Models.Inventory;

public sealed record DeviceHealthFinding(Guid? DeviceClass, uint? ProblemCode, string State, string Description);
public sealed record DeviceHealthReport(string Outcome, ViewDataState Coverage, int Enumerated, int Checked,
    IReadOnlyList<DeviceHealthFinding> Findings)
{
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(15);
    public const string Source = "Windows PnP";

    public DeviceHealthReport At(DateTimeOffset now) => Coverage.AtUtc is { } at && (at > now || now - at > MaximumAge)
        ? this with { Outcome = "unknown", Coverage = Coverage with
            { Status = "stale", Reason = at > now ? "invalid_sample" : "sample_expired" },
            Findings = at > now ? [] : Findings }
        : this;
}
