using System.Text;
using System.Text.Json;
using HostAgent.Core.Dashboard;
using HostAgent.Core.Models.Diagnosis;
using HostAgent.Core.Models.Incidents;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Administration;
using HostAgent.Service;
using HostAgent.Core.Orchestration;
using HostAgent.Core.Contracts;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class DashboardViewTests
{
    [Fact]
    public async Task Overview_pipe_returns_committed_incident_while_WAL_writer_is_open()
    {
        await using var f = await OperatingModeTests.Fixture.Create();
        var expected = Assert.Single(await f.Store.ReadIncidentHeadsAsync(default));
        await using var writer = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={f.Path};Pooling=False;Cache=Shared");
        await writer.OpenAsync();
        await using var transaction = writer.BeginTransaction();
        await using var update = writer.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE incidents SET payload_json='{}';";
        await update.ExecuteNonQueryAsync();
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var sid = identity.User!;
        var name = "HostAgent-view-wal-" + Guid.NewGuid().ToString("N");
        var dispatcher = new DashboardDispatcher(f.Store, new(), new(), TimeProvider.System);
        var server = new AdminPipeServer(name, sid, sid, dispatcher.DispatchAsync, TimeSpan.FromSeconds(3));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var serving = Task.Run(() => server.ServeOnceAsync(stop.Token));
        try
        {
            var client = new ViewPipeClient(name, sid, TimeSpan.FromSeconds(3));
            using var reply = JsonDocument.Parse(await client.SendAsync(ViewProtocol.Request("overview", new { }), stop.Token));
            Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
            var data = reply.RootElement.GetProperty("data");
            Assert.Equal("Shadow", data.GetProperty("Mode").GetString());
            Assert.True(data.GetProperty("ReadOnly").GetBoolean());
            var incident = Assert.Single(data.GetProperty("Incidents").EnumerateArray());
            Assert.Equal(expected.IncidentId, incident.GetProperty("Id").GetString());
            Assert.Equal(expected.LatestDiagnosis.Confidence, incident.GetProperty("Confidence").GetInt32());
        }
        finally
        {
            stop.Cancel();
            await transaction.RollbackAsync();
            await serving.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Idle_view_listener_does_not_retire_at_a_request_deadline_boundary()
    {
        using var identity=System.Security.Principal.WindowsIdentity.GetCurrent();var sid=identity.User!;
        var name="HostAgent-view-idle-stable-"+Guid.NewGuid().ToString("N");
        using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server=new AdminPipeServer(name,sid,sid,(_,_)=>ValueTask.FromResult(AdminProtocol.Reply(true,"overview")),
            TimeSpan.FromMilliseconds(500));
        var serving=server.ServeOnceAsync(stop.Token);
        try
        {
            await Task.Delay(750);
            Assert.False(serving.IsCompleted,"An idle listener retired before a client connected; a concurrent connect can get a closing pipe.");
            using var response=JsonDocument.Parse(await new ViewPipeClient(name,sid,TimeSpan.FromSeconds(2))
                .SendAsync(ViewProtocol.Request("overview",new{}),stop.Token));
            Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
            await serving;
        }
        finally{stop.Cancel();await serving;}
    }

    [Theory]
    [InlineData("gateway_failure", 90)]
    [InlineData("dns_failure", 90)]
    [InlineData("internet_connectivity_failure", 80)]
    public async Task Overview_preserves_network_diagnosis_code_and_confidence(string rootCauseCode, int confidence)
    {
        await using var f = await OperatingModeTests.Fixture.Create();
        var now = DateTimeOffset.UtcNow;
        var diagnosis = new Diagnosis(
            Guid.NewGuid().ToString("N"),
            "network:" + rootCauseCode,
            now,
            DiagnosisSeverity.High,
            confidence,
            [],
            new ProbableRootCause(rootCauseCode, "Synthetic network diagnosis", null),
            [],
            new RemediationRecommendation("inspect_network", "Review network connectivity."),
            IncidentLifecycleState.Detected);
        var incident = new Incident(diagnosis.DiagnosisId, diagnosis.CorrelationKey, IncidentLifecycleState.Detected,
            now, now, 1, diagnosis, []);
        await f.Store.SaveIncidentBundleAsync(incident, default);

        var bytes = await new DashboardDispatcher(f.Store, new(), new(), TimeProvider.System)
            .DispatchAsync(ViewProtocol.Request("overview", new { }), default);

        using var reply = JsonDocument.Parse(bytes);
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        var projected = Assert.Single(reply.RootElement.GetProperty("data").GetProperty("Incidents").EnumerateArray(),
            item => item.GetProperty("Id").GetString() == incident.IncidentId);
        Assert.Equal(rootCauseCode, projected.GetProperty("Code").GetString());
        Assert.Equal(confidence, projected.GetProperty("Confidence").GetInt32());
    }

    [Fact]
    public async Task Overview_reports_stage_timings_without_payload_or_exception_text()
    {
        await using var f=await OperatingModeTests.Fixture.Create();var diagnostics=new List<DashboardDispatchDiagnostic>();
        var dispatcher=new DashboardDispatcher(f.Store,new(),new(),TimeProvider.System,diagnostics.Add);
        using var response=JsonDocument.Parse(await dispatcher.DispatchAsync(ViewProtocol.Request("overview",new{}),default));
        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains(diagnostics,x=>x.Stage=="metric_cpu");
        Assert.Contains(diagnostics,x=>x.Stage=="telemetry_disk");
        Assert.Contains(diagnostics,x=>x.Stage=="incident_heads");
        Assert.Contains(diagnostics,x=>x.Stage=="serialize_overview");
        Assert.All(diagnostics,x=>Assert.InRange(x.ElapsedMilliseconds,0,30_000));
        var safe=JsonSerializer.Serialize(diagnostics);
        Assert.DoesNotContain(f.Path,safe,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload",safe,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception",safe,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Five_second_collection_preserves_sustained_recommendations_and_complete_24hour_history_with_bounded_frames()
    {
        await using var f=await OperatingModeTests.Fixture.Create();var now=DateTimeOffset.UtcNow;
        await using(var connection=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={f.Path};Pooling=False"))
        {
            await connection.OpenAsync();await using var transaction=connection.BeginTransaction();
            await using var command=connection.CreateCommand();command.Transaction=transaction;
            command.CommandText="INSERT INTO baseline_samples(sample_id,metric_name,scope,sampled_at_utc,value) VALUES($id,'cpu.usage.percent','host',$at,95)";
            var id=command.Parameters.Add("$id",Microsoft.Data.Sqlite.SqliteType.Text);var at=command.Parameters.Add("$at",Microsoft.Data.Sqlite.SqliteType.Text);
            for(var i=0;i<=17280;i++){id.Value=Guid.NewGuid().ToString("D");at.Value=now.AddSeconds(-i*5).ToString("O",System.Globalization.CultureInfo.InvariantCulture);await command.ExecuteNonQueryAsync();}
            await transaction.CommitAsync();
        }
        var dispatcher=new DashboardDispatcher(f.Store,new(){CollectionIntervalSeconds=5},new(),TimeProvider.System);
        var bytes=await dispatcher.DispatchAsync(ViewProtocol.Request("overview",new{}),default);
        using(var reply=JsonDocument.Parse(bytes))
        {
            Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
            var snapshot=reply.RootElement.GetProperty("data").Deserialize<DashboardSnapshot>()!;
            Assert.Contains(new RecommendationEngine().Evaluate(snapshot,DateTimeOffset.UtcNow),x=>x.Code=="sustained_cpu");
            Assert.True(bytes.Length<=16384);
        }
        using var history=JsonDocument.Parse(await dispatcher.DispatchAsync(ViewProtocol.Request("history",new{metric="cpu"}),default));
        var points=history.RootElement.GetProperty("data").Deserialize<ViewMetric[]>()!;
        Assert.True(points.Min(x=>x.AtUtc)<now.AddHours(-23));Assert.InRange(points.Length,96,97);
    }
    [Fact]
    public async Task Actual_interactive_pipe_authenticates_read_access_and_rejects_wrong_owner()
    {
        using var identity=System.Security.Principal.WindowsIdentity.GetCurrent();var sid=identity.User!;
        var interactive=new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.InteractiveSid,null);
        Assert.True(new System.Security.Principal.WindowsPrincipal(identity).IsInRole(interactive));
        var name="HostAgent-view-test-"+Guid.NewGuid().ToString("N");var calls=0;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var serving=new HostAgent.Infrastructure.Administration.AdminPipeServer(name,sid,interactive,(payload,token)=>
        { Assert.Equal("overview",ViewProtocol.Parse(payload).Command);calls++;return ValueTask.FromResult(HostAgent.Infrastructure.Administration.AdminProtocol.Reply(true,"overview")); },TimeSpan.FromSeconds(2)).ServeOnceAsync(timeout.Token);
        var bytes=await new ViewPipeClient(name,sid,TimeSpan.FromSeconds(2)).SendAsync(ViewProtocol.Request("overview",new{}),timeout.Token);
        await serving;using var reply=JsonDocument.Parse(bytes);Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());Assert.Equal(1,calls);
        serving=new HostAgent.Infrastructure.Administration.AdminPipeServer(name,sid,interactive,(_,_)=>{calls++;return ValueTask.FromResult(Array.Empty<byte>());},TimeSpan.FromSeconds(2)).ServeOnceAsync(timeout.Token);
        using var mismatch=JsonDocument.Parse(await new ViewPipeClient(name,new("S-1-5-21-1-2-3-9999"),TimeSpan.FromSeconds(2)).SendAsync(ViewProtocol.Request("overview",new{}),timeout.Token));
        await serving;Assert.False(mismatch.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("view_identity_mismatch",mismatch.RootElement.GetProperty("code").GetString());Assert.Equal(1,calls);
    }

    [Fact]
    public async Task View_worker_accepts_a_fresh_connection_after_a_timed_out_dispatch()
    {
        using var identity=System.Security.Principal.WindowsIdentity.GetCurrent();var sid=identity.User!;
        var name="HostAgent-view-reconnect-"+Guid.NewGuid().ToString("N");var calls=0;
        var failures=new List<PipeServerDiagnostic>();
        var server=new AdminPipeServer(name,sid,sid,async(_,token)=>
        {
            if(Interlocked.Increment(ref calls)==1)await Task.Delay(Timeout.InfiniteTimeSpan,token);
            return AdminProtocol.Reply(true,"overview");
        },TimeSpan.FromMilliseconds(300),reportDiagnostic:failures.Add);
        using var worker=new DashboardWorker(server.ServeOnceAsync);
        await worker.StartAsync(default);
        try
        {
            var client=new ViewPipeClient(name,sid,TimeSpan.FromSeconds(2));
            using var first=JsonDocument.Parse(await client.SendAsync(ViewProtocol.Request("overview",new{}),default));
            Assert.False(first.RootElement.GetProperty("ok").GetBoolean());
            using var recovered=JsonDocument.Parse(await client.SendAsync(ViewProtocol.Request("overview",new{}),default));
            Assert.True(recovered.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(2,calls);
            var failure=Assert.Single(failures);Assert.Equal("dispatch",failure.Stage);Assert.Equal("server_deadline",failure.Kind);
        }
        finally{await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(3));}
    }

    [Fact]
    public async Task Idle_listener_time_does_not_consume_an_authenticated_views_request_deadline()
    {
        using var identity=System.Security.Principal.WindowsIdentity.GetCurrent();var sid=identity.User!;
        var name="HostAgent-view-late-"+Guid.NewGuid().ToString("N");
        PipeServerDiagnostic? diagnostic=null;
        using var lifetime=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var serving=new AdminPipeServer(name,sid,sid,async(_,token)=>
        {
            await Task.Delay(1200,token);
            return AdminProtocol.Reply(true,"overview");
        },TimeSpan.FromSeconds(2),reportDiagnostic:value=>diagnostic=value).ServeOnceAsync(lifetime.Token);
        await Task.Delay(1200,lifetime.Token);
        using var reply=JsonDocument.Parse(await new ViewPipeClient(name,sid,TimeSpan.FromSeconds(3))
            .SendAsync(ViewProtocol.Request("overview",new{}),lifetime.Token));
        await serving;
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean(),
            $"client={reply.RootElement.GetProperty("code").GetString()}; server={diagnostic?.Stage}/{diagnostic?.Kind}");
        Assert.Null(diagnostic);
    }

    [Fact]
    public async Task Client_timeout_and_server_dispatch_deadline_are_reported_as_separate_safe_stages()
    {
        var sid=System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var interactive=new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.InteractiveSid,null);
        var name="HostAgent-view-timeout-"+Guid.NewGuid().ToString("N");
        PipeServerDiagnostic? serverDiagnostic=null;PipeClientDiagnostic? clientDiagnostic=null;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var serving=new HostAgent.Infrastructure.Administration.AdminPipeServer(name,sid,interactive,
            async(_,token)=>{await Task.Delay(Timeout.InfiniteTimeSpan,token);return Array.Empty<byte>();},
            TimeSpan.FromMilliseconds(800),reportDiagnostic:value=>serverDiagnostic=value).ServeOnceAsync(deadline.Token);
        var bytes=await new ViewPipeClient(name,sid,TimeSpan.FromMilliseconds(250),
            reportDiagnostic:value=>clientDiagnostic=value).SendAsync(ViewProtocol.Request("overview",new{}),deadline.Token);
        await serving;using var reply=JsonDocument.Parse(bytes);
        Assert.False(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("view_unavailable",reply.RootElement.GetProperty("code").GetString());
        Assert.Equal("response_frame",clientDiagnostic?.Stage);Assert.Equal("client_deadline",clientDiagnostic?.Kind);
        Assert.Equal("dispatch",serverDiagnostic?.Stage);Assert.Equal("server_deadline",serverDiagnostic?.Kind);
        Assert.InRange(clientDiagnostic!.ElapsedMilliseconds,150,1500);
        Assert.InRange(serverDiagnostic!.ElapsedMilliseconds,600,2000);
        Assert.DoesNotContain("payload",JsonSerializer.Serialize(clientDiagnostic),StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Caller_cancelled_view_reports_failure_stage_without_losing_diagnostics()
    {
        using var identity=System.Security.Principal.WindowsIdentity.GetCurrent();var sid=identity.User!;
        var name="HostAgent-view-caller-cancel-"+Guid.NewGuid().ToString("N");
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PipeClientDiagnostic? diagnostic=null;
        using var serverLifetime=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var caller=new CancellationTokenSource();
        var serving=new HostAgent.Infrastructure.Administration.AdminPipeServer(name,sid,sid,
            async(_,token)=>{entered.TrySetResult();await Task.Delay(Timeout.InfiniteTimeSpan,token);return Array.Empty<byte>();},
            TimeSpan.FromSeconds(4)).ServeOnceAsync(serverLifetime.Token);
        try
        {
            var pending=new ViewPipeClient(name,sid,TimeSpan.FromSeconds(5),value=>diagnostic=value)
                .SendAsync(ViewProtocol.Request("overview",new{}),caller.Token).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            caller.Cancel();
            using var reply=JsonDocument.Parse(await pending.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("view_unavailable",reply.RootElement.GetProperty("code").GetString());
            Assert.NotNull(diagnostic);
            Assert.Equal("response_frame",diagnostic.Stage);
            Assert.Equal("caller_cancelled",diagnostic.Kind);
            Assert.DoesNotContain(name,JsonSerializer.Serialize(diagnostic));
        }
        finally{serverLifetime.Cancel();await serving.WaitAsync(TimeSpan.FromSeconds(2));}
    }

    [Fact]
    public async Task Investigation_returns_wer_identity_without_raw_xml_and_never_moves_checkpoint()
    {
        await using var f=await OperatingModeTests.Fixture.Create();var now=DateTimeOffset.UtcNow;
        await f.Store.SaveCollectorCheckpointAsync("event-log","System","synthetic-checkpoint",now,default);
        var evt=new EventLogEntryMetadata("System","Microsoft-Windows-WER-SystemErrorReporting",123,1001,"Error",now.AddMinutes(-11),"private-machine")
            {BugCheckCode=10,CrashDumpFileName="101226-1234-01.dmp"};
        await Seed(f.Store,"event-log",new EventLogTelemetry(now,[evt],[]),now);
        using var reply=JsonDocument.Parse(await new DashboardDispatcher(f.Store,new(),new(),TimeProvider.System).DispatchAsync(ViewProtocol.Request("investigate",new{}),default));
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(123,reply.RootElement.GetProperty("data").GetProperty("Events")[0].GetProperty("RecordId").GetInt32());
        Assert.DoesNotContain("private-machine",reply.RootElement.GetRawText());
        Assert.Equal("synthetic-checkpoint",await f.Store.ReadCollectorCheckpointAsync("event-log","System",default));
    }

    [Theory]
    [InlineData("overview")]
    [InlineData("investigate")]
    public void Accepts_only_bounded_read_only_commands(string command)
        => Assert.Equal(command,ViewProtocol.Parse(ViewProtocol.Request(command,new{})).Command);

    [Theory]
    [InlineData("{\"version\":1,\"command\":\"execute\",\"body\":{}}")]
    [InlineData("{\"version\":1,\"command\":\"secret-set\",\"body\":{}}")]
    [InlineData("{\"version\":1,\"command\":\"overview\",\"body\":{\"x\":1}}")]
    [InlineData("{\"version\":1,\"command\":\"overview\",\"command\":\"investigate\",\"body\":{}}")]
    [InlineData("{\"version\":1,\"command\":\"history\",\"body\":{\"metric\":\"C:\\\\sensitive\"}}")]
    public void Rejects_mutations_unknown_fields_duplicates_and_arbitrary_series(string json)
        => Assert.Throws<InvalidDataException>(()=>ViewProtocol.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public async Task Overview_reads_durable_metrics_without_exposing_process_paths_or_network_addresses()
    {
        await using var f=await OperatingModeTests.Fixture.Create();
        var now=DateTimeOffset.UtcNow;
        await f.Store.SaveMetricSampleAsync(new(Guid.NewGuid(),"cpu.usage.percent","host",now.AddSeconds(-10),2),default);
        await f.Store.SaveMetricSampleAsync(new(Guid.NewGuid(),"memory.used.percent","host",now.AddSeconds(-10),1),default);
        await f.Store.SaveMetricSampleAsync(new(Guid.NewGuid(),"cpu.usage.percent","host",now,42),default);
        await f.Store.SaveMetricSampleAsync(new(Guid.NewGuid(),"memory.used.percent","host",now,64),default);
        await Seed(f.Store,"process-services",new ProcessServiceTelemetry(
            [new(42,"example","C:\\synthetic-private-secret.exe",3,12345,now)],[],[]),now);
        await Seed(f.Store,"network",new NetworkTelemetry(
            [new("private-id","private-name","private-desc","Up",["192.0.2.10"],[],[])],[]),now);
        await Seed(f.Store,"disk",new DiskTelemetry([new("C:\\","private-label","NTFS",100,50,50,50)],null,[]),now);
        var dispatch=new DashboardDispatcher(f.Store,new(),new(),TimeProvider.System);
        var bytes=await dispatch.DispatchAsync(ViewProtocol.Request("overview",new{}),default);
        using var reply=JsonDocument.Parse(bytes);
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        var view=reply.RootElement.GetProperty("data").Deserialize<DashboardSnapshot>()!;
        Assert.Equal(42,view.Cpu!.Value);Assert.Equal(64,view.Memory!.Value);
        Assert.Equal("Shadow",view.Mode);Assert.True(view.ReadOnly);
        Assert.Equal(42,Assert.Single(view.Processes).Pid);Assert.Equal(1,view.ConnectedAdapters);
        Assert.Equal(50,Assert.Single(view.Disks).FreeBytes);
        Assert.DoesNotContain("synthetic-private",Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("192.0.2",Encoding.UTF8.GetString(bytes));
        Assert.True(bytes.Length<=16_384);
        Assert.Equal(0,f.Helper.Calls);
    }

    [Fact]
    public async Task History_uses_fixed_series_and_24hour_15minute_buckets()
    {
        await using var f=await OperatingModeTests.Fixture.Create();var now=DateTimeOffset.UtcNow;
        for(var i=0;i<200;i++)await f.Store.SaveMetricSampleAsync(new(Guid.NewGuid(),"cpu.usage.percent","host",now.AddMinutes(-i),i%100),default);
        await f.Store.SaveMetricSampleAsync(new(Guid.NewGuid(),"cpu.usage.percent","host",now.AddDays(-2),99),default);
        using var reply=JsonDocument.Parse(await new DashboardDispatcher(f.Store,new(),new(),TimeProvider.System).DispatchAsync(ViewProtocol.Request("history",new{metric="cpu"}),default));
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        var points=reply.RootElement.GetProperty("data").Deserialize<ViewMetric[]>()!;
        Assert.InRange(points.Length,10,97);Assert.All(points,p=>Assert.InRange(p.Value,0,100));
        Assert.All(points,p=>Assert.True(p.AtUtc>=now.AddHours(-24)));
    }

    [Fact]
    public async Task Incident_export_exposes_numeric_evidence_and_codes_without_arbitrary_text_or_xml()
    {
        await using var f=await OperatingModeTests.Fixture.Create();
        var saved=Assert.Single(await f.Store.ReadIncidentHeadsAsync(default));
        await f.Store.SaveIncidentBundleAsync(saved with{LatestDiagnosis=saved.LatestDiagnosis with{
            ProbableRootCause=new("service_crash","synthetic-private-token","C:\\private"),
            Evidence=[new("id","eventlog.bugcheck","<xml>synthetic-private-token</xml>",10,DateTimeOffset.UtcNow)]}},default);
        using var reply=JsonDocument.Parse(await new DashboardDispatcher(f.Store,new(),new(),TimeProvider.System).DispatchAsync(ViewProtocol.Request("incident",new{incidentId=saved.IncidentId}),default));
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.DoesNotContain("synthetic-private",reply.RootElement.GetRawText());Assert.DoesNotContain("private",reply.RootElement.GetRawText());
        Assert.Equal(90,reply.RootElement.GetProperty("data").GetProperty("Incident").GetProperty("Confidence").GetInt32());
        Assert.Equal(10,reply.RootElement.GetProperty("data").GetProperty("Evidence")[0].GetProperty("Value").GetInt32());
    }

    [Fact]
    public async Task Service_incident_exposes_only_a_validated_affected_service_identifier()
    {
        await using var f=await OperatingModeTests.Fixture.Create();
        var saved=Assert.Single(await f.Store.ReadIncidentHeadsAsync(default));
        var serviceIncident=saved with{LatestDiagnosis=saved.LatestDiagnosis with{
            ProbableRootCause=new("service_stopped","A previously running service stopped unexpectedly","Spooler")}};
        await f.Store.SaveIncidentBundleAsync(serviceIncident,default);
        var dispatcher=new DashboardDispatcher(f.Store,new(),new(),TimeProvider.System);
        using(var overview=JsonDocument.Parse(await dispatcher.DispatchAsync(ViewProtocol.Request("overview",new{}),default)))
        {
            var item=overview.RootElement.GetProperty("data").GetProperty("Incidents")[0];
            Assert.Equal("Spooler",item.GetProperty("AffectedService").GetString());
        }

        using(var detail=JsonDocument.Parse(await dispatcher.DispatchAsync(
            ViewProtocol.Request("incident",new{incidentId=saved.IncidentId}),default)))
        {
            var item=detail.RootElement.GetProperty("data").GetProperty("Incident");
            Assert.Equal("Spooler",item.GetProperty("AffectedService").GetString());
        }

        await f.Store.SaveIncidentBundleAsync(serviceIncident with{LatestDiagnosis=serviceIncident.LatestDiagnosis with{
            ProbableRootCause=new("service_stopped","A previously running service stopped unexpectedly","C:\\Users\\private\\service.exe")}},default);
        using var redacted=JsonDocument.Parse(await dispatcher.DispatchAsync(
            ViewProtocol.Request("incident",new{incidentId=saved.IncidentId}),default));
        Assert.Equal(JsonValueKind.Null,redacted.RootElement.GetProperty("data").GetProperty("Incident").GetProperty("AffectedService").ValueKind);
        Assert.DoesNotContain("C:\\Users\\private",redacted.RootElement.GetRawText());

        await f.Store.SaveIncidentBundleAsync(serviceIncident with{LatestDiagnosis=serviceIncident.LatestDiagnosis with{
            ProbableRootCause=new("memory_pressure","Memory pressure was observed.","private-token")}},default);
        using var unrelated=JsonDocument.Parse(await dispatcher.DispatchAsync(
            ViewProtocol.Request("incident",new{incidentId=saved.IncidentId}),default));
        Assert.Equal(JsonValueKind.Null,unrelated.RootElement.GetProperty("data").GetProperty("Incident").GetProperty("AffectedService").ValueKind);
        Assert.DoesNotContain("private-token",unrelated.RootElement.GetRawText());
    }

    [Fact]
    public async Task Missing_or_old_metrics_are_not_returned_as_current_and_invalid_request_is_fixed()
    {
        await using var f=await OperatingModeTests.Fixture.Create();
        await f.Store.SaveMetricSampleAsync(new(Guid.NewGuid(),"cpu.usage.percent","host",DateTimeOffset.UtcNow.AddHours(-1),99),default);
        var dispatcher=new DashboardDispatcher(f.Store,new(),new(),TimeProvider.System);
        using var reply=JsonDocument.Parse(await dispatcher.DispatchAsync(ViewProtocol.Request("overview",new{}),default));
        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null,reply.RootElement.GetProperty("data").GetProperty("Cpu").ValueKind);
        using var rejected=JsonDocument.Parse(await dispatcher.DispatchAsync("synthetic-private-invalid"u8.ToArray(),default));
        Assert.False(rejected.RootElement.GetProperty("ok").GetBoolean());Assert.Equal("view_request_rejected",rejected.RootElement.GetProperty("code").GetString());
    }
    private static async Task Seed<T>(IHostAgentStore store,string id,T value,DateTimeOffset at) where T:class
        =>_=await new TelemetryCollectionJob<T>(new SyntheticCollector<T>(id,value,at),store).CollectAndPersistAsync(default);
    private sealed class SyntheticCollector<T>(string id,T value,DateTimeOffset at):ITelemetryCollector<T> where T:class
    {
        public string CollectorId=>id;
        public ValueTask<CollectorResult<T>> CollectAsync(CancellationToken token)=>ValueTask.FromResult(CollectorResult<T>.Success(id,at,value));
    }
}
