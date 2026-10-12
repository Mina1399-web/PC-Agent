using System.Text.Json;
using System.Security.Principal;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Storage;
using HostAgent.Cli;
using HostAgent.Service;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class CliTests
{
    [Fact]
    public async Task Desktop_session_shows_exact_target_and_requires_guid_without_auto_approval()
    {
        await using var fixture=new OperatorFixture();var request=Task3Fixture.Request();var calls=0;var approvals=0;
        var app=new CliApplication((_,_)=>{calls++;return ValueTask.FromResult(AdminProtocol.Reply(true,"confirmation_required",JsonSerializer.Deserialize<JsonElement>(HelperProtocol.SerializeRequest(request))));},
            _=>ValueTask.FromResult<string?>(null),
            (id,_)=>{Assert.Equal(request.RequestId.ToString("D"),id);Assert.Contains("Resolved service: FixtureService",fixture.Output.ToString());return ValueTask.FromResult<string?>("refused");},
            (_,_)=>{approvals++;throw new InvalidOperationException();},fixture.Output,fixture.Directory,WindowsIdentity.GetCurrent().User!,
            (_,_)=>ValueTask.FromResult("Resolved service: FixtureService"));
        Assert.Equal(1,await app.RunAsync(["propose-and-review",request.IncidentId,"RestartService","svc"],default));
        Assert.Equal(1,calls);Assert.Equal(0,approvals);Assert.Contains("approval_cancelled",fixture.Output.ToString());
    }
    [Fact]
    public async Task Incident_command_requests_one_detail_record_and_prints_explanation_and_safe_recommendation()
    {
        const string incidentId = "b8720be8f5b741939291e97ad4198143";
        var output = new StringWriter();
        var calls = 0;
        var app = new CliApplication((bytes, _) =>
        {
            calls++;
            var request = AdminProtocol.Parse(bytes);
            Assert.Equal("incident", request.Command);
            Assert.Equal(incidentId, request.Body.GetProperty("incidentId").GetString());
            return ValueTask.FromResult(AdminProtocol.Reply(true, "incident", new
            {
                rootCause = new { code = "windows_bugcheck", summary = "Windows recorded a kernel bugcheck; the responsible driver is not identified." },
                confidence = 78,
                evidence = new[] { new { source = "eventlog.bugcheck", observation = "Bug check 0xA (IRQL_NOT_LESS_OR_EQUAL)." } },
                recommendation = new { code = "analyze_crash_dump", summary = "Analyze the crash dump before changing drivers." },
                automaticRepair = new { available = false, reason = "The faulty driver has not been identified." }
            }));
        }, _ => ValueTask.FromResult<string?>(null), (_, _) => ValueTask.FromResult<string?>(null),
            (_, _) => throw new InvalidOperationException(), output, Path.GetTempPath());

        Assert.Equal(0, await app.RunAsync(["incident", incidentId.ToUpperInvariant()], default));
        Assert.Equal(1, calls);
        Assert.Contains("responsible driver is not identified", output.ToString());
        Assert.Contains("Analyze the crash dump before changing drivers", output.ToString());
        Assert.Contains("\"available\":false", output.ToString());
    }

    [Fact]
    public async Task Resolved_local_target_is_displayed_before_confirmation_and_never_executes_after_refusal()
    {
        await using var fixture=new OperatorFixture();var request=Task3Fixture.Request();var calls=0;
        await File.WriteAllBytesAsync(Path.Combine(fixture.Directory,request.RequestId.ToString("D")+".json"),HelperProtocol.SerializeRequest(request));
        var app=new CliApplication((_,_)=>{calls++;throw new InvalidOperationException();},_=>ValueTask.FromResult<string?>(null),
            (_,_)=>{Assert.Contains("Resolved service: SyntheticService",fixture.Output.ToString());return ValueTask.FromResult<string?>(null);},
            (_,_)=>throw new InvalidOperationException(),fixture.Output,fixture.Directory,WindowsIdentity.GetCurrent().User!,
            (value,_)=>{Assert.Equal(request.RequestId,value.RequestId);return ValueTask.FromResult("Resolved service: SyntheticService");});
        Assert.Equal(1,await app.RunAsync(["approve-execute",request.RequestId.ToString("D")],default));
        Assert.Contains("approval_cancelled",fixture.Output.ToString());Assert.Equal(0,calls);
    }
    // Defect: operator cannot discover commands, invalid commands reach IPC, or secrets are accepted as arguments.
    [Fact]
    public async Task Help_and_invalid_usage_never_contact_service_or_read_secret()
    {
        var calls=0;var secrets=0;var output=new StringWriter();
        var app=new CliApplication((_,_)=>{calls++;return ValueTask.FromResult(AdminProtocol.Reply(true,"ready"));},
            _=>{secrets++;return ValueTask.FromResult<string?>("synthetic");},(_,_)=>ValueTask.FromResult<string?>(null),
            (_,_)=>throw new InvalidOperationException(),output,Path.GetTempPath());
        Assert.Equal(0,await app.RunAsync(["--help"],default));Assert.Contains("approve-execute",output.ToString());
        Assert.Equal(2,await app.RunAsync(["shell"],default));
        Assert.Equal(2,await app.RunAsync(["incident","not-an-incident-id"],default));
        Assert.Equal(2,await app.RunAsync(["secret-set","PushoverToken","do-not-accept-secret-argument"],default));
        Assert.Equal(0,calls);Assert.Equal(0,secrets);Assert.DoesNotContain("do-not-accept-secret-argument",output.ToString());
    }
    [Fact]
    public async Task Masked_secret_input_is_sent_once_and_never_echoed()
    {
        var calls=0;var output=new StringWriter();
        var app=new CliApplication((bytes,_)=>{
            calls++;var request=AdminProtocol.Parse(bytes);Assert.Equal("secret-set",request.Command);
            Assert.Equal("synthetic-input-only",request.Body.GetProperty("value").GetString());
            return ValueTask.FromResult(AdminProtocol.Reply(true,"secret_saved"));
        },_=>ValueTask.FromResult<string?>("synthetic-input-only"),(_,_)=>ValueTask.FromResult<string?>(null),
            (_,_)=>throw new InvalidOperationException(),output,Path.GetTempPath());
        Assert.Equal(0,await app.RunAsync(["secret-set","PushoverUser"],default));
        Assert.Equal(1,calls);Assert.DoesNotContain("synthetic-input-only",output.ToString());
    }
    // Defect: proposal is not persisted or approval happens before explicit exact-request confirmation.
    [Fact]
    public async Task Proposal_persists_exact_request_and_refused_confirmation_never_approves()
    {
        await using var fixture=new OperatorFixture();var request=Task3Fixture.Request();var approvals=0;var calls=0;
        var app=fixture.App((bytes,_)=>{calls++;return ValueTask.FromResult(AdminProtocol.Reply(true,"shadow_proposal",JsonSerializer.Deserialize<JsonElement>(HelperProtocol.SerializeRequest(request))));},
            (_,_)=>ValueTask.FromResult<string?>("wrong"),(_,_)=>{approvals++;throw new InvalidOperationException();});
        Assert.Equal(0,await app.RunAsync(["propose",request.IncidentId,"RestartService","svc"],default));
        var path=Path.Combine(fixture.Directory,request.RequestId.ToString("D")+".json");
        Assert.Equal(HelperProtocol.Digest(request),HelperProtocol.Digest(HelperProtocol.ParseRequest(await File.ReadAllBytesAsync(path))));
        Assert.Equal(1,await app.RunAsync(["approve-execute",request.RequestId.ToString("D")],default));
        Assert.Equal(0,approvals);Assert.Equal(1,calls);Assert.Contains("approval_cancelled",fixture.Output.ToString());
    }
    [Fact]
    public async Task Exact_confirmation_persists_approval_before_single_bound_execute()
    {
        await using var fixture=new OperatorFixture();var request=Task3Fixture.Request();
        await File.WriteAllBytesAsync(Path.Combine(fixture.Directory,request.RequestId.ToString("D")+".json"),HelperProtocol.SerializeRequest(request));
        var sid=WindowsIdentity.GetCurrent().User!.Value;
        await using var journal=new SqliteHelperJournal(Path.Combine(fixture.Directory,"journal.db"),[sid]);
        var issuer=new ApprovalIssuer(journal,sid,TimeProvider.System);var calls=0;
        var app=fixture.App(async(bytes,token)=>{
            calls++;var message=AdminProtocol.Parse(bytes);Assert.Equal("execute",message.Command);
            var execution=AdminProtocol.Execution(message.Body);
            Assert.Equal(HelperProtocol.Digest(request),execution.Approval.RequestDigest);
            Assert.Equal(execution.Approval,await journal.ReadApprovalAsync(execution.Approval.ApprovalId,token));
            Assert.Equal(execution.Approval.ApprovalId,execution.Request.ApprovalId);
            return AdminProtocol.Reply(true,"succeeded");
        },(id,_)=>ValueTask.FromResult<string?>(id),(value,token)=>issuer.CreateAsync(value,new("FixtureService",null,null),new("FixtureService",null,null),token));
        Assert.Equal(0,await app.RunAsync(["approve-execute",request.RequestId.ToString("D")],default));Assert.Equal(1,calls);
        Assert.Contains(HelperProtocol.Digest(request),fixture.Output.ToString());
    }
    [Fact]
    public async Task Admin_worker_cancels_pending_request_and_backs_off_after_failure()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var calls=0;var health=new AgentRuntimeHealth();
        using var worker=new AdminWorker(async token=>{
            if(Interlocked.Increment(ref calls)==1)throw new IOException("synthetic-sensitive");
            entered.TrySetResult();await Task.Delay(Timeout.Infinite,token);
        },health);
        await worker.StartAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2,calls);Assert.False(health.Degraded);
    }
    private sealed class OperatorFixture:IAsyncDisposable
    {
        public string Directory{get;}=Path.Combine(Path.GetTempPath(),"operator-"+Guid.NewGuid().ToString("N"));
        public StringWriter Output{get;}=new();
        public OperatorFixture()=>HostAgentDataDirectory.EnsureSecure(Directory,WindowsIdentity.GetCurrent().User!);
        public CliApplication App(Func<byte[],CancellationToken,ValueTask<byte[]>> send,Func<string,CancellationToken,ValueTask<string?>> confirm,
            Func<HelperOperationRequest,CancellationToken,ValueTask<OperationApproval>> approve)=>new(send,_=>ValueTask.FromResult<string?>(null),confirm,approve,Output,Directory,WindowsIdentity.GetCurrent().User!);
        public ValueTask DisposeAsync(){System.IO.Directory.Delete(Directory,true);return ValueTask.CompletedTask;}
    }
}
