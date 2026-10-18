using System.ComponentModel;

namespace HostAgent.Infrastructure.Collectors;

// Preserve a bounded reason without exposing process identity or native text.
// Access denial remains partial telemetry; this does not change health policy.
internal static class ProcessMetricFailure
{
    internal static string Classify(Exception error)
        => error is Win32Exception { NativeErrorCode: 5 }
            ? "some_process_metrics_access_denied"
            : "some_process_metrics_unavailable";
}
