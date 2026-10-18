using System.Security.Principal;
using System.Security.AccessControl;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Diagnostics;
using HostAgent.Infrastructure.Pipes;
namespace HostAgent.Infrastructure.Administration;
// Production supplies BuiltinAdministratorsSid; explicit principals also support isolated deployments.
public sealed class AdminPipeServer(string name,SecurityIdentifier owner,SecurityIdentifier allowedRole,
    Func<byte[],CancellationToken,ValueTask<byte[]>> dispatch,TimeSpan timeout,Action<string>? reportRejection=null,
    Action<PipeServerDiagnostic>? reportDiagnostic=null)
{
    public async Task ServeOnceAsync(CancellationToken token)
    {
        var started=Stopwatch.GetTimestamp();byte[]? payload=null;var phase="endpoint_validation";
        try
        {
            HelperProtocol.ValidateEndpoint(name,timeout);
            var acl=new PipeSecurity();acl.SetAccessRuleProtection(true,false);acl.SetOwner(owner);
            acl.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
            acl.AddAccessRule(new(owner,PipeAccessRights.FullControl,AccessControlType.Allow));
            if(!owner.Equals(allowedRole))acl.AddAccessRule(new(allowedRole,PipeAccessRights.ReadWrite|PipeAccessRights.Synchronize,AccessControlType.Allow));
            phase="server_create";
            await using var pipe=NamedPipeServerStreamAcl.Create(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous|PipeOptions.FirstPipeInstance,16384,16384,acl);
            phase="accept";
            // Keep the single idle listener until connection or service stop.
            // Periodic accept cancellation races a connecting client and can
            // hand it a pipe that is about to close (write/response EOF).
            await pipe.WaitForConnectionAsync(token);
            // Only a connected client consumes the bounded request budget.
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(timeout);started=Stopwatch.GetTimestamp();
            // Windows authenticates the security context of the last message
            // read. Consume only a bounded frame before checking the token;
            // parsing and dispatch remain strictly after authorization.
            phase="request_frame";payload=await HelperProtocol.ReadFrameAsync(pipe,deadline.Token);
            var authorized=false;
            PipeStreamImpersonationWorker authorize=()=>
            {
                phase="token_query";
                using var identity=WindowsIdentity.GetCurrent(true);
                phase="role_check";
                authorized=identity?.User is{} sid && (sid.Equals(allowedRole)||new WindowsPrincipal(identity).IsInRole(allowedRole));
            };
            // A cold callback must resolve/JIT its identity types under the
            // service token. Identification cannot load assemblies from disk.
            phase="impersonation_prepare";
            System.Runtime.CompilerServices.RuntimeHelpers.PrepareDelegate(authorize);
            phase="impersonation";
            pipe.RunAsClient(authorize);
            if(!authorized)
            {
                SafeReportRejection("control_unauthorized");
                SafeReportDiagnostic(new("client_authorization","unauthorized",ElapsedMilliseconds(started)));
                return;
            }
            phase="dispatch";
            var response=await dispatch(payload,deadline.Token);
            phase="response_frame";
            await HelperProtocol.WriteFrameAsync(pipe,response,deadline.Token);
            phase="completed";
        }
        catch(Exception error)
        {
            var kind=FailureKind(error,token);
            if(token.IsCancellationRequested)return;
            SafeReportRejection("control_"+phase+"_failed");
            SafeReportDiagnostic(new(phase,kind,ElapsedMilliseconds(started)));
            // Listener creation/configuration failure is a worker failure.
            // Preserve its backoff/health path instead of success polling.
            if(phase is "server_create" or "endpoint_validation")throw;
        }
        finally{if(payload is not null)CryptographicOperations.ZeroMemory(payload);}
    }
    private void SafeReportRejection(string code)
    {try{reportRejection?.Invoke(code);}catch{}}
    private void SafeReportDiagnostic(PipeServerDiagnostic value)
    {try{reportDiagnostic?.Invoke(value);}catch{}}
    private static long ElapsedMilliseconds(long started)
        =>(long)Math.Max(0,Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    private static string FailureKind(Exception error,CancellationToken ownerToken)=>error switch
    {
        OperationCanceledException when !ownerToken.IsCancellationRequested=>"server_deadline",
        OperationCanceledException=>"service_cancelled",
        IOException=>"io",
        UnauthorizedAccessException=>"unauthorized",
        InvalidDataException=>"invalid_frame",
        ArgumentException=>"invalid_configuration",
        TimeoutException=>"timeout",
        _=>"unexpected"
    };
}
public sealed record PipeServerDiagnostic(string Stage,string Kind,long ElapsedMilliseconds);
public sealed class AdminPipeClient(string name,SecurityIdentifier owner,TimeSpan timeout)
{
    public async ValueTask<byte[]> SendAsync(byte[] payload,CancellationToken token)
    {
        var sent=false;
        try
        {
            HelperProtocol.ValidateEndpoint(name,timeout);_=AdminProtocol.Parse(payload);
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(timeout);
            await using var pipe=new NamedPipeClientStream(".",name,PipeAccessRights.ReadWrite|PipeAccessRights.Synchronize,
                PipeOptions.Asynchronous,TokenImpersonationLevel.Identification,HandleInheritability.None);
            await pipe.ConnectAsync(deadline.Token);
            if(!owner.Equals(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier))))
                return AdminProtocol.Reply(false,"control_identity_mismatch");
            sent=true;await HelperProtocol.WriteFrameAsync(pipe,payload,deadline.Token);
            return await HelperProtocol.ReadFrameAsync(pipe,deadline.Token);
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidDataException)
        {return AdminProtocol.Reply(false,sent?"control_outcome_uncertain":"control_unavailable");}
    }
}
