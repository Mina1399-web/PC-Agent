using System.Text.Json;
using System.Security.Principal;
using System.Security.Cryptography;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Infrastructure.Security;
namespace HostAgent.Cli;
public sealed class CliApplication(Func<byte[],CancellationToken,ValueTask<byte[]>> send,
    Func<CancellationToken,ValueTask<string?>> readSecret,Func<string,CancellationToken,ValueTask<string?>> confirm,
    Func<HelperOperationRequest,CancellationToken,ValueTask<OperationApproval>> approve,TextWriter output,string proposalDirectory,
    SecurityIdentifier? directoryIdentity=null,Func<HelperOperationRequest,CancellationToken,ValueTask<string>>? describe=null)
{
    public const string Usage="""
        Host Agent V1 local operator CLI (run as administrator except --help)
          --help
          status | incidents
          secret-set <PushoverToken|PushoverUser|HeartbeatUrl>  (masked prompt)
          configure <Shadow|Confirmation> <pushover:on|off> <heartbeat:on|off>
          network-probes <on|off>  (default off; gateway/1.1.1.1 ICMP and example.com DNS)
          allow-service <alias> <service-name> <comma-separated StartService,StopService,RestartService>
          allow-process <alias> <absolute-executable>
          remove-service <alias> | remove-process <alias>
          propose <incident-id> <StartService|StopService|RestartService> <alias>
          propose <incident-id> TerminateProcess <alias> <pid> <start-utc-ticks>
          approve-execute <request-guid>  (review and type exact GUID)
        """;
    public async ValueTask<int> RunAsync(string[] args,CancellationToken token)
    {
        byte[]? payload=null;
        try
        {
            if(args.Length==0||args is ["--help"]){await output.WriteLineAsync(Usage);return 0;}
            switch(args[0])
            {
                case "status" or "incidents" when args.Length==1:
                    payload=AdminProtocol.Request(args[0],new{});break;
                case "secret-set" when args.Length==2:
                    if(!Enum.TryParse<SecretName>(args[1],false,out var name)||!Enum.IsDefined(name)||name.ToString()!=args[1])return Invalid();
                    var value=await readSecret(token);
                    if(string.IsNullOrWhiteSpace(value)){await output.WriteLineAsync("secret_input_cancelled");return 1;}
                    payload=AdminProtocol.Request("secret-set",new{name=name.ToString(),value});break;
                case "propose" when args.Length is 4 or 6:
                    if(!Enum.TryParse<HelperOperation>(args[2],false,out var operation)||!Enum.IsDefined(operation)||operation.ToString()!=args[2])return Invalid();
                    object parameters;
                    if(operation==HelperOperation.TerminateProcess)
                    {
                        if(args.Length!=6||!int.TryParse(args[4],out var pid)||!long.TryParse(args[5],out var ticks))return Invalid();
                        parameters=new{processId=args[3],pid,startTimeUtcTicks=ticks};
                    }
                    else{if(args.Length!=4)return Invalid();parameters=new{serviceId=args[3]};}
                    payload=AdminProtocol.Request("propose",new{incidentId=args[1],operation=operation.ToString(),parameters});break;
                case "approve-execute" when args.Length==2:
                    if(!Guid.TryParseExact(args[1],"D",out var id)||id==Guid.Empty)return Invalid();
                    var root=SecureProposals();
                    var path=Path.Combine(root,id.ToString("D")+".json");
                    HelperProtectedFiles.ValidateExisting(path,Identity());
                    using(var stream=File.OpenRead(path))
                    {
                        if(stream.Length is <=0 or >16384)throw new InvalidDataException();
                        var bytes=new byte[(int)stream.Length];await stream.ReadExactlyAsync(bytes,token);
                        var request=HelperProtocol.ParseRequest(bytes);
                        if(request.RequestId!=id||request.ApprovalId is not null)throw new InvalidDataException();
                        await output.WriteLineAsync(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(bytes)));
                        await output.WriteLineAsync("Request SHA-256: "+HelperProtocol.Digest(request));
                        if(describe is not null)await output.WriteLineAsync(await describe(request,token));
                        var answer=await confirm(id.ToString("D"),token);
                        if(answer!=id.ToString("D")){await output.WriteLineAsync("approval_cancelled");return 1;}
                        var approval=await approve(request,token);
                        payload=AdminProtocol.Request("execute",new{request=JsonSerializer.Deserialize<JsonElement>(
                            HelperProtocol.SerializeRequest(request with{ApprovalId=approval.ApprovalId})),approval});
                    }
                    break;
                default:return Invalid();
            }
            using var result=JsonDocument.Parse(await send(payload,token),new JsonDocumentOptions{MaxDepth=12});
            var response=result.RootElement;
            var ok=response.GetProperty("ok").GetBoolean();
            if(args[0]=="propose"&&ok)
            {
                var request=HelperProtocol.ParseRequest(JsonSerializer.SerializeToUtf8Bytes(response.GetProperty("data")));
                var root=SecureProposals();
                var path=Path.Combine(root,request.RequestId.ToString("D")+".json");
                await using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                await file.WriteAsync(HelperProtocol.SerializeRequest(request),token);file.Flush(true);
                await output.WriteLineAsync("Proposal: "+request.RequestId.ToString("D"));
            }
            // Secret operations expose only fixed codes, even if a transport returns unexpected data.
            if(args[0]=="secret-set")await output.WriteLineAsync(ok?"secret_saved":"secret_save_failed");
            else await output.WriteLineAsync(response.GetRawText());
            return ok?0:1;
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){await output.WriteLineAsync("operator_cancelled");return 1;}
        catch{await output.WriteLineAsync("operator_action_failed");return 1;}
        finally{if(payload is not null)CryptographicOperations.ZeroMemory(payload);}
    }
    private SecurityIdentifier Identity()=>directoryIdentity??new(WellKnownSidType.LocalSystemSid,null);
    private string SecureProposals()=>HostAgentDataDirectory.EnsureSecure(proposalDirectory,Identity());
    private int Invalid(){output.WriteLine("invalid_usage");output.WriteLine(Usage);return 2;}
}
