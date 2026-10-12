using System.Security.Principal;
using HostAgent.Infrastructure.Administration;
using System.IO.Pipes;
using HostAgent.Infrastructure.Pipes;
namespace HostAgent.Infrastructure.Dashboard;

public sealed class ViewPipeClient(string name,SecurityIdentifier owner,TimeSpan timeout)
{
    public async ValueTask<byte[]> SendAsync(byte[] payload,CancellationToken token)
    {
        try
        {
            HelperProtocol.ValidateEndpoint(name,timeout);_=ViewProtocol.Parse(payload);
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(timeout);
            await using var pipe=new NamedPipeClientStream(".",name,PipeAccessRights.ReadWrite|PipeAccessRights.Synchronize,
                PipeOptions.Asynchronous,TokenImpersonationLevel.Identification,HandleInheritability.None);
            await pipe.ConnectAsync(deadline.Token);
            if(!owner.Equals(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier))))return AdminProtocol.Reply(false,"view_identity_mismatch");
            await HelperProtocol.WriteFrameAsync(pipe,payload,deadline.Token);
            return await HelperProtocol.ReadFrameAsync(pipe,deadline.Token);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidDataException)
        {return AdminProtocol.Reply(false,"view_unavailable");}
    }
}
