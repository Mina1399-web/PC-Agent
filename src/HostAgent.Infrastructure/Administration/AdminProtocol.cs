using System.Text;
using System.Text.Json;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Pipes;
namespace HostAgent.Infrastructure.Administration;
public sealed record AdminMessage(string Command, JsonElement Body);
public static class AdminProtocol
{
    public static AdminMessage Parse(byte[] bytes)
    {
        try
        {
            if(bytes.Length is <=0 or >HelperProtocol.MaximumFrameBytes) throw new InvalidDataException();
            using var json=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=12});
            var root=json.RootElement;HelperProtocol.Exact(root,"version","command","body");
            if(root.GetProperty("version").GetInt32()!=1) throw new InvalidDataException();
            var command=root.GetProperty("command").GetString();var body=root.GetProperty("body");
            switch(command)
            {
                case "status":
                    if (body.TryGetProperty("verbose", out var verbose))
                    {
                        HelperProtocol.Exact(body, "verbose");
                        _ = verbose.GetBoolean();
                    }
                    else HelperProtocol.Exact(body);
                    break;
                case "incidents":HelperProtocol.Exact(body);break;
                case "incident":
                    HelperProtocol.Exact(body,"incidentId");
                    var incidentId=body.GetProperty("incidentId").GetString();
                    if(!Guid.TryParseExact(incidentId,"N",out var parsedIncident)||parsedIncident==Guid.Empty)
                        throw new InvalidDataException();
                    break;
                case "propose":
                    HelperProtocol.Exact(body,"incidentId","operation","parameters");
                    _=Proposal(body,"validation",80,["validation"]);break;
                case "execute":
                    _=Execution(body);break;
                case "secret-set":
                    HelperProtocol.Exact(body,"name","value");
                    var name=body.GetProperty("name").GetString();
                    if(!Enum.TryParse<SecretName>(name,false,out var parsed)||!Enum.IsDefined(parsed)||parsed.ToString()!=name)
                        throw new InvalidDataException();
                    var value=body.GetProperty("value").GetString();
                    if(string.IsNullOrWhiteSpace(value)||Encoding.UTF8.GetByteCount(value)>4096)throw new InvalidDataException();
                    break;
                default:throw new InvalidDataException();
            }
            return new(command!,body.Clone());
        }
        catch(Exception error) when(error is JsonException or InvalidOperationException or ArgumentException or FormatException or InvalidDataException)
        {throw new InvalidDataException("control_invalid_request");}
    }
    public static byte[] Request(string command,object body)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new{version=1,command,body});
        _=Parse(bytes);return bytes;
    }
    public static HelperOperationRequest Proposal(JsonElement body,string diagnosis,int confidence,string[] evidence)
        =>HelperProtocol.ParseRequest(JsonSerializer.SerializeToUtf8Bytes(new{version=1,requestId=Guid.NewGuid(),
            incidentId=body.GetProperty("incidentId"),diagnosisId=diagnosis,confidence,evidenceReferences=evidence,
            operation=body.GetProperty("operation"),parameters=body.GetProperty("parameters"),approvalId=(Guid?)null}));
    public static (HelperOperationRequest Request,OperationApproval Approval) Execution(JsonElement body)
    {
        HelperProtocol.Exact(body,"request","approval");
        var request=HelperProtocol.ParseRequest(JsonSerializer.SerializeToUtf8Bytes(body.GetProperty("request")));
        var value=body.GetProperty("approval");
        HelperProtocol.Exact(value,"ApprovalId","RequestId","IncidentId","DiagnosisId","Operation","RequestDigest","ApproverSid","ApprovedAtUtc","ExpiresAtUtc","Target");
        HelperProtocol.Exact(value.GetProperty("Target"),"ServiceName","ExecutablePath","Sha256");
        var approval=value.Deserialize<OperationApproval>()??throw new InvalidDataException("control_invalid_approval");
        if(!Enum.IsDefined(approval.Operation)||approval.RequestDigest is null||approval.RequestDigest.Length!=64)
            throw new InvalidDataException("control_invalid_approval");
        return(request,approval);
    }
    public static byte[] Reply(bool ok,string code,object? data=null)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new{version=1,ok,code,data});
        return bytes.Length<=HelperProtocol.MaximumFrameBytes?bytes:JsonSerializer.SerializeToUtf8Bytes(new{version=1,ok=false,code="control_response_limit",data=(object?)null});
    }
}
