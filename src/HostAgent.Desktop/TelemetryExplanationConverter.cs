using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace HostAgent.Desktop;

public sealed class TelemetryExplanationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IReadOnlyList<string> reasons || reasons.Count == 0)
            return "Podrobné důvody nebyly předány.";
        return string.Join(Environment.NewLine, reasons.Select(Explain));
    }

    private static string Explain(string reason) => reason switch
    {
        "some_process_metrics_access_denied" => "Windows odmítl přístup k některým údajům o procesech.",
        "some_process_metrics_unavailable" => "Některé údaje o procesech nejsou dostupné.",
        "process_cpu_time_access_denied" => "Čas CPU některých procesů nelze přečíst kvůli oprávněním.",
        "process_identity_access_denied" => "Identitu některých procesů nelze přečíst kvůli oprávněním.",
        "process_name_access_denied" => "Názvy některých procesů nelze přečíst kvůli oprávněním.",
        "process_start_time_access_denied" => "Čas spuštění některých procesů nelze přečíst kvůli oprávněním.",
        "process_working_set_access_denied" => "Využití paměti některých procesů nelze přečíst kvůli oprávněním.",
        "some_executable_paths_unavailable" => "Cesty k programům některých procesů nejsou dostupné.",
        "some_process_start_times_unavailable" => "Čas spuštění některých procesů není dostupný.",
        "process_list_truncated" => "Seznam procesů byl omezen; nezahrnuje všechny procesy.",
        "process_list_unavailable" => "Seznam procesů se nepodařilo načíst.",
        "cpu_baseline_not_ready" => "Pro výpočet využití CPU zatím chybí předchozí vzorek.",
        "system_cpu_counter_unavailable" => "Celkové využití CPU se nepodařilo změřit.",
        "global_memory_status_unavailable" => "Celkové využití paměti se nepodařilo změřit.",
        "service_catalog_unavailable" => "Seznam služeb Windows se nepodařilo načíst.",
        "some_service_start_modes_unavailable" => "Způsob spouštění některých služeb není dostupný.",
        "some_service_states_unavailable" => "Aktuální stav některých služeb není dostupný.",
        "some_service_triggers_unavailable" => "Spouštěcí podmínky některých služeb nejsou dostupné.",
        _ => "Další hlášený důvod: " + reason
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
