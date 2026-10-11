using System.Security.Principal;
using System.Security.AccessControl;
using System.IO.Pipes;
using System.Security.Cryptography;
using HostAgent.Infrastructure.Pipes;
namespace HostAgent.Infrastructure.Administration;
// Production supplies BuiltinAdministratorsSid; explicit principals also support isolated deployments.
public sealed class AdminPipeServer(string name,SecurityIdentifier owner,SecurityIdentifier allowedRole,
    Func<byte[],CancellationToken,ValueTask<byte[]>> dispatch,TimeSpan timeout)
{
    public async Task ServeOnceAsync(CancellationToken token)
    {
        HelperProtocol.ValidateEndpoint(name,timeout);
        var acl=new PipeSecurity();acl.SetAccessRuleProtection(true,false);acl.SetOwner(owner);
        acl.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
        acl.AddAccessRule(new(owner,PipeAccessRights.FullControl,AccessControlType.Allow));
        if(!owner.Equals(allowedRole))acl.AddAccessRule(new(allowedRole,PipeAccessRights.ReadWrite|PipeAccessRights.Synchronize,AccessControlType.Allow));
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(timeout);
        await using var pipe=NamedPipeServerStreamAcl.Create(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous|PipeOptions.FirstPipeInstance,16384,16384,acl);
        byte[]? payload=null;
        try
        {
            await pipe.WaitForConnectionAsync(deadline.Token);
            var authorized=false;
            pipe.RunAsClient(()=>
            {
                using var identity=WindowsIdentity.GetCurrent(true);
                authorized=identity?.User is{} sid && (sid.Equals(allowedRole)||new WindowsPrincipal(identity).IsInRole(allowedRole));
            });
            if(!authorized)return;
            payload=await HelperProtocol.ReadFrameAsync(pipe,deadline.Token);
            var response=await dispatch(payload,deadline.Token);
            await HelperProtocol.WriteFrameAsync(pipe,response,deadline.Token);
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidDataException)
        { /* Close without raw payload/exception logging. No automatic replay. */ }
        finally{if(payload is not null)CryptographicOperations.ZeroMemory(payload);}
    }
}
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
