using System;
using System.Globalization;
using System.Linq;
using HostAgent.Core.Dashboard;

namespace HostAgent.Desktop;

public static class HealthSummary
{
    public static string Describe(DashboardSnapshot? snapshot)
        => Describe(snapshot, CultureInfo.GetCultureInfo("cs-CZ"));

    public static string Describe(DashboardSnapshot? snapshot, CultureInfo culture)
    {
        if (snapshot is null || !ViewFreshness.IsFresh(snapshot.GeneratedAtUtc, DateTimeOffset.UtcNow))
            return DesktopLocalization.Text(culture, "HealthSummaryUnavailable");
        var limited = snapshot.Health.Count(x => x.Degraded || (x.Reasons?.Count ?? 0) > 0);
        var text = snapshot.Health.Count == 0
            ? DesktopLocalization.Text(culture, "HealthSummaryComponentsMissing")
            : DesktopLocalization.Text(culture, "HealthSummaryComponentsLimited", limited, snapshot.Health.Count);
        if (snapshot.Degraded && limited == 0) text += DesktopLocalization.Text(culture, "HealthSummaryDegradedWithoutReasons");
        var now = DateTimeOffset.UtcNow;
        if (snapshot.Cpu is not {Value:>=0 and <=100} || !ViewFreshness.IsFresh(snapshot.Cpu.AtUtc, now)
            || snapshot.DataState?.Cpu.HasCurrentData(now)==false)
            text += DesktopLocalization.Text(culture, "HealthSummaryMissingCpu");
        if (snapshot.Memory is not {Value:>=0 and <=100} || !ViewFreshness.IsFresh(snapshot.Memory.AtUtc, now)
            || (snapshot.DataState?.Memory.HasCurrentData(now)==false || snapshot.DataState is null && snapshot.Memory.Value==0))
            text += DesktopLocalization.Text(culture, "HealthSummaryMissingMemory");
        return text + DesktopLocalization.Text(culture, "HealthSummaryDetails");
    }
}
