namespace HostAgent.Core.Dashboard;

public sealed class RecommendationEngine
{
    public IReadOnlyList<Recommendation> Evaluate(DashboardSnapshot snapshot, DateTimeOffset now)
    {
        var result = new List<Recommendation>();
        if (Sustained(snapshot.RecentCpu, now))
            result.Add(new("sustained_cpu", "Prověřit dlouhodobé vytížení CPU",
                "CPU alespoň 90 % po dobu nejméně 4 minut v posledních 6 minutách.",
                "Omezení nepotřebných programů po spuštění může uvolnit výkon; přínos se musí změřit.",
                "Vypnutí automatického spuštění může odložit synchronizaci nebo jiné funkce aplikace.",
                "Položku lze znovu povolit v nastavení Po spuštění.", GuidedAction.StartupSettings));
        if (Sustained(snapshot.RecentMemory, now))
            result.Add(new("sustained_memory", "Prověřit tlak na paměť",
                "RAM alespoň 90 % po dobu nejméně 4 minut v posledních 6 minutách.",
                "Zavření nepotřebné aplikace může uvolnit RAM. Neuloženou práci nejdřív uložte.",
                "Odinstalace není nutná; nejdřív prověřte spotřebu aplikací v přehledu.",
                "Zavřenou aplikaci lze znovu spustit; neuložená data nemusí být obnovitelná.", GuidedAction.AppsSettings));
        foreach (var disk in snapshot.Disks.Take(8).Where(x => ViewFreshness.IsFresh(x.AtUtc,now)
            && x.CapacityBytes > 0 && x.FreeBytes >= 0 && x.FreeBytes <= x.CapacityBytes / 10))
            result.Add(new("disk_space:" + disk.Drive, "Prověřit místo na disku " + disk.Drive,
                FormattableString.Invariant($"Volné místo {100d*disk.FreeBytes/disk.CapacityBytes:0.#} % ({disk.FreeBytes} B)."),
                "Uvolnění místa může pomoci aplikacím a aktualizacím. Odhad zrychlení není k dispozici.",
                "Nastavení úložiště zobrazí kandidáty; každý výběr ke smazání zkontrolujte. Osobní soubory se nemažou automaticky.",
                "Některé odstranění je nevratné; osobní soubory nejdřív zálohujte.", GuidedAction.StorageSettings));
        var crash = snapshot.Incidents.FirstOrDefault(x => x.State is not ("Resolved" or "Failed")
            && x.AtUtc <= now && now-x.AtUtc <= TimeSpan.FromHours(24)
            && (x.Code == "windows_bugcheck" || x.Code.StartsWith("bugcheck_",StringComparison.Ordinal)));
        if (crash is not null)
            result.Add(new("analyze_crash", "Prověřit příčinu pádu Windows",
                $"Kód {crash.Code}; confidence {crash.Confidence} % je klasifikace stop kódu, nikoli určení vadného ovladače.",
                "Porovnání WER událostí a dumpu může zúžit příčinu pádu.",
                "Automatická změna ovladačů není dostupná; konkrétní ovladač není tímto důkazem určen.",
                "Tato diagnostika je pouze pro čtení a nemění Windows.", GuidedAction.Investigate, crash.Id));
        return result;
    }

    private static bool Sustained(IReadOnlyList<ViewMetric> source, DateTimeOffset now)
    {
        var samples=source.Where(x=>x.AtUtc<=now && now-x.AtUtc<=TimeSpan.FromMinutes(6))
            .OrderBy(x=>x.AtUtc).ToArray();
        if(samples.Length<5 || !ViewFreshness.IsFresh(samples[^1].AtUtc,now)
            || samples[^1].AtUtc-samples[0].AtUtc<TimeSpan.FromMinutes(4)) return false;
        return samples.All(x=>double.IsFinite(x.Value) && x.Value is >=90 and <=100)
            && samples.Zip(samples.Skip(1),(a,b)=>b.AtUtc-a.AtUtc).All(gap=>gap>TimeSpan.Zero && gap<=TimeSpan.FromSeconds(90));
    }
}
