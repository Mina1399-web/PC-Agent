using HostAgent.Core.Models.Remediation;
using HostAgent.Core.Policy;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Infrastructure.Security;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class ApprovalTargetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Issuer_refuses_target_rebound_while_operator_is_confirming(bool process)
    {
        await using var fixture=new Task3Fixture();var request=Task3Fixture.Request();
        var displayed=process?new ApprovedTarget(null,@"C:\Synthetic\fixture.exe",new string('A',64)):new ApprovedTarget("OriginalService",null,null);
        var current=process?displayed with{Sha256=new string('B',64)}:displayed with{ServiceName="DifferentService"};
        var issuer=new HostAgent.Infrastructure.Administration.ApprovalIssuer(fixture.Journal,Task3Fixture.ApproverSid,fixture.Clock);
        var error=await Assert.ThrowsAsync<InvalidDataException>(()=>issuer.CreateAsync(request,displayed,current,default).AsTask());
        Assert.Equal("approval_target_changed",error.Message);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rebound_alias_or_legacy_unbound_approval_is_denied_before_executor(bool legacy)
    {
        await using var fixture=new Task3Fixture();var request=Task3Fixture.Request();
        var approval=new OperationApproval(Guid.NewGuid(),request.RequestId,request.IncidentId,request.DiagnosisId,request.Operation,
            HelperProtocol.Digest(request),Task3Fixture.ApproverSid,fixture.Clock.Now,fixture.Clock.Now.AddMinutes(5))
            {Target=legacy?null:new ApprovedTarget("OriginalService",null,null)};
        await fixture.Journal.RecordApprovalAsync(approval,default);
        var result=await fixture.Dispatcher().ExecuteAsync(request with{ApprovalId=approval.ApprovalId},Task3Fixture.CallerSid,default);
        Assert.Equal(HelperResult.Denied,result.Result);Assert.Equal("approval_target_mismatch",result.Code);Assert.Equal(0,fixture.Executor.Calls);
    }
    [Fact]
    public async Task Process_hash_change_after_approval_is_denied_even_with_same_alias_path_pid_and_request_digest()
    {
        await using var fixture=new Task3Fixture();var request=Task3Fixture.Request() with{Operation=HelperOperation.TerminateProcess,Parameters=new ProcessParameters("demo",100,123456)};
        var path=Path.Combine(Path.GetTempPath(),"synthetic-never-launched.exe");
        var approval=new OperationApproval(Guid.NewGuid(),request.RequestId,request.IncidentId,request.DiagnosisId,request.Operation,
            HelperProtocol.Digest(request),Task3Fixture.ApproverSid,fixture.Clock.Now,fixture.Clock.Now.AddMinutes(5))
            {Target=new ApprovedTarget(null,path,new string('A',64))};
        await fixture.Journal.RecordApprovalAsync(approval,default);
        var helper=new HelperDispatcher(fixture.Journal,new HelperAllowlist([],[new("demo",path,new string('B',64),true)]),fixture.Executor,new PolicyEngine(),()=>new(false,false),timeProvider:fixture.Clock);
        var result=await helper.ExecuteAsync(request with{ApprovalId=approval.ApprovalId},Task3Fixture.CallerSid,default);
        Assert.Equal(HelperResult.Denied,result.Result);Assert.Equal("approval_target_mismatch",result.Code);Assert.Equal(0,fixture.Executor.Calls);
    }
}
