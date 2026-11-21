using HostAgent.Core.Contracts;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Core.Dashboard;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Core.Models.Incidents;
using System.Text.Json;
using System.Diagnostics;
namespace HostAgent.Service;

public sealed partial class DashboardDispatcher(IHostAgentStore store,ServiceConfiguration configuration,AgentRuntimeHealth health,
    TimeProvider clock,Action<DashboardDispatchDiagnostic>? reportDiagnostic=null)
{
    public async ValueTask<byte[]> DispatchAsync(byte[] payload,CancellationToken token)
    {
        try
        {
            var request=Timed("request_parse",()=>ViewProtocol.Parse(payload));var now=clock.GetUtcNow();
            switch(request.Command)
            {
                case "overview":
                    var cpu=await Timed("metric_cpu",()=>Metrics("cpu.usage.percent",now.AddMinutes(-6),now,128,token));
                    var memory=await Timed("metric_memory",()=>Metrics("memory.used.percent",now.AddMinutes(-6),now,128,token));
                    var uptime=await Timed("metric_uptime",()=>Metrics("system.uptime.seconds",now.AddMinutes(-3),now,1,token));
                    var cpuSource=await Timed("telemetry_cpu_memory",()=>Latest<CpuMemoryTelemetry>("cpu-memory",now,token));
                    var system=await Timed("telemetry_system",()=>Latest<SystemTelemetry>("system",now,token));
                    var disk=await Timed("telemetry_disk",()=>Latest<DiskTelemetry>("disk",now,token));
                    var process=await Timed("telemetry_process_services",()=>Latest<ProcessServiceTelemetry>("process-services",now,token));
                    var network=await Timed("telemetry_network",()=>Latest<NetworkTelemetry>("network",now,token));
                    var cpuPoint=CurrentMetric(cpuSource, cpuSource.Value?.CpuUsagePercent is >= 0 and <= 100
                        ? cpuSource.Value?.CpuUsagePercent : null, cpu, now,
                        cpuSource.Value?.CpuUsagePercent is not null || cpuSource.HasWarning("system_cpu_counter_invalid") ? "invalid_sample"
                            : cpuSource.HasWarning("system_cpu_counter_access_denied") ? "access_denied"
                            : cpuSource.HasWarning("cpu_baseline_not_ready") ? "waiting_for_sample" : "collector_unavailable",
                        "GetSystemTimes");
                    var memoryPoint=CurrentMetric(cpuSource, cpuSource.Value?.MemoryPressurePercent, memory, now,
                        cpuSource.HasWarning("global_memory_status_access_denied") ? "access_denied"
                            : cpuSource.HasWarning("global_memory_status_unavailable") ? "collector_unavailable" : "invalid_sample",
                        "GlobalMemoryStatusEx");
                    var uptimePoint=CurrentMetric(system, system.Value?.Uptime.TotalSeconds, uptime, now,
                        "invalid_sample", "system");
                    var volumes=(disk.Value?.Volumes ?? []).Where(x=>x is not null && x.HasValidCapacity && ValidDrive(x.RootPath)).ToArray();
                    var ramProcesses=UsableProcesses(process)?.Where(x=>x.MemoryBytes is >=0).ToArray();
                    var services=process.Value?.Services?.Where(x=>x is not null && !string.IsNullOrWhiteSpace(x.ServiceName)).ToArray();
                    var adapters=network.Value?.Adapters?.Where(x=>x is not null && Enum.TryParse<System.Net.NetworkInformation.OperationalStatus>(x.Status,out var status)
                        && Enum.IsDefined(status) && status!=System.Net.NetworkInformation.OperationalStatus.Unknown).ToArray();
                    var diskState=ListState(disk, volumes.Length, disk.Value?.Volumes?.Count, "volumes",
                        "volume_metrics_access_denied", "volume_metrics_invalid", "volume_not_ready", "volume_metrics_unavailable");
                    var processState=ProcessRamState(process,ramProcesses?.Length);
                    var serviceState=ListState(process, services?.Length, process.Value?.Services?.Count, "services",
                        "service_catalog_access_denied", "service_catalog_unavailable", "some_service_states_unavailable",
                        "some_service_start_modes_unavailable","some_service_triggers_unavailable");
                    if(serviceState.Status=="available" && services?.Any(x=>x.Status==HostServiceStatus.Unknown || !Enum.IsDefined(x.Status))==true)
                        serviceState=serviceState with {Status="partial",Reason="incomplete_sample"};
                    var networkState=ListState(network, adapters?.Length,network.Value?.Adapters?.Count,"network",
                        "some_network_adapter_details_access_denied", "some_network_adapter_details_unavailable","no_network_adapters_available");
                    var incidents=await Timed("incident_heads",()=>Heads(token));
                    return Reply("serialize_overview",true,"overview",new DashboardSnapshot(now,configuration.Mode.ToString(),configuration.ReadOnly,health.Degraded,
                        cpuPoint.Point,memoryPoint.Point,uptimePoint.Point,
                        volumes.Take(8).Select(x=>new ViewDisk(x.RootPath.TrimEnd('\\'),x.FreeBytes,x.CapacityBytes,disk.At!.Value)).ToArray(),
                        TopRam(ramProcesses,process.At),
                        services?.OrderBy(x=>x.ServiceName,StringComparer.Ordinal).Take(30).Select(x=>new ViewService(SafeName(x.ServiceName),
                            Enum.IsDefined(x.Status)?x.Status.ToString():"Unknown")).ToArray()??[],
                        networkState.Status=="available" ? adapters?.Count(x=>x.Status=="Up") : null,incidents,
                        health.Snapshot().Take(16).Select(x=>new ViewHealth(x.Component,x.Code,x.Degraded,x.Reasons)).ToArray(),MinuteMinimum(cpu),MinuteMinimum(memory))
                        {
                            KillSwitch=configuration.KillSwitch,
                            SupportsIncidentHistory=true,
                            DataState=new(cpuPoint.State,memoryPoint.State,uptimePoint.State,diskState,processState,serviceState,networkState),
                            DiskIoDiagnostics=disk.Value is not null && disk.Raw is {} raw
                                ? ReadDiskDiagnostics(raw) : new(null,"unavailable",[]),
                            NetworkChecks=network.Value?.Probes is {} probes && network.At is {} networkAt
                                ?new(networkAt,configuration.EnableNetworkProbes,probes
                                    .Where(p=>p is not null&&Enum.IsDefined(p.Kind)&&Enum.IsDefined(p.Status))
                                    .GroupBy(p=>p.Kind).Select(group=>group.First())
                                    .Select(p=>new ViewNetworkProbe(p.Kind.ToString(),p.Status.ToString())).ToArray())
                                :null
                        });
                case "disk-history":
                    var group=request.Body.GetProperty("group").GetString()!;
                    var diskHistory=await Timed("disk_history",()=>ReadDiskHistory(group,now,token));
                    return Reply("serialize_disk_history",true,"disk-history",diskHistory);
                case "process-load":
                    var loadSource=await Timed("process_load",()=>Latest<ProcessServiceTelemetry>("process-services",now,token));
                    return Reply("serialize_process_load",true,"process-load",ProjectProcessLoad(loadSource));
                case "history":
                    var metric=request.Body.GetProperty("metric").GetString()=="cpu"?"cpu.usage.percent":"memory.used.percent";
                    // Minimum supported interval is 5s: 24h has at most 17,281 samples.
                    var samples=await Timed("metric_history",()=>Metrics(metric,now.AddHours(-24),now,20_000,token));
                    if(samples.Length==20_000)return Reply("serialize_history",false,"view_history_capacity",null);
                    var points=samples.GroupBy(x=>x.AtUtc.UtcTicks/TimeSpan.FromMinutes(15).Ticks)
                        .OrderBy(x=>x.Key).Select(g=>new ViewMetric(g.Max(x=>x.AtUtc),g.Average(x=>x.Value))).TakeLast(97).ToArray();
                    return Reply("serialize_history",true,"history",points);
                case "incident-page":
                    var before = ViewProtocol.ReadIncidentCursor(request.Body);
                    var heads = await Timed("incident_page", () => store.ReadIncidentHistoryAsync(before, token));
                    var rows = heads.Take(IncidentHistoryCursor.PageSize).Select(head => Project(head) with
                    {
                        CreatedAtUtc = head.CreatedAtUtc,
                        UpdatedAtUtc = head.UpdatedAtUtc
                    }).ToArray();
                    var last = rows.LastOrDefault();
                    var next = heads.Count > IncidentHistoryCursor.PageSize && last?.CreatedAtUtc is { } created
                        ? new IncidentHistoryCursor(created.UtcTicks, last.Id) : null;
                    var page = Reply("serialize_incident_page", true, "incident-page", new ViewIncidentPage(rows, next));
                    return page.Length <= ViewProtocol.MaximumIncidentPageBytes
                        ? page : Reply("serialize_incident_page_limit", false, "view_incident_page_capacity", null);
                case "incident":
                    var id=request.Body.GetProperty("incidentId").GetString();
                    var incident=await Timed("incident_summary",()=>store.ReadIncidentSummaryAsync(id!,token));
                    if(incident is null)return Reply("serialize_incident",false,"view_incident_not_found",null);
                    return Reply("serialize_incident",true,"incident",new ViewIncidentDetail(Project(incident),
                        IsCrash(incident.LatestDiagnosis.ProbableRootCause.Code)?"stop_code_classification_only":"probable_cause_estimate",
                        incident.LatestDiagnosis.Evidence.Take(16).Select(x=>new ViewEvidence(SafeSource(x.Source),
                            x.NumericValue is {} v && double.IsFinite(v)?v:null,x.ObservedAtUtc)).ToArray(),incident.LatestDiagnosis.Evidence.Count>16));
                case "investigate":
                    // Read stored evidence only: no checkpoint changes, probes, process launches or dump uploads.
                    var logs=await Timed("telemetry_event_log",()=>store.ReadTelemetrySnapshotsAsync("event-log",now.AddHours(-24),now,256,token));
                    var eventSource=await Latest<EventLogTelemetry>("event-log",now,token);
                    var eventState=ListState(eventSource,eventSource.Value?.Events?.Count,eventSource.Value?.Events?.Count,"event-log",
                        eventSource.Warnings);
                    if(eventState.Status=="available" && (logs.Count==256 || logs.Any(log=>!IsCompleteEventSample(log))))
                        eventState=eventState with {Status="partial", Reason="incomplete_sample"};
                    var events=new List<ViewEvent>();
                    foreach(var log in logs)
                    {
                        var value=ReadPayload<EventLogTelemetry>(log);
                        if(value?.Events is null)continue;
                        events.AddRange(value.Events.Where(x=>x is not null && x.EventId is (1001 or 41 or 6008) && x.OccurredAtUtc is {} at && at<=now && now-at<=TimeSpan.FromHours(24))
                            .Select(x=>new ViewEvent(x.EventId,x.RecordId,x.OccurredAtUtc!.Value,x.BugCheckCode,SafeDump(x.CrashDumpFileName))));
                    }
                    var investigateHeads=await Timed("incident_heads",()=>Heads(token));
                    return Reply("serialize_investigation",true,"investigate",new ViewInvestigation(now,
                        events.Distinct().OrderByDescending(x=>x.AtUtc).Take(16).ToArray(),investigateHeads,
                        "stored_metadata_only_latest_256_samples_no_driver_attribution"){EventData=eventState});
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch { /* Never echo exception text, paths, caller data or database payload. */ }
        return Reply("serialize_rejected",false,"view_request_rejected",null);
    }

    private async ValueTask<T> Timed<T>(string stage,Func<ValueTask<T>> action)
    {
        var started=Stopwatch.GetTimestamp();
        try{var result=await action();Report(stage,"completed",started);return result;}
        catch(Exception error){Report(stage,FailureKind(error),started);throw;}
    }
    private T Timed<T>(string stage,Func<T> action)
    {
        var started=Stopwatch.GetTimestamp();
        try{var result=action();Report(stage,"completed",started);return result;}
        catch(Exception error){Report(stage,FailureKind(error),started);throw;}
    }
    private byte[] Reply(string stage,bool ok,string code,object? data)
    {
        var started=Stopwatch.GetTimestamp();
        try{var result=AdminProtocol.Reply(ok,code,data);Report(stage,"completed",started);return result;}
        catch(Exception error){Report(stage,FailureKind(error),started);throw;}
    }
    private void Report(string stage,string kind,long started)
    {
        try{reportDiagnostic?.Invoke(new(stage,kind,(long)Math.Max(0,Stopwatch.GetElapsedTime(started).TotalMilliseconds)));}catch{}
    }
    private static string FailureKind(Exception error)=>error switch
    {
        OperationCanceledException=>"cancelled",
        IOException=>"io",
        UnauthorizedAccessException=>"unauthorized",
        JsonException or InvalidDataException=>"invalid_data",
        InvalidOperationException=>"invalid_operation",
        _=>"unexpected"
    };

    private async ValueTask<ViewMetric[]> Metrics(string name,DateTimeOffset from,DateTimeOffset through,int limit,CancellationToken token)
        =>(await store.ReadMetricSeriesAsync(name,"host",from,through,limit,token)).Where(x=>double.IsFinite(x.Value) && x.Value>=0
            && (name=="system.uptime.seconds" || x.Value<=100))
            .OrderBy(x=>x.SampledAtUtc).Select(x=>new ViewMetric(x.SampledAtUtc,x.Value)).ToArray();
    private static ViewMetric[] MinuteMinimum(ViewMetric[] samples)=>samples.GroupBy(x=>x.AtUtc.UtcTicks/TimeSpan.FromMinutes(1).Ticks)
        .OrderBy(x=>x.Key).Select(g=>new ViewMetric(g.Max(x=>x.AtUtc),g.Min(x=>x.Value))).ToArray();
    private static bool IsCompleteEventSample(TelemetrySnapshot sample)
    {
        var value=ReadPayload<EventLogTelemetry>(sample);
        if(value?.Events is null || value.Events.Any(x=>x is null))return false;
        using var doc=JsonDocument.Parse(sample.PayloadJson);
        return doc.RootElement.GetProperty("Status").GetInt32()==(int)CollectorStatus.Success;
    }

    private static ViewDiskIoDiagnostics ReadDiskDiagnostics(TelemetrySnapshot sample)
    {
        using var doc=JsonDocument.Parse(sample.PayloadJson);
        return DiskDiagnostics(doc.RootElement.GetProperty("Value"),sample.SampledAtUtc);
    }

    private static ViewDiskIoDiagnostics DiskDiagnostics(JsonElement value,DateTimeOffset at)
    {
        var invalid=new ViewDiskIoDiagnostics(at,"invalid",[]);
        var absent=new ViewDiskIoDiagnostics(at,"not_recorded",[]);
        if(!value.TryGetProperty("IoCounters",out var io)||io.ValueKind==JsonValueKind.Null)return absent;
        if(io.ValueKind!=JsonValueKind.Object)return invalid;
        if(!io.TryGetProperty("Diagnostics",out var list)||list.ValueKind==JsonValueKind.Null)return absent;
        if(list.ValueKind!=JsonValueKind.Array||list.GetArrayLength()>10)return invalid;
        if(list.GetArrayLength()==0)return absent;
        var records=new List<ViewDiskIoDiagnostic>(10);
        var seen=new HashSet<(int,int)>();
        foreach(var item in list.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Object
                ||!item.TryGetProperty("Stage",out var stageValue)||stageValue.ValueKind!=JsonValueKind.Number||!stageValue.TryGetInt32(out var stage)||stage is <0 or >5
                ||!item.TryGetProperty("Counter",out var counterValue)||counterValue.ValueKind!=JsonValueKind.Number||!counterValue.TryGetInt32(out var counter)||counter is <0 or >3
                ||!item.TryGetProperty("Status",out var statusValue)||statusValue.ValueKind!=JsonValueKind.Number||!statusValue.TryGetUInt32(out var status)
                ||!seen.Add((stage,counter)))return invalid;
            if((stage is 1 or 4)!=(counter is 1 or 2 or 3))return invalid;
            uint? dataStatus=null;
            if(item.TryGetProperty("DataStatus",out var data)&&data.ValueKind!=JsonValueKind.Null)
            {
                if(data.ValueKind!=JsonValueKind.Number||!data.TryGetUInt32(out var parsed)
                    ||stage!=4||status is not (0 or 0xC0000BC6u))return invalid;
                dataStatus=parsed;
            }
            bool? finite=null;
            if(item.TryGetProperty("ValueFinite",out var finiteValue)&&finiteValue.ValueKind!=JsonValueKind.Null)
            {
                if(finiteValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                    ||stage!=4||status!=0||dataStatus is not (0 or 1))return invalid;
                finite=finiteValue.GetBoolean();
            }
            var stageName=stage switch{0=>"open_query",1=>"add_counter",2=>"collect_first",3=>"collect_second",4=>"format_value",_=>"close_query"};
            var counterName=counter switch{1=>"read",2=>"write",3=>"latency",_=>"none"};
            records.Add(new(stageName,counterName,status,dataStatus,finite));
        }
        return new(at,"available",records.AsReadOnly());
    }

    private async ValueTask<SourceSample<T>> Latest<T>(string collector,DateTimeOffset now,CancellationToken token) where T:class
    {
        var attempt=await store.ReadLatestServiceHealthAsync(collector,now,token);
        var raw=(await store.ReadTelemetrySnapshotsAsync(collector,DateTimeOffset.MinValue,now,1,token)).FirstOrDefault();
        if(attempt is not null && (raw is null || attempt.ObservedAtUtc>=raw.SampledAtUtc))
        {
            var at=attempt.ObservedAtUtc;
            if(!ViewFreshness.IsFresh(at,now))return new(null,null,at,"stale","sample_expired",[]);
            if(attempt.Status is not ("Success" or "Partial"))
                return new(null,null,at,"unavailable",ReadFailureReason(attempt.PayloadJson),[]);
            if(raw?.SampledAtUtc!=at)return new(null,null,at,"unknown","missing_sample",[]);
        }
        if(raw is null)return new(null,null,null,"unknown","missing_sample",[]);
        if(!ViewFreshness.IsFresh(raw.SampledAtUtc,now))
            return new(null,null,raw.SampledAtUtc,"stale","sample_expired",[]);
        var value=typeof(T)==typeof(DiskTelemetry) ? ReadDiskPayload(raw) as T : ReadPayload<T>(raw);
        if(value is null)return new(null,null,raw.SampledAtUtc,"unavailable","invalid_sample",[]);
        using var doc=JsonDocument.Parse(raw.PayloadJson);
        var root=doc.RootElement;
        if(value is SystemTelemetry && !root.GetProperty("Value").TryGetProperty("Uptime",out _))
            return new(null,null,raw.SampledAtUtc,"unavailable","invalid_sample",[]);
        var warnings=root.TryGetProperty("Warnings",out var list)&&list.ValueKind==JsonValueKind.Array
            ? list.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()!).ToArray()
            : [];
        var partial=root.GetProperty("Status").GetInt32()==(int)CollectorStatus.Partial
            || (attempt is {Status:"Partial"} && attempt.ObservedAtUtc==raw.SampledAtUtc);
        return new(value,raw,raw.SampledAtUtc,partial?"partial":"available",partial?"incomplete_sample":null,warnings);
    }

    private static string ReadFailureReason(string json)
    {
        try
        {
            using var doc=JsonDocument.Parse(json);
            if(doc.RootElement.TryGetProperty("ErrorCode",out var code) && code.ValueKind==JsonValueKind.String)
                return code.GetString() switch
                {
                    "global_memory_status_access_denied" or "volume_enumeration_denied" or "volume_metrics_access_denied"
                        or "event_logs_access_denied" or "network_adapters_access_denied"=>"access_denied",
                    "volume_metrics_invalid"=>"invalid_sample",
                    "collector_timeout"=>"read_timeout",
                    _=>"collector_unavailable"
                };
        }
        catch(JsonException) { }
        catch(InvalidOperationException) { }
        return "collector_unavailable";
    }

    private static (ViewMetric? Point,ViewDataState State) CurrentMetric<T>(SourceSample<T> sample,double? value,
        ViewMetric[] series,DateTimeOffset now,string missingReason,string source) where T:class
    {
        if(sample.Value is not null)
            return value is >=0 and < double.PositiveInfinity && sample.At is {} at
                ? (new ViewMetric(at,value.Value),new(source,at,"available",null))
                : (null,new(source,sample.At,missingReason=="waiting_for_sample"?"unknown":"unavailable",missingReason));
        // Older stores/fixtures may have a metric without any collector envelope.
        // A newer collector attempt always takes precedence over that fallback.
        if(sample.At is null && series.LastOrDefault() is {} legacy)
            return ViewFreshness.IsFresh(legacy.AtUtc,now)
                ? (legacy,new(source,legacy.AtUtc,"partial","legacy_sample"))
                : (null,new(source,legacy.AtUtc,"stale","sample_expired"));
        return (null,sample.Describe(source));
    }

    private static ViewDataState ListState<T>(SourceSample<T> sample,int? count,int? rawCount,string source,
        params string[] relevantWarnings) where T:class
    {
        if(sample.Value is null)return sample.Describe(source);
        if(count is null)return new(source,sample.At,"unavailable","invalid_sample");
        var warnings=relevantWarnings.Where(sample.HasWarning).ToArray();
        var denied=warnings.Any(code=>code.EndsWith("_access_denied",StringComparison.Ordinal));
        var invalid=count!=rawCount || warnings.Contains("volume_metrics_invalid");
        var limited=invalid || warnings.Length>0 || (sample.Status=="partial" && sample.Warnings.Length==0);
        if(!limited)return new(source,sample.At,"available",null);
        var reason=denied?"access_denied":invalid?"invalid_sample":"incomplete_sample";
        return new(source,sample.At,count==0&&(invalid||warnings.Length>0)?"unavailable":"partial",reason);
    }

    private static ProcessTelemetry[]? UsableProcesses(SourceSample<ProcessServiceTelemetry> sample)
        => sample.Value?.Processes?.Where(row=>row is not null && row.ProcessId>=0
            && !string.IsNullOrWhiteSpace(row.ProcessName)).ToArray();

    private static ViewProcess[] TopRam(ProcessTelemetry[]? rows,DateTimeOffset? at)
        => rows?.OrderByDescending(row=>row.MemoryBytes).ThenBy(row=>row.ProcessId).Take(10)
            .Select(row=>ProjectProcess(row,at)).ToArray()??[];

    private static ViewDataState ProcessRamState(SourceSample<ProcessServiceTelemetry> sample,int? count)
        => ListState(sample,count,sample.Value?.Processes?.Count,"processes",
            "process_list_access_denied","process_list_unavailable","some_process_metrics_access_denied",
            "some_process_metrics_unavailable","some_process_start_times_unavailable",
            "some_executable_paths_unavailable","process_list_truncated");

    private static ViewProcessLoad ProjectProcessLoad(SourceSample<ProcessServiceTelemetry> sample)
    {
        var rows=UsableProcesses(sample);
        var cpu=rows?.Where(row=>VerifiedProcessCpu(row,sample.At) is not null).ToArray();
        var ram=rows?.Where(row=>row.MemoryBytes is >=0).ToArray();
        var topCpu=cpu?.OrderByDescending(row=>row.CpuUsagePercent).ThenBy(row=>row.ProcessId).Take(10)
            .Select(row=>ProjectProcess(row,sample.At)).ToArray()??[];
        return new(topCpu,TopRam(ram,sample.At),rows?.Length??0,cpu?.Length??0,ram?.Length??0,
            ProcessCpuState(sample,rows?.Length,cpu?.Length),ProcessRamState(sample,ram?.Length));
    }

    private static double? VerifiedProcessCpu(ProcessTelemetry process,DateTimeOffset? sampledAt)
        => process.CpuIdentityVerified && process.StartedAtUtc is {UtcTicks:>0} start && sampledAt is {} at
            && start<=at && process.CpuUsagePercent is >=0 and <=100 ? process.CpuUsagePercent : null;

    private static ViewProcess ProjectProcess(ProcessTelemetry process,DateTimeOffset? sampledAt)
        => new(process.ProcessId,SafeName(process.ProcessName),VerifiedProcessCpu(process,sampledAt),
            process.MemoryBytes is >=0 ? process.MemoryBytes : null)
        {
            StartTimeUtcTicks=process.StartedAtUtc is {UtcTicks:>0} start && sampledAt is {} at && start<=at
                ? start.UtcTicks : null
        };

    private static ViewDataState ProcessCpuState(SourceSample<ProcessServiceTelemetry> sample,int? count,int? measured)
    {
        const string source="processes";
        if(sample.Value is null)return sample.Describe(source);
        if(count is null||measured is null)return new(source,sample.At,"unavailable","invalid_sample");
        var denied=sample.HasWarning("process_start_time_access_denied")||sample.HasWarning("process_cpu_time_access_denied")
            ||sample.HasWarning("process_list_access_denied");
        var incomplete=sample.HasWarning("process_list_truncated")||sample.HasWarning("some_process_metrics_unavailable")
            ||sample.HasWarning("some_process_start_times_unavailable");
        if(measured==0)
        {
            if(denied)return new(source,sample.At,"unavailable","access_denied");
            if(sample.HasWarning("process_list_unavailable"))return new(source,sample.At,"unavailable","collector_unavailable");
            if(count==0 && sample.Value.Processes.Count==0)
                return incomplete?new(source,sample.At,"unavailable","incomplete_sample"):new(source,sample.At,"available",null);
            return new(source,sample.At,"unknown","waiting_for_sample");
        }
        return measured<count||count!=sample.Value.Processes.Count||incomplete||denied
            ?new(source,sample.At,"partial",denied?"access_denied":"incomplete_sample")
            :new(source,sample.At,"available",null);
    }

    private sealed record SourceSample<T>(T? Value,TelemetrySnapshot? Raw,DateTimeOffset? At,string Status,string? Reason,string[] Warnings)
        where T:class
    {
        public bool HasWarning(string code)=>Warnings.Contains(code,StringComparer.Ordinal);
        public ViewDataState Describe(string source)=>new(source,At,Status,Reason);
    }
    private static DiskTelemetry? ReadDiskPayload(TelemetrySnapshot sample)
    {
        if(sample.PayloadSchemaVersion!=1)return null;
        try
        {
            using var doc=JsonDocument.Parse(sample.PayloadJson);
            var root=doc.RootElement;
            if(root.GetProperty("CollectorId").GetString()!="disk"||root.GetProperty("Status").GetInt32() is not (0 or 1))
                return null;
            var volumes=root.GetProperty("Value").GetProperty("Volumes").Deserialize<DiskVolumeTelemetry[]>();
            return volumes is null?null:new(volumes,null,[]);
        }
        catch(Exception error) when(error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException){return null;}
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
        catch(Exception error) when(error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException){return null;}
    }
    private async ValueTask<ViewIncident[]> Heads(CancellationToken token)
        =>(await store.ReadIncidentSummariesAsync(20,token)).Select(Project).ToArray();
    private static ViewIncident Project(Incident x)=>new(x.IncidentId,x.State.ToString(),SafeCause(x.LatestDiagnosis.ProbableRootCause.Code),
        x.LatestDiagnosis.Confidence,x.LatestDiagnosis.Severity.ToString(),x.LatestDiagnosis.ObservedAtUtc,x.RecurrenceCount)
        {AffectedService=SafeAffectedService(x.LatestDiagnosis.ProbableRootCause.Code,x.LatestDiagnosis.ProbableRootCause.Subject)};
    private static bool ValidDrive(string? name)=>name is {Length:2 or 3} && char.IsAsciiLetter(name[0]) && name[1]==':' && (name.Length==2||name[2]=='\\');
    private static string SafeName(string value)=>new(value.Take(64).Select(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_'?c:'_').ToArray());
    private static bool IsCrash(string value)=>value=="windows_bugcheck"||value.StartsWith("bugcheck_",StringComparison.Ordinal);
    private static string? SafeAffectedService(string code,string? subject)
        => code is "service_stopped" or "service_crash"
            && subject is {Length:>0 and <=128}
            && subject.All(character=>char.IsAsciiLetterOrDigit(character)||character is '.' or '_' or '-')
                ?subject:null;
    private static string SafeCause(string value)=>value is "cpu_host_saturation" or "cpu_process_saturation" or "memory_pressure" or "disk_capacity"
        or "service_stopped" or "service_crash" or "windows_bugcheck" or "bugcheck_irql_not_less_or_equal"
        or "gateway_failure" or "dns_failure" or "internet_connectivity_failure"
        or "gateway_unreachable" or "dns_resolution_failure" or "internet_unreachable" or "network_unavailable"?value:"other_diagnosis";
    private static string SafeSource(string value)=>value.StartsWith("eventlog",StringComparison.Ordinal)?"eventlog":
        value.StartsWith("cpu",StringComparison.Ordinal)?"cpu":value.StartsWith("memory",StringComparison.Ordinal)?"memory":
        value.StartsWith("disk",StringComparison.Ordinal)?"disk":value.StartsWith("service",StringComparison.Ordinal)?"service":"diagnostic";
    private static string? SafeDump(string? value)=>value is not null && value.Length<=64 && value.EndsWith(".dmp",StringComparison.OrdinalIgnoreCase)
        && value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_')?value:null;
}
public sealed record DashboardDispatchDiagnostic(string Stage,string Kind,long ElapsedMilliseconds);
