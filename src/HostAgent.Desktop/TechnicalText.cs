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
        ["windows_bugcheck"]="DesignWindowsCrash",["bugcheck_irql_not_less_or_equal"]="DesignWindowsCrashIrql",
        ["other_diagnosis"]="DesignOtherDiagnosis",
        ["memory_pressure"]="DesignMemoryPressure",["cpu_host_saturation"]="DesignCpuLoad",["cpu_process_saturation"]="DesignProcessCpuLoad",
        ["disk_capacity"]="DesignDiskCapacity",["gateway_failure"]="DesignGatewayFailure",["dns_failure"]="DesignDnsFailure",
        ["internet_connectivity_failure"]="DesignInternetFailure",["service_stopped"]="DesignServiceStopped",["service_crash"]="DesignServiceCrash",
        ["adaptive_metric_deviation"]="DesignMetricDeviation",
        ["Critical"]="DesignCritical",["High"]="DesignHigh",["Warning"]="DesignWarning",["Information"]="DesignInfo",
        ["Shadow"]="ShadowObservationOnly",["Confirmation"]="ActionConfirmation",
        ["Detected"]="Detected",["Active"]="Active",["Acknowledged"]="Acknowledged",["RemediationProposed"]="ActionProposed",
        ["RemediationPendingApproval"]="AwaitingApproval",["Remediating"]="RemediationInProgress",["Resolved"]="Resolved",["Failed"]="ActionFailed",["Escalated"]="RequiresAttention",
        ["Running"]="Running",["Stopped"]="Stopped",["Paused"]="Paused",["StartPending"]="Starting",["StopPending"]="Stopping",["PausePending"]="Pausing",["ContinuePending"]="Resuming",["Unknown"]="UnknownStatus",
        ["Quarantined"]="Quarantined",["NeedsReview"]="RequiresReview",["NotMoved"]="NotMoved",["Restored"]="Restored",["Intent"]="IncompleteMove",["RestoreIntent"]="IncompleteRestore",
        ["administration"]="Administration",["collection_worker"]="CollectionScheduler",["collectors"]="DataCollection",["cpu-memory"]="CPUAndMemory",["diagnostics"]="Diagnostics",["disk"]="Disks",["event-log"]="EventLog",["network"]="Network",["process-services"]="ProcessesAndServices",["publication"]="Publishing",["system"]="System",["runtime"]="ApplicationRuntime",
        ["healthy"]="Healthy",["collector_healthy"]="CollectionIsHealthy",["collector_partial"]="PartialData",["collectors_partial"]="PartialData",["collector_unavailable"]="CollectionUnavailable",["collectors_unavailable"]="CollectionUnavailable",["collector_failed"]="CollectionFailed",["collector_timeout"]="CollectionTimedOut",["collector_exception"]="CollectionError",["backoff_active"]="WaitingBeforeRetry",["collector_still_running"]="PreviousCollectionIsStillRunning",
        ["optional_smart_unavailable"]="OptionalSMARTUnavailable",["optional_process_paths_unavailable"]="OptionalProcessPathsUnavailable",
        ["volume_not_ready"]="VolumeNotReady",["volume_metrics_unavailable"]="VolumeMetricsUnavailable",["disk_io_counters_unavailable"]="DiskIOCountersUnavailable",["some_disk_io_counters_unavailable"]="SomeDiskIOCountersUnavailable",["smart_health_unavailable"]="SMARTUnavailable",["system_cpu_counter_unavailable"]="TotalCPUUtilizationUnavailable",["cpu_baseline_not_ready"]="WaitingForSecondCPUSample",["process_list_unavailable"]="ProcessListUnavailable",
        ["some_process_metrics_unavailable"]="SomeProcessMetricsUnavailable",["some_process_metrics_access_denied"]="AccessToSomeProcessMetricsDenied",["some_process_start_times_unavailable"]="SomeProcessStartTimesUnavailable",["process_list_truncated"]="ProcessListWasLimited",
        ["process_identity_access_denied"]="AccessToProcessIdentityDenied",["process_name_access_denied"]="AccessToProcessNameDenied",["process_cpu_time_access_denied"]="AccessToProcessCPUTimeDenied",["process_working_set_access_denied"]="AccessToProcessMemoryDenied",["process_start_time_access_denied"]="AccessToProcessStartTimeDenied",
        ["service_catalog_unavailable"]="ServiceListUnavailable",["some_service_states_unavailable"]="SomeServiceStatusesUnavailable",["some_executable_paths_unavailable"]="SomeExecutablePathsUnavailable",["some_service_start_modes_unavailable"]="SomeServiceStartupModesUnavailable",["some_service_triggers_unavailable"]="SomeServiceTriggersUnavailable",["some_network_adapter_details_unavailable"]="SomeAdapterDetailsUnavailable",["no_network_adapters_available"]="NoNetworkAdapterAvailable",["windows_version_registry_unavailable"]="WindowsVersionUnavailable",["shutdown_history_unavailable"]="ShutdownHistoryUnavailable",
        ["unsafe_name"]="MaintenanceReason_unsafe_name",["too_recent"]="MaintenanceReason_too_recent",["file_too_large"]="MaintenanceReason_file_too_large",
        ["scope_byte_limit"]="MaintenanceReason_scope_byte_limit",["unsupported_file"]="MaintenanceReason_unsupported_file",["multiple_links"]="MaintenanceReason_multiple_links",
        ["read_only"]="MaintenanceReason_read_only",["file_locked"]="MaintenanceReason_file_locked",["access_denied"]="MaintenanceReason_access_denied",
        ["file_unavailable"]="MaintenanceReason_file_unavailable",["verification_failed"]="MaintenanceReason_verification_failed",["preview_time_limit"]="MaintenanceReason_preview_time_limit",
        ["preview_incomplete"]="MaintenanceReason_preview_incomplete",["service_not_approved"]="MaintenanceReason_service_not_approved",
        ["source_identity_changed"]="MaintenanceReason_source_identity_changed",["source_metadata_changed"]="MaintenanceReason_source_metadata_changed",
        ["source_hash_changed"]="MaintenanceReason_source_hash_changed",["destination_already_exists"]="MaintenanceReason_destination_already_exists",
        ["quarantine_limit_reached"]="MaintenanceReason_quarantine_limit_reached",["operation_failed"]="MaintenanceReason_operation_failed",
        ["cancelled"]="MaintenanceReason_cancelled",["outcome_unverified"]="MaintenanceReason_outcome_unverified",
        ["preview_expired"]="MaintenanceReason_preview_expired",["source_root_unavailable"]="MaintenanceReason_source_root_unavailable",
        ["quarantine_root_unavailable"]="MaintenanceReason_quarantine_root_unavailable",["quarantine_lock_unavailable"]="MaintenanceReason_quarantine_lock_unavailable",
        ["quarantine_busy"]="MaintenanceReason_quarantine_busy",["volume_mismatch"]="MaintenanceReason_volume_mismatch",
        ["payload_unavailable"]="MaintenanceReason_payload_unavailable",["payload_verification_failed"]="MaintenanceReason_payload_verification_failed",
        ["journal_recovery_required"]="MaintenanceReason_journal_recovery_required",["journal_unavailable"]="MaintenanceReason_journal_unavailable",
        ["journal_integrity"]="MaintenanceReason_journal_integrity",
        ["preview_ready"]="PreviewReady",["maintenance_busy"]="MaintenanceAlreadyInProgress",["maintenance_confirmation_required"]="ExactConfirmationRequired",["maintenance_entry_unavailable"]="RecordUnavailable",["maintenance_journal_integrity"]="MaintenanceRecordsRequireReview",["maintenance_journal_limit"]="RecordLimitReached",["maintenance_journal_write_failed"]="CouldNotWriteRecord",["maintenance_not_approved"]="MaintenanceNotApproved",["maintenance_payload_changed"]="FileChanged",["maintenance_preview_expired"]="PreviewNoLongerValid",["maintenance_quota_reached"]="MaintenanceQuotaReached",["maintenance_root_unavailable"]="MaintenanceFolderUnavailable",["maintenance_scope"]="TargetOutsideAllowedScope",["maintenance_volume_mismatch"]="TargetOnADifferentVolume",
        ["quarantine_complete"]="MoveToQuarantineCompleted",["quarantine_empty"]="QuarantineIsEmpty",["quarantine_no_files_moved"]="NoFilesWereMoved",["quarantine_partial"]="MovePartiallyCompleted",["quarantine_recovery_required"]="MoveRequiresRecovery",["restore_complete"]="RestoreCompleted",["restore_conflict"]="DestinationFileAlreadyExists",["restore_failed"]="RestoreFailed",["restore_recovery_required"]="RestoreRequiresReview",
        ["maintenance_preview_incomplete"]="PreviewIncomplete",["maintenance_state_unavailable"]="MaintenanceResultStateUnavailable",
        ["quarantine_blocked"]="MaintenanceResultBlocked",["quarantine_cancelled"]="MaintenanceResultCancelled",["quarantine_cancelled_partial"]="MaintenanceResultCancelledPartial",
        ["quarantine_failed"]="MaintenanceResultFailed",["quarantine_outcome_unknown"]="MaintenanceResultOutcomeUnknown",
        ["quarantine_partial_blocked"]="MaintenanceResultPartialBlocked",["quarantine_partial_blocked_recovery_required"]="MaintenanceResultPartialBlockedRecovery",
        ["quarantine_partial_failure"]="MaintenanceResultPartialFailure",["restore_cancelled"]="MaintenanceResultCancelled",["restore_outcome_unknown"]="MaintenanceResultOutcomeUnknown",
        ["moved"]="MaintenanceActionMoved",["restored"]="Restored",["skipped"]="MaintenanceActionSkipped",["blocked"]="MaintenanceActionBlocked",["failed"]="ActionFailed",["cancelled"]="MaintenanceActionCancelled",["unknown"]="UnknownStatus"
    };
    public static string Format(string? code,CultureInfo culture,bool includeCode=false)
    {
        if(string.IsNullOrEmpty(code))return "—";
        if(Labels.TryGetValue(code,out var source))return DesktopLocalization.Text(culture,source)+(includeCode?" ("+code+")":"");
        return includeCode
            ? DesktopLocalization.Text(culture,"TechnicalCode",code)
            : DesktopLocalization.Text(culture,"UnclassifiedDiagnostic");
    }
    public static string FormatMaintenanceReason(string? code,CultureInfo culture,bool includeCode=false)
    {
        if(string.IsNullOrWhiteSpace(code))return "—";
        var value=Labels.TryGetValue(code,out var source)
            ? DesktopLocalization.Text(culture,source)
            : DesktopLocalization.Text(culture,"MaintenanceUnknownReason");
        return includeCode?value+" ("+code+")":value;
    }
    public static string FormatMaintenanceActionState(string? state,CultureInfo culture,bool includeCode=false)
    {
        var source=state switch
        {
            "moved"=>"MaintenanceActionMoved",
            "restored"=>"Restored",
            "skipped"=>"MaintenanceActionSkipped",
            "blocked"=>"MaintenanceActionBlocked",
            "failed"=>"ActionFailed",
            "cancelled"=>"MaintenanceActionCancelled",
            "unknown"=>"UnknownStatus",
            _=>"MaintenanceUnknownReason"
        };
        var value=DesktopLocalization.Text(culture,source);
        return includeCode&&state is not null?value+" ("+state+")":value;
    }
    internal static IEnumerable<string> Sources => Labels.Values;
}

public sealed class TechnicalTextConverter : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
        => string.Join(", ",(value as string ?? "").Split(", ",StringSplitOptions.RemoveEmptyEntries).Select(code=>TechnicalText.Format(code,culture,string.Equals(parameter as string,"Developer",StringComparison.Ordinal))));
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}

public sealed class MaintenanceNameConverter : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
        => value is HostAgent.Infrastructure.Maintenance.QuarantineItem item ? item.Id=="requires-review"?DesktopLocalization.Text(culture,"CorruptQuarantineRecord"):item.Name : "—";
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
