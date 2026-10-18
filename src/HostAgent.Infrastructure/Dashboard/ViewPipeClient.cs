using System.Security.Principal;
using System.Diagnostics;
using HostAgent.Infrastructure.Administration;
using System.IO.Pipes;
using HostAgent.Infrastructure.Pipes;
namespace HostAgent.Infrastructure.Dashboard;

public sealed class ViewPipeClient(string name,SecurityIdentifier owner,TimeSpan timeout,
    Action<PipeClientDiagnostic>? reportDiagnostic=null)
{
    public async ValueTask<byte[]> SendAsync(byte[] payload,CancellationToken token)
    {
        var started=Stopwatch.GetTimestamp();var phase="endpoint_validation";
        try
        {
            HelperProtocol.ValidateEndpoint(name,timeout);phase="request_validation";_=ViewProtocol.Parse(payload);
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(timeout);
            phase="client_create";
            await using var pipe=new NamedPipeClientStream(".",name,PipeAccessRights.ReadWrite|PipeAccessRights.Synchronize,
                PipeOptions.Asynchronous,TokenImpersonationLevel.Identification,HandleInheritability.None);
            phase="connect";
            await pipe.ConnectAsync(deadline.Token);
            phase="server_identity";
            if(!owner.Equals(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier))))
            {
                SafeReport(new("server_identity","identity_mismatch",ElapsedMilliseconds(started)));
                return AdminProtocol.Reply(false,"view_identity_mismatch");
            }
            phase="request_frame";
            await HelperProtocol.WriteFrameAsync(pipe,payload,deadline.Token);
            phase="response_frame";
            return await HelperProtocol.ReadFrameAsync(pipe,deadline.Token);
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidDataException)
        {
            // Caller deadlines must remain diagnosable too. Report only fixed
            // stage/kind/timing; never the payload, endpoint or exception text.
            SafeReport(new(phase,error is OperationCanceledException && token.IsCancellationRequested
                ?"caller_cancelled":FailureKind(error),ElapsedMilliseconds(started)));
            return AdminProtocol.Reply(false,"view_unavailable");
        }
    }
    private void SafeReport(PipeClientDiagnostic value)
    {try{reportDiagnostic?.Invoke(value);}catch{}}
    private static long ElapsedMilliseconds(long started)
        =>(long)Math.Max(0,Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    private static string FailureKind(Exception error)=>error switch
    {
        OperationCanceledException=>"client_deadline",
        UnauthorizedAccessException=>"unauthorized",
        InvalidDataException=>"invalid_frame",
        IOException=>"io",
        _=>"other"
    };
}
public sealed record PipeClientDiagnostic(string Stage,string Kind,long ElapsedMilliseconds);
