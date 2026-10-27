using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace HostAgent.Desktop;

internal static class TechnicalText
{
    private static readonly IReadOnlyDictionary<string,string> Labels=new Dictionary<string,string>(StringComparer.Ordinal)
    {
        ["Shadow"]="ShadowObservationOnly",["Confirmation"]="ActionConfirmation",
        ["Detected"]="Detected",["Active"]="Active",["Acknowledged"]="Acknowledged",["RemediationProposed"]="ActionProposed",
        ["RemediationPendingApproval"]="AwaitingApproval",["Remediating"]="RemediationInProgress",["Resolved"]="Resolved",["Failed"]="ActionFailed",["Escalated"]="RequiresAttention",
        ["Running"]="Running",["Stopped"]="Stopped",["Paused"]="Paused",["StartPending"]="Starting",["StopPending"]="Stopping",["PausePending"]="Pausing",["ContinuePending"]="Resuming",["Unknown"]="UnknownStatus",
        ["Quarantined"]="Quarantined",["NeedsReview"]="RequiresReview",["NotMoved"]="NotMoved",["Restored"]="Restored",["Intent"]="IncompleteMove",["RestoreIntent"]="IncompleteRestore",
        ["administration"]="Administration",["collection_worker"]="CollectionScheduler",["collectors"]="DataCollection",["cpu-memory"]="CPUAndMemory",["diagnostics"]="Diagnostics",["disk"]="Disks",["event-log"]="EventLog",["network"]="Network",["process-services"]="ProcessesAndServices",["publication"]="Publishing",["system"]="System",["runtime"]="ApplicationRuntime",
        ["healthy"]="Healthy",["collector_healthy"]="CollectionIsHealthy",["collector_partial"]="PartialData",["collectors_partial"]="PartialData",["collector_unavailable"]="CollectionUnavailable",["collectors_unavailable"]="CollectionUnavailable",["collector_failed"]="CollectionFailed",["collector_timeout"]="CollectionTimedOut",["collector_exception"]="CollectionError",["backoff_active"]="WaitingBeforeRetry",["collector_still_running"]="PreviousCollectionIsStillRunning",
        ["optional_smart_unavailable"]="OptionalSMARTUnavailable",["optional_process_paths_unavailable"]="OptionalProcessPathsUnavailable",
        ["optional_process_identity_unavailable"]="OptionalProcessIdentityUnavailable",["process_identity_unavailable"]="ExactProcessIdentityUnavailable",
        ["volume_not_ready"]="VolumeNotReady",["volume_metrics_unavailable"]="VolumeMetricsUnavailable",["disk_io_counters_unavailable"]="DiskIOCountersUnavailable",["some_disk_io_counters_unavailable"]="SomeDiskIOCountersUnavailable",["smart_health_unavailable"]="SMARTUnavailable",["system_cpu_counter_unavailable"]="TotalCPUUtilizationUnavailable",["cpu_baseline_not_ready"]="WaitingForSecondCPUSample",["process_list_unavailable"]="ProcessListUnavailable",
        ["some_process_metrics_unavailable"]="SomeProcessMetricsUnavailable",["some_process_metrics_access_denied"]="AccessToSomeProcessMetricsDenied",["some_process_start_times_unavailable"]="SomeProcessStartTimesUnavailable",["process_list_truncated"]="ProcessListWasLimited",
        ["process_identity_access_denied"]="AccessToProcessIdentityDenied",["process_name_access_denied"]="AccessToProcessNameDenied",["process_cpu_time_access_denied"]="AccessToProcessCPUTimeDenied",["process_working_set_access_denied"]="AccessToProcessMemoryDenied",["process_start_time_access_denied"]="AccessToProcessStartTimeDenied",
        ["service_catalog_unavailable"]="ServiceListUnavailable",["some_service_states_unavailable"]="SomeServiceStatusesUnavailable",["some_executable_paths_unavailable"]="SomeExecutablePathsUnavailable",["some_service_start_modes_unavailable"]="SomeServiceStartupModesUnavailable",["some_service_triggers_unavailable"]="SomeServiceTriggersUnavailable",["some_network_adapter_details_unavailable"]="SomeAdapterDetailsUnavailable",["no_network_adapters_available"]="NoNetworkAdapterAvailable",["windows_version_registry_unavailable"]="WindowsVersionUnavailable",["shutdown_history_unavailable"]="ShutdownHistoryUnavailable",
        ["preview_ready"]="PreviewReady",["maintenance_busy"]="MaintenanceAlreadyInProgress",["maintenance_confirmation_required"]="ExactConfirmationRequired",["maintenance_entry_unavailable"]="RecordUnavailable",["maintenance_journal_integrity"]="MaintenanceRecordsRequireReview",["maintenance_journal_limit"]="RecordLimitReached",["maintenance_journal_write_failed"]="CouldNotWriteRecord",["maintenance_not_approved"]="MaintenanceNotApproved",["maintenance_payload_changed"]="FileChanged",["maintenance_preview_expired"]="PreviewNoLongerValid",["maintenance_quota_reached"]="MaintenanceQuotaReached",["maintenance_root_unavailable"]="MaintenanceFolderUnavailable",["maintenance_scope"]="TargetOutsideAllowedScope",["maintenance_volume_mismatch"]="TargetOnADifferentVolume",
        ["quarantine_complete"]="MoveToQuarantineCompleted",["quarantine_empty"]="QuarantineIsEmpty",["quarantine_no_files_moved"]="NoFilesWereMoved",["quarantine_partial"]="MovePartiallyCompleted",["quarantine_recovery_required"]="MoveRequiresRecovery",["restore_complete"]="RestoreCompleted",["restore_conflict"]="DestinationFileAlreadyExists",["restore_failed"]="RestoreFailed",["restore_recovery_required"]="RestoreRequiresReview"
    };
    public static string Format(string? code,CultureInfo culture,bool includeCode=false)
    {
        if(string.IsNullOrEmpty(code))return "—";
        if(Labels.TryGetValue(code,out var source))return DesktopLocalization.Text(culture,source)+(includeCode?" ("+code+")":"");
        return DesktopLocalization.Text(culture,"TechnicalCode",code);
    }
    internal static IEnumerable<string> Sources => Labels.Values;
}

public sealed class TechnicalTextConverter : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
        => string.Join(", ",(value as string ?? "").Split(", ",StringSplitOptions.RemoveEmptyEntries).Select(code=>TechnicalText.Format(code,culture,true)));
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}

public sealed class MaintenanceNameConverter : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
        => value is HostAgent.Infrastructure.Maintenance.QuarantineItem item ? item.Id=="requires-review"?DesktopLocalization.Text(culture,"CorruptQuarantineRecord"):item.Name : "—";
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
