using System.ComponentModel;

namespace HostAgent.Infrastructure.Collectors;

// Preserve a bounded reason without exposing process identity or native text.
// Only .NET's known unqueryable Idle PID0 is expected; other access denial
// remains partial telemetry and retains the existing health policy.
internal static class ProcessMetricFailure
{
    internal static string? Classify(Exception error, int? processId = null)
        => processId == 0 && error is Win32Exception { NativeErrorCode: 5 }
            ? null
            : error is Win32Exception { NativeErrorCode: 5 }
            ? "some_process_metrics_access_denied"
            : "some_process_metrics_unavailable";
}
