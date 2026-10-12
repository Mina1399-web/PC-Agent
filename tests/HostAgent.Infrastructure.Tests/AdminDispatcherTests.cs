using System.Text.Json;
using System.Security.Principal;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Infrastructure.Security;
using HostAgent.Core.Models.Remediation;
using HostAgent.Service;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class AdminDispatcherTests
{
    [Fact]
    public async Task Incident_detail_returns_durable_explanation_evidence_recommendation_and_repair_safety()
    {
        await using var fixture=await OperatingModeTests.Fixture.Create();
        using var remediation=new RemediationCoordinator(fixture.Store,fixture.Helper,new(),TimeProvider.System);
        var dispatcher=new AdminDispatcher(fixture.Store,remediation,new(),new(),(_,_,_)=>ValueTask.CompletedTask);

        using var response=JsonDocument.Parse(await dispatcher.DispatchAsync(
            AdminProtocol.Request("incident",new{incidentId=fixture.Request.IncidentId}),default));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("incident",response.RootElement.GetProperty("code").GetString());
        var detail=response.RootElement.GetProperty("data");
        Assert.Equal(fixture.Request.IncidentId,detail.GetProperty("incidentId").GetString());
        Assert.Equal("service_crash",detail.GetProperty("rootCause").GetProperty("code").GetString());
        Assert.Equal("Synthetic",detail.GetProperty("rootCause").GetProperty("summary").GetString());
        Assert.Contains("Estimated confidence in the probable root cause",detail.GetProperty("confidenceScope").GetString());
        Assert.Equal("fixture",detail.GetProperty("evidence")[0].GetProperty("source").GetString());
        Assert.Equal("restart_service",detail.GetProperty("recommendation").GetProperty("code").GetString());
        Assert.False(detail.GetProperty("automaticRepair").GetProperty("available").GetBoolean());
        Assert.Equal(0,fixture.Helper.Calls);
    }

    [Fact]
    public async Task Bugcheck_detail_scopes_confidence_to_stop_code_and_refuses_automatic_driver_repair()
    {
        await using var fixture=await OperatingModeTests.Fixture.Create();
        using var remediation=new RemediationCoordinator(fixture.Store,fixture.Helper,new(),TimeProvider.System);
        var dispatcher=new AdminDispatcher(fixture.Store,remediation,new(),new(),(_,_,_)=>ValueTask.CompletedTask);
        var now=DateTimeOffset.UtcNow.AddMinutes(-1);
        var diagnosis=new HostAgent.Core.Models.Diagnosis.Diagnosis("bugcheck-diag","bugcheck:0xA",now,
            HostAgent.Core.Models.Diagnosis.DiagnosisSeverity.High,88,
            [new("evidence","eventlog.bugcheck","IRQL_NOT_LESS_OR_EQUAL (0xA); driver not identified",null,now)],
            new("bugcheck_irql_not_less_or_equal","Kernel memory access fault; faulty driver not identified",null),
            ["driver_attribution_pending_dump_analysis"],
            new("analyze_crash_dump","Analyze the referenced dump in WinDbg with !analyze -v."),
            HostAgent.Core.Models.Incidents.IncidentLifecycleState.Detected);
        var incident=await new HostAgent.Core.Diagnostics.IncidentEngine().ApplyAsync(diagnosis,
            new("bugcheck-snapshot","",now,null,null,null,null,null,null,[],diagnosis.Evidence));
        await fixture.Store.SaveIncidentBundleAsync(incident,default);

        using var response=JsonDocument.Parse(await dispatcher.DispatchAsync(
            AdminProtocol.Request("incident",new{incidentId=incident.IncidentId}),default));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        var detail=response.RootElement.GetProperty("data");
        Assert.Contains("does not identify the responsible driver",detail.GetProperty("confidenceScope").GetString());
        Assert.False(detail.GetProperty("automaticRepair").GetProperty("available").GetBoolean());
        Assert.Contains("faulty driver",detail.GetProperty("automaticRepair").GetProperty("reason").GetString());
        Assert.Equal(0,fixture.Helper.Calls);
    }

    [Fact]
    public async Task Incident_detail_bounds_large_evidence_and_marks_truncation_instead_of_dropping_the_response()
    {
        await using var fixture=await OperatingModeTests.Fixture.Create();
        using var remediation=new RemediationCoordinator(fixture.Store,fixture.Helper,new(),TimeProvider.System);
        var dispatcher=new AdminDispatcher(fixture.Store,remediation,new(),new(),(_,_,_)=>ValueTask.CompletedTask);
        var saved=Assert.Single(await fixture.Store.ReadIncidentHeadsAsync(default));
        var diagnosis=saved.LatestDiagnosis with
        {
            Evidence=Enumerable.Range(0,100).Select(index=>new HostAgent.Core.Models.Diagnosis.DiagnosticEvidence(
                $"evidence-{index}","test",new string('x',800),null,DateTimeOffset.UtcNow)).ToArray()
        };
        await fixture.Store.SaveIncidentBundleAsync(saved with{LatestDiagnosis=diagnosis},default);

        var bytes=await dispatcher.DispatchAsync(AdminProtocol.Request("incident",new{incidentId=saved.IncidentId}),default);
        using var response=JsonDocument.Parse(bytes);

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        var detail=response.RootElement.GetProperty("data");
        Assert.True(detail.GetProperty("evidenceTruncated").GetBoolean());
        Assert.InRange(detail.GetProperty("evidence").GetArrayLength(),1,16);
        Assert.True(bytes.Length<16_384);
    }

    // Defect: a proposal trusts caller confidence or executes in default Shadow mode.
    [Fact]
    public async Task Proposal_uses_durable_evidence_and_shadow_audits_without_execution()
    {
        await using var fixture=await OperatingModeTests.Fixture.Create();
        using var remediation=new RemediationCoordinator(fixture.Store,fixture.Helper,new(),TimeProvider.System);
        var dispatcher=new AdminDispatcher(fixture.Store,remediation,new(),new(),(_,_,_)=>ValueTask.CompletedTask);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new{version=1,command="propose",body=new{incidentId=fixture.Request.IncidentId,operation="RestartService",parameters=new{serviceId="svc"}}});
        using var response=JsonDocument.Parse(await dispatcher.DispatchAsync(bytes,default));
        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("shadow_proposal",response.RootElement.GetProperty("code").GetString());
        var request=HelperProtocol.ParseRequest(JsonSerializer.SerializeToUtf8Bytes(response.RootElement.GetProperty("data")));
        Assert.Equal(90,request.Confidence);Assert.Equal("diag",request.DiagnosisId);Assert.Equal(new[]{"evidence"},request.EvidenceReferences);
        Assert.Equal(0,fixture.Helper.Calls);
    }
    // Defect: provisioning uses the CLI identity, persists plaintext, or echoes secrets in the response.
    [Fact]
    public async Task Secret_is_written_by_serving_identity_and_never_returned_in_status()
    {
        await using var fixture=await OperatingModeTests.Fixture.Create();
        using var remediation=new RemediationCoordinator(fixture.Store,fixture.Helper,new(),TimeProvider.System);
        var parent=Path.Combine(Path.GetTempPath(),"admin-secret-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(parent);
        try
        {
            var secrets=new ProtectedSecretStore(Path.Combine(parent,"secrets"),WindowsIdentity.GetCurrent().User!);
            var dispatcher=new AdminDispatcher(fixture.Store,remediation,new(),new(),secrets.WriteAsync);
            var bytes=AdminProtocol.Request("secret-set",new{name="PushoverToken",value="synthetic-service-identity-value"});
            using var response=JsonDocument.Parse(await dispatcher.DispatchAsync(bytes,default));
            Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("synthetic-service-identity-value",await secrets.ReadAsync(HostAgent.Core.Contracts.SecretName.PushoverToken,default));
            Assert.DoesNotContain("synthetic-service-identity-value",response.RootElement.GetRawText());
            using var status=JsonDocument.Parse(await dispatcher.DispatchAsync(AdminProtocol.Request("status",new{}),default));
            Assert.Equal("Shadow",status.RootElement.GetProperty("data").GetProperty("mode").GetString());
            Assert.DoesNotContain("synthetic-service-identity-value",status.RootElement.GetRawText());
        }
        finally{Directory.Delete(parent,true);}
    }
    // Defect: admin IPC bypasses exact-approval remediation checks or drops typed parameters.
    [Fact]
    public async Task Execute_keeps_exact_binding_and_denies_changed_parameters()
    {
        await using var fixture=await OperatingModeTests.Fixture.Create();
        var config=new ServiceConfiguration{Mode=OperatingMode.Confirmation,ReadOnly=false,ConfigurationAvailable=true};
        using var remediation=new RemediationCoordinator(fixture.Store,fixture.Helper,config,TimeProvider.System);
        var dispatcher=new AdminDispatcher(fixture.Store,remediation,config,new(),(_,_,_)=>ValueTask.CompletedTask);
        var request=fixture.Request;var now=DateTimeOffset.UtcNow;
        var approval=new OperationApproval(Guid.NewGuid(),request.RequestId,request.IncidentId,request.DiagnosisId,request.Operation,HelperProtocol.Digest(request),WindowsIdentity.GetCurrent().User!.Value,now,now.AddMinutes(1)){Target=new("FixtureService",null,null)};
        byte[] Body(HelperOperationRequest value)=>JsonSerializer.SerializeToUtf8Bytes(new{version=1,command="execute",body=new{request=JsonSerializer.Deserialize<JsonElement>(HelperProtocol.SerializeRequest(value)),approval}});
        using var denied=JsonDocument.Parse(await dispatcher.DispatchAsync(Body(request with{Parameters=new ServiceParameters("other")}),default));
        Assert.False(denied.RootElement.GetProperty("ok").GetBoolean());Assert.Equal(0,fixture.Helper.Calls);
        fixture.Helper.Response=HelperResult.Succeeded;
        using var success=JsonDocument.Parse(await dispatcher.DispatchAsync(Body(request),default));
        Assert.True(success.RootElement.GetProperty("ok").GetBoolean());Assert.Equal(1,fixture.Helper.Calls);
        Assert.Equal(approval.ApprovalId,fixture.Helper.Last!.ApprovalId);
    }
    [Fact]
    public async Task Malformed_control_payload_returns_only_fixed_error_code()
    {
        await using var fixture=await OperatingModeTests.Fixture.Create();
        using var remediation=new RemediationCoordinator(fixture.Store,fixture.Helper,new(),TimeProvider.System);
        var dispatcher=new AdminDispatcher(fixture.Store,remediation,new(),new(),(_,_,_)=>ValueTask.CompletedTask);
        using var result=JsonDocument.Parse(await dispatcher.DispatchAsync("synthetic-sensitive-invalid-payload"u8.ToArray(),default));
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("control_request_rejected",result.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("synthetic-sensitive",result.RootElement.GetRawText());Assert.Equal(0,fixture.Helper.Calls);
    }
}
