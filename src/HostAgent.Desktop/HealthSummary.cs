using System;
using System.Linq;
using HostAgent.Core.Dashboard;

namespace HostAgent.Desktop;

public static class HealthSummary
{
    public static string Describe(DashboardSnapshot? snapshot)
    {
        if (snapshot is null || !ViewFreshness.IsFresh(snapshot.GeneratedAtUtc, DateTimeOffset.UtcNow))
            return "Aktuální souhrn není dostupný. Obnovte spojení se službou; podrobnosti najdete v části Komponenty.";
        var limited = snapshot.Health.Count(x => x.Degraded || (x.Reasons?.Count ?? 0) > 0);
        var text = snapshot.Health.Count == 0
            ? "Stav komponent zatím nebyl předán."
            : $"Komponenty s hlášeným omezením: {limited} z {snapshot.Health.Count}.";
        if (snapshot.Degraded && limited == 0) text += " Služba hlásí omezení bez podrobných důvodů.";
        if (snapshot.Cpu is null || !ViewFreshness.IsFresh(snapshot.Cpu.AtUtc, DateTimeOffset.UtcNow))
            text += " Chybí aktuální celkové měření CPU.";
        if (snapshot.Memory is null || !ViewFreshness.IsFresh(snapshot.Memory.AtUtc, DateTimeOffset.UtcNow))
            text += " Chybí aktuální celkové měření paměti.";
        return text + " Podrobnosti a technické kódy najdete v části Komponenty.";
    }
}
