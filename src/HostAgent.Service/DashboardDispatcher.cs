using HostAgent.Core.Contracts;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Core.Dashboard;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Core.Models.Incidents;
using System.Text.Json;
namespace HostAgent.Service;

public sealed class DashboardDispatcher(IHostAgentStore store,ServiceConfiguration configuration,AgentRuntimeHealth health,TimeProvider clock)
{
    public async ValueTask<byte[]> DispatchAsync(byte[] payload,CancellationToken token)
    {
        try
        {
            var request=ViewProtocol.Parse(payload);var now=clock.GetUtcNow();
            switch(request.Command)
            {
                case "overview":
                    var cpu=await Metrics("cpu.usage.percent",now.AddMinutes(-6),now,128,token);
                    var memory=await Metrics("memory.used.percent",now.AddMinutes(-6),now,128,token);
                    var uptime=await Metrics("system.uptime.seconds",now.AddMinutes(-3),now,1,token);
                    var disk=await Latest<DiskTelemetry>("disk",now,token);
                    var process=await Latest<ProcessServiceTelemetry>("process-services",now,token);
                    var network=await Latest<NetworkTelemetry>("network",now,token);
                    return AdminProtocol.Reply(true,"overview",new DashboardSnapshot(now,configuration.Mode.ToString(),configuration.ReadOnly,health.Degraded,
                        Current(cpu,now),Current(memory,now),Current(uptime,now),
                        disk.Value?.Volumes.Where(x=>ValidDrive(x.RootPath)).Take(8).Select(x=>new ViewDisk(x.RootPath.TrimEnd('\\'),x.FreeBytes,x.CapacityBytes,disk.At)).ToArray()??[],
                        process.Value?.Processes.OrderByDescending(x=>x.MemoryBytes).Take(10).Select(x=>new ViewProcess(x.ProcessId,SafeName(x.ProcessName),x.CpuUsagePercent,x.MemoryBytes){StartTimeUtcTicks=x.StartedAtUtc?.UtcTicks}).ToArray()??[],
                        process.Value?.Services.OrderBy(x=>x.ServiceName,StringComparer.Ordinal).Take(30).Select(x=>new ViewService(SafeName(x.ServiceName),x.Status.ToString())).ToArray()??[],
                        network.Value?.Adapters.Count(x=>x.Status=="Up"),await Heads(token),
                        health.Snapshot().Take(16).Select(x=>new ViewHealth(x.Component,x.Code,x.Degraded,x.Reasons)).ToArray(),MinuteMinimum(cpu),MinuteMinimum(memory)){KillSwitch=configuration.KillSwitch});
                case "history":
                    var metric=request.Body.GetProperty("metric").GetString()=="cpu"?"cpu.usage.percent":"memory.used.percent";
                    // Minimum supported interval is 5s: 24h has at most 17,281 samples.
                    var samples=await Metrics(metric,now.AddHours(-24),now,20_000,token);
                    if(samples.Length==20_000)return AdminProtocol.Reply(false,"view_history_capacity");
                    var points=samples.GroupBy(x=>x.AtUtc.UtcTicks/TimeSpan.FromMinutes(15).Ticks)
                        .OrderBy(x=>x.Key).Select(g=>new ViewMetric(g.Max(x=>x.AtUtc),g.Average(x=>x.Value))).TakeLast(97).ToArray();
                    return AdminProtocol.Reply(true,"history",points);
                case "incident":
                    var id=request.Body.GetProperty("incidentId").GetString();
                    var incident=(await store.ReadIncidentHeadsAsync(token)).SingleOrDefault(x=>x.IncidentId==id);
                    if(incident is null)return AdminProtocol.Reply(false,"view_incident_not_found");
                    return AdminProtocol.Reply(true,"incident",new ViewIncidentDetail(Project(incident),
                        IsCrash(incident.LatestDiagnosis.ProbableRootCause.Code)?"stop_code_classification_only":"probable_cause_estimate",
                        incident.LatestDiagnosis.Evidence.Take(16).Select(x=>new ViewEvidence(SafeSource(x.Source),
                            x.NumericValue is {} v && double.IsFinite(v)?v:null,x.ObservedAtUtc)).ToArray(),incident.LatestDiagnosis.Evidence.Count>16));
                case "investigate":
                    // Read stored evidence only: no checkpoint changes, probes, process launches or dump uploads.
                    var logs=await store.ReadTelemetrySnapshotsAsync("event-log",now.AddHours(-24),now,256,token);
                    var events=new List<ViewEvent>();
                    foreach(var log in logs)
                    {
                        var value=ReadPayload<EventLogTelemetry>(log);
                        if(value is null)continue;
                        events.AddRange(value.Events.Where(x=>x.EventId is 1001 or 41 or 6008 && x.OccurredAtUtc is {} at && at<=now && now-at<=TimeSpan.FromHours(24))
                            .Select(x=>new ViewEvent(x.EventId,x.RecordId,x.OccurredAtUtc!.Value,x.BugCheckCode,SafeDump(x.CrashDumpFileName))));
                    }
                    return AdminProtocol.Reply(true,"investigate",new ViewInvestigation(now,
                        events.Distinct().OrderByDescending(x=>x.AtUtc).Take(16).ToArray(),await Heads(token),
                        "stored_metadata_only_latest_256_samples_no_driver_attribution"));
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch { /* Never echo exception text, paths, caller data or database payload. */ }
        return AdminProtocol.Reply(false,"view_request_rejected");
    }

    private async ValueTask<ViewMetric[]> Metrics(string name,DateTimeOffset from,DateTimeOffset through,int limit,CancellationToken token)
        =>(await store.ReadMetricSeriesAsync(name,"host",from,through,limit,token)).Where(x=>double.IsFinite(x.Value))
            .OrderBy(x=>x.SampledAtUtc).Select(x=>new ViewMetric(x.SampledAtUtc,x.Value)).ToArray();
    private static ViewMetric[] MinuteMinimum(ViewMetric[] samples)=>samples.GroupBy(x=>x.AtUtc.UtcTicks/TimeSpan.FromMinutes(1).Ticks)
        .OrderBy(x=>x.Key).Select(g=>new ViewMetric(g.Max(x=>x.AtUtc),g.Min(x=>x.Value))).ToArray();
    private async ValueTask<(T? Value,DateTimeOffset At)> Latest<T>(string collector,DateTimeOffset now,CancellationToken token) where T:class
    {
        var sample=(await store.ReadTelemetrySnapshotsAsync(collector,now.AddMinutes(-3),now,1,token)).FirstOrDefault();
        return sample is null?(null,default):(ReadPayload<T>(sample),sample.SampledAtUtc);
    }
    private static T? ReadPayload<T>(TelemetrySnapshot snapshot) where T:class
    {
        if(snapshot.PayloadSchemaVersion!=1)return null;
        try
        {
            using var doc=JsonDocument.Parse(snapshot.PayloadJson);
            var root=doc.RootElement;
            if(root.GetProperty("CollectorId").GetString()!=snapshot.CollectorId
                ||root.GetProperty("Status").GetInt32() is not (0 or 1))return null;
            return root.GetProperty("Value").Deserialize<T>();
        }
        catch(Exception error) when(error is JsonException or InvalidOperationException or KeyNotFoundException){return null;}
    }
    private async ValueTask<ViewIncident[]> Heads(CancellationToken token)
        =>(await store.ReadIncidentHeadsAsync(token)).OrderByDescending(x=>x.UpdatedAtUtc).Take(20).Select(Project).ToArray();
    private static ViewIncident Project(Incident x)=>new(x.IncidentId,x.State.ToString(),SafeCause(x.LatestDiagnosis.ProbableRootCause.Code),
        x.LatestDiagnosis.Confidence,x.LatestDiagnosis.Severity.ToString(),x.LatestDiagnosis.ObservedAtUtc,x.RecurrenceCount);
    private static ViewMetric? Current(ViewMetric[] points,DateTimeOffset now)=>points.LastOrDefault(x=>ViewFreshness.IsFresh(x.AtUtc,now));
    private static bool ValidDrive(string name)=>name.Length is 2 or 3 && char.IsAsciiLetter(name[0]) && name[1]==':' && (name.Length==2||name[2]=='\\');
    private static string SafeName(string value)=>new(value.Take(64).Select(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_'?c:'_').ToArray());
    private static bool IsCrash(string value)=>value=="windows_bugcheck"||value.StartsWith("bugcheck_",StringComparison.Ordinal);
    private static string SafeCause(string value)=>value is "cpu_host_saturation" or "cpu_process_saturation" or "memory_pressure" or "disk_capacity"
        or "service_stopped" or "service_crash" or "windows_bugcheck" or "bugcheck_irql_not_less_or_equal"
        or "gateway_unreachable" or "dns_resolution_failure" or "internet_unreachable" or "network_unavailable"?value:"other_diagnosis";
    private static string SafeSource(string value)=>value.StartsWith("eventlog",StringComparison.Ordinal)?"eventlog":
        value.StartsWith("cpu",StringComparison.Ordinal)?"cpu":value.StartsWith("memory",StringComparison.Ordinal)?"memory":
        value.StartsWith("disk",StringComparison.Ordinal)?"disk":value.StartsWith("service",StringComparison.Ordinal)?"service":"diagnostic";
    private static string? SafeDump(string? value)=>value is not null && value.Length<=64 && value.EndsWith(".dmp",StringComparison.OrdinalIgnoreCase)
        && value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_')?value:null;
}
