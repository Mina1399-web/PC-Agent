using System.Text;
using HostAgent.Core.Contracts;
using HostAgent.Core.Diagnostics;
using HostAgent.Core.Models.Audit;
using HostAgent.Core.Models.Diagnosis;
using HostAgent.Core.Models.Incidents;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Storage;
using HostAgent.Service;
using Microsoft.Data.Sqlite;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class OperatingModeTests
{
    [Theory]
    [InlineData("LimitedAutonomous")]
    [InlineData("0")]
    public void Unsafe_or_unknown_mode_is_rejected(string mode)
    {
        var json = """{"mode":"MODE","readOnly":false,"killSwitch":false,"collectionIntervalSeconds":60,"heartbeatIntervalSeconds":60,"requestTimeoutSeconds":10,"enablePushover":false,"enableHeartbeat":false}""".Replace("MODE",mode);
        Assert.ThrowsAny<Exception>(() => ServiceConfiguration.Parse(Encoding.UTF8.GetBytes(json)));
    }
    [Fact]
    public void Configuration_rejects_secret_fields_and_unbounded_intervals()
    {
        Assert.ThrowsAny<Exception>(() => ServiceConfiguration.Parse("""{"token":"synthetic"}"""u8.ToArray()));
        var valid = """{"mode":"Confirmation","readOnly":false,"killSwitch":false,"collectionIntervalSeconds":60,"heartbeatIntervalSeconds":60,"requestTimeoutSeconds":10,"enablePushover":false,"enableHeartbeat":false}""";
        Assert.True(ServiceConfiguration.Parse(Encoding.UTF8.GetBytes(valid)).ConfigurationAvailable);
        Assert.ThrowsAny<Exception>(() => ServiceConfiguration.Parse(Encoding.UTF8.GetBytes(valid.Replace("\"heartbeatIntervalSeconds\":60", "\"heartbeatIntervalSeconds\":0"))));
        Assert.ThrowsAny<Exception>(() => ServiceConfiguration.Parse(Encoding.UTF8.GetBytes(valid.Replace("\"readOnly\":false", "\"readOnly\":false,\"readOnly\":false"))));
    }
    // Shadow must leave a durable proposal and never contact the helper; approvals cannot be reused for changed parameters.
    [Fact]
    public async Task Shadow_records_proposal_without_helper_and_confirmation_binds_exact_operation()
    {
        await using var fixture = await Fixture.Create();
        var coordinator = new RemediationCoordinator(fixture.Store, fixture.Helper, new(), TimeProvider.System);
        var result = await coordinator.ProposeAsync(fixture.Request, null, default);
        Assert.Equal("shadow_proposal", result.Code);
        Assert.Equal(0, fixture.Helper.Calls);
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=fixture.Path,Pooling=false }.ToString());
        await db.OpenAsync();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM audit_entries;";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        coordinator = new(fixture.Store, fixture.Helper, new() {Mode=OperatingMode.Confirmation,ReadOnly=false,ConfigurationAvailable=true},TimeProvider.System);
        var request = fixture.Request;
        var now = DateTimeOffset.UtcNow;
        var approval = new OperationApproval(Guid.NewGuid(),request.RequestId,request.IncidentId,request.DiagnosisId,request.Operation,
            HelperProtocol.Digest(request),"S-1-5-21-100-100-100-1001",now.AddSeconds(-1),now.AddMinutes(1));
        Assert.Equal("confirmation_required",(await coordinator.ProposeAsync(request,null,default)).Code);
        Assert.Equal(HelperResult.Denied,(await coordinator.ProposeAsync(request with { Parameters=new ServiceParameters("other") },approval,default)).Result);
        Assert.Equal(0,fixture.Helper.Calls);
        fixture.Helper.Response = HelperResult.Succeeded;
        Assert.Equal(HelperResult.Succeeded,(await coordinator.ProposeAsync(request,approval,default)).Result);
        Assert.Equal(approval.ApprovalId,fixture.Helper.Last!.ApprovalId);
        Assert.Equal(1,fixture.Helper.Calls);
        command.CommandText="SELECT requested_action FROM audit_entries WHERE result='confirmed' LIMIT 1;";
        var audit=Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.Contains(approval.ApproverSid,audit);
        Assert.Contains(approval.ApprovalId.ToString(),audit);
        Assert.Contains("serviceId",audit);
        Assert.Contains("\"svc\"",audit);
        Assert.Contains(approval.RequestDigest,audit);
    }
    [Fact]
    public async Task Cancellation_after_send_preserves_uncertainty_and_escalation_without_retry()
    {
        await using var fixture=await Fixture.Create();
        using var cancelled=new CancellationTokenSource();
        var request=fixture.Request;
        var now=DateTimeOffset.UtcNow;
        var approval=new OperationApproval(Guid.NewGuid(),request.RequestId,request.IncidentId,request.DiagnosisId,request.Operation,
            HelperProtocol.Digest(request),"S-1-5-21-100-100-100-1001",now.AddSeconds(-1),now.AddMinutes(1));
        var helper=new CancellingHelper(cancelled);
        var coordinator=new RemediationCoordinator(fixture.Store,helper,new(){Mode=OperatingMode.Confirmation,ReadOnly=false,ConfigurationAvailable=true},TimeProvider.System);
        Assert.Equal(HelperResult.Indeterminate,(await coordinator.ProposeAsync(request,approval,cancelled.Token)).Result);
        Assert.Equal(IncidentLifecycleState.Escalated,Assert.Single(await fixture.Store.ReadIncidentHeadsAsync(default)).State);
    }
    private sealed class CancellingHelper(CancellationTokenSource source):IHelperClient
    {
        public ValueTask<HelperOperationResponse> ExecuteAsync(HelperOperationRequest request,CancellationToken cancellationToken)
        {source.Cancel();throw new OperationCanceledException(cancellationToken);}
    }
    [Fact]
    public async Task Helper_guard_refusal_escalates_durable_incident_and_no_automatic_retry()
    {
        await using var fixture = await Fixture.Create();
        var request = fixture.Request;
        var now = DateTimeOffset.UtcNow;
        var approval = new OperationApproval(Guid.NewGuid(),request.RequestId,request.IncidentId,request.DiagnosisId,request.Operation,
            HelperProtocol.Digest(request),"S-1-5-21-100-100-100-1001",now.AddSeconds(-1),now.AddMinutes(1));
        fixture.Helper.Response = HelperResult.Denied;
        var coordinator = new RemediationCoordinator(fixture.Store,fixture.Helper,new() { Mode=OperatingMode.Confirmation,ReadOnly=false,ConfigurationAvailable=true },TimeProvider.System);
        Assert.Equal("target_rate_limit",(await coordinator.ProposeAsync(request,approval,default)).Code);
        var incident = Assert.Single(await fixture.Store.ReadIncidentHeadsAsync(default));
        Assert.Equal(IncidentLifecycleState.Escalated,incident.State);
        Assert.True(incident.Snapshots.Count >= 2);
        Assert.Equal(1,fixture.Helper.Calls);
        Assert.Equal(HelperResult.Denied,(await coordinator.ProposeAsync(request,approval,default)).Result);
        Assert.Equal(1,fixture.Helper.Calls);
    }
    internal sealed class Fixture : IAsyncDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mode-"+Guid.NewGuid()+".db");
        public SqliteHostAgentStore Store { get; }
        public FakeHelper Helper { get; } = new();
        public HelperOperationRequest Request { get; private set; } = null!;
        private Fixture() { Store=new(Path); }
        public static async Task<Fixture> Create()
        {
            var fixture=new Fixture();
            var now=DateTimeOffset.UtcNow.AddMinutes(-1);
            var diagnosis=new Diagnosis("diag","service-crash",now,DiagnosisSeverity.High,90,
                [new("evidence","fixture","Synthetic",null,now)],new("service_crash","Synthetic","svc"),[],new("restart_service","Synthetic"),IncidentLifecycleState.Detected);
            var incident=await new IncidentEngine().ApplyAsync(diagnosis,new("snapshot","",now,null,null,null,null,null,null,[],diagnosis.Evidence));
            await fixture.Store.SaveIncidentBundleAsync(incident,default);
            fixture.Request=new(1,Guid.NewGuid(),incident.IncidentId,"diag",90,["evidence"],HelperOperation.RestartService,new ServiceParameters("svc"),null);
            return fixture;
        }
        public async ValueTask DisposeAsync() { await Store.DisposeAsync(); File.Delete(Path); }
    }
    internal sealed class FakeHelper : IHelperClient
    {
        public int Calls {get;private set;}
        public HelperOperationRequest? Last {get;private set;}
        public HelperResult Response {get;set;}=HelperResult.Denied;
        public ValueTask<HelperOperationResponse> ExecuteAsync(HelperOperationRequest request,CancellationToken cancellationToken)
        { Calls++;Last=request;return ValueTask.FromResult(new HelperOperationResponse(1,request.RequestId,Response,Response==HelperResult.Succeeded?"succeeded":"target_rate_limit")); }
    }
}
