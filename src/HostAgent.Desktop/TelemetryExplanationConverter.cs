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
            return DesktopLocalization.Text(culture, "NoDetailedReasonsProvided");
        return string.Join(Environment.NewLine, reasons.Select(reason => Explain(reason, culture)));
    }

    private static string Explain(string reason, CultureInfo culture)
    {
        var key = reason switch
        {
            "some_process_metrics_access_denied" => "SomeProcessMetricsAccessDenied",
            "some_process_metrics_unavailable" => "SomeProcessMetricsUnavailable",
            "process_cpu_time_access_denied" => "ProcessCpuTimeAccessDenied",
            "process_identity_access_denied" => "ProcessIdentityAccessDenied",
            "process_name_access_denied" => "ProcessNameAccessDenied",
            "process_start_time_access_denied" => "ProcessStartTimeAccessDenied",
            "process_working_set_access_denied" => "ProcessWorkingSetAccessDenied",
            "some_executable_paths_unavailable" => "SomeExecutablePathsUnavailable",
            "some_process_start_times_unavailable" => "SomeProcessStartTimesUnavailable",
            "process_list_truncated" => "ProcessListTruncated",
            "process_list_unavailable" => "ProcessListUnavailable",
            "cpu_baseline_not_ready" => "CpuBaselineNotReady",
            "system_cpu_counter_unavailable" => "SystemCpuCounterUnavailable",
            "global_memory_status_unavailable" => "GlobalMemoryStatusUnavailable",
            "service_catalog_unavailable" => "ServiceCatalogUnavailable",
            "some_service_start_modes_unavailable" => "SomeServiceStartModesUnavailable",
            "some_service_states_unavailable" => "SomeServiceStatesUnavailable",
            "some_service_triggers_unavailable" => "SomeServiceTriggersUnavailable",
            _ => null
        };
        return key is null
            ? DesktopLocalization.Text(culture, "AdditionalReportedReason", reason)
            : DesktopLocalization.Text(culture, key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
