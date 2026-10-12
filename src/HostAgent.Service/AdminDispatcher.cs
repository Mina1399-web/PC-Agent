using HostAgent.Core.Contracts;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Core.Models.Remediation;
using System.Text.Json;
namespace HostAgent.Service;
public sealed class AdminDispatcher(IHostAgentStore store,RemediationCoordinator remediation,ServiceConfiguration configuration,
    AgentRuntimeHealth health,Func<SecretName,string,CancellationToken,ValueTask> writeSecret)
{
    public async ValueTask<byte[]> DispatchAsync(byte[] payload,CancellationToken token)
    {
        try
        {
            var message=AdminProtocol.Parse(payload);var body=message.Body;
            switch(message.Command)
            {
                case "status":return AdminProtocol.Reply(true,"ready",new{mode=configuration.Mode.ToString(),configuration.ReadOnly,
                    configuration.ConfigurationAvailable,degraded=health.Degraded,configuration.EnablePushover,configuration.EnableHeartbeat});
                case "incidents":
                    var heads=await store.ReadIncidentHeadsAsync(token);
                    return AdminProtocol.Reply(true,"incidents",heads.OrderByDescending(x=>x.UpdatedAtUtc).Take(20).Select(x=>new
                        {x.IncidentId,state=x.State.ToString(),x.LatestDiagnosis.DiagnosisId,x.LatestDiagnosis.Confidence,severity=x.LatestDiagnosis.Severity.ToString()}));
                case "incident":
                    var incidentId=body.GetProperty("incidentId").GetString();
                    var selected=(await store.ReadIncidentHeadsAsync(token)).SingleOrDefault(x=>x.IncidentId==incidentId);
                    if(selected is null)return AdminProtocol.Reply(false,"incident_not_found");
                    var diagnosis=selected.LatestDiagnosis;
                    var isBugcheck=diagnosis.ProbableRootCause.Code=="windows_bugcheck"
                        ||diagnosis.ProbableRootCause.Code.StartsWith("bugcheck_",StringComparison.Ordinal);
                    var confidenceScope=isBugcheck
                        ? "Confidence describes stop-code classification only; it does not identify the responsible driver."
                        : "Estimated confidence in the probable root cause.";
                    var automaticRepairReason=isBugcheck
                        ? "The crash event does not identify a faulty driver; automatic driver changes are disabled."
                        : configuration.Mode==OperatingMode.Shadow||configuration.ReadOnly
                            ? "Shadow or read-only mode records recommendations but never executes repairs."
                            : "No unattended repair is mapped to this diagnosis; any action needs an exact approved operation.";
                    return AdminProtocol.Reply(true,"incident",new
                    {
                        incidentId=selected.IncidentId,state=selected.State.ToString(),recurrenceCount=selected.RecurrenceCount,
                        diagnosisId=diagnosis.DiagnosisId,observedAtUtc=diagnosis.ObservedAtUtc,severity=diagnosis.Severity.ToString(),
                        confidence=diagnosis.Confidence,confidenceScope,
                        rootCause=new{code=diagnosis.ProbableRootCause.Code,summary=Limit(diagnosis.ProbableRootCause.Summary,512),
                            subject=diagnosis.ProbableRootCause.Subject is null?null:Limit(diagnosis.ProbableRootCause.Subject,256)},
                        evidenceTruncated=diagnosis.Evidence.Count>16,
                        evidence=diagnosis.Evidence.Take(16).Select(item=>new{evidenceId=item.EvidenceId,source=Limit(item.Source,128),
                            observation=Limit(item.Observation,512),numericValue=item.NumericValue,observedAtUtc=item.ObservedAtUtc}),
                        secondarySymptoms=diagnosis.SecondarySymptoms.Take(32),
                        recommendation=new{code=diagnosis.RecommendedRemediation.RecommendationCode,
                            summary=Limit(diagnosis.RecommendedRemediation.Summary,512)},
                        automaticRepair=new{available=false,reason=automaticRepairReason}
                    });
                case "secret-set":
                    await writeSecret(Enum.Parse<SecretName>(body.GetProperty("name").GetString()!),body.GetProperty("value").GetString()!,token);
                    return AdminProtocol.Reply(true,"secret_saved");
                case "propose":
                    var incident=(await store.ReadIncidentHeadsAsync(token)).SingleOrDefault(x=>x.IncidentId==body.GetProperty("incidentId").GetString());
                    if(incident is null)return AdminProtocol.Reply(false,"incident_not_found");
                    var request=AdminProtocol.Proposal(body,incident.LatestDiagnosis.DiagnosisId,incident.LatestDiagnosis.Confidence,
                        incident.LatestDiagnosis.Evidence.Select(x=>x.EvidenceId).ToArray());
                    var proposal=await remediation.ProposeAsync(request,null,token);
                    return AdminProtocol.Reply(proposal.Code is "shadow_proposal" or "confirmation_required" or "read_only",proposal.Code,
                        JsonSerializer.Deserialize<JsonElement>(HelperProtocol.SerializeRequest(request)));
                case "execute":
                    var execution=AdminProtocol.Execution(body);
                    var result=await remediation.ProposeAsync(execution.Request,execution.Approval,token);
                    return AdminProtocol.Reply(result.Result==HelperResult.Succeeded,result.Code,new{result=result.Result.ToString(),result.RequestId});
                default:return AdminProtocol.Reply(false,"control_request_rejected");
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch{return AdminProtocol.Reply(false,"control_request_rejected");}
    }
    private static string Limit(string value,int maximum)=>value.Length<=maximum?value:value[..maximum];
}
