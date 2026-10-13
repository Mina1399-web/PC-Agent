using System.Text;
using System.Text.Json;
using HostAgent.Core.Dashboard;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Service;
using HostAgent.Core.Orchestration;
using HostAgent.Core.Contracts;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class DashboardViewTests
{
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
