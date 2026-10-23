using System.ComponentModel;

namespace HostAgent.Infrastructure.Collectors;

// Preserve a bounded reason without exposing process identity or native text.
// Only .NET's known unqueryable Idle PID0 is expected; other access denial
// remains partial telemetry and retains the existing health policy.
internal static class ProcessMetricFailure
{
    internal static string? ClassifyStage(Exception error,int? processId,ProcessMetricReadStage stage)
        => Classify(error,processId)!="some_process_metrics_access_denied"?null:stage switch
        {
            ProcessMetricReadStage.Identity=>"process_identity_access_denied",
            ProcessMetricReadStage.Name=>"process_name_access_denied",
            ProcessMetricReadStage.CpuTime=>"process_cpu_time_access_denied",
            ProcessMetricReadStage.WorkingSet=>"process_working_set_access_denied",
            ProcessMetricReadStage.StartTime=>"process_start_time_access_denied",
            _=>null
        };
    internal static string? Classify(Exception error, int? processId = null)
        => processId == 0 && error is Win32Exception { NativeErrorCode: 5 }
            ? null
            : error is Win32Exception { NativeErrorCode: 5 }
            ? "some_process_metrics_access_denied"
            : "some_process_metrics_unavailable";
}
internal enum ProcessMetricReadStage { Identity, Name, CpuTime, WorkingSet, StartTime }
