using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Remediation;
using System.Security.Principal;
using System.IO.Pipes;
namespace HostAgent.Infrastructure.Pipes;
public sealed class NamedPipeHelperClient(string pipeName, SecurityIdentifier expectedHelperSid,
    TimeSpan timeout) : IHelperClient
{
    public async ValueTask<HelperOperationResponse> ExecuteAsync(HelperOperationRequest request, CancellationToken cancellationToken)
    {
        var sent = false;
        HelperOperationResponse Response(HelperResult result, string code) => new(1, request.RequestId, result, code);
        try
        {
            HelperProtocol.ValidateEndpoint(pipeName, timeout);
            var payload = HelperProtocol.SerializeRequest(request);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            await using var stream = new NamedPipeClientStream(".", pipeName,
                PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification, HandleInheritability.None);
            await stream.ConnectAsync(deadline.Token);
            var security = stream.GetAccessControl();
            if (!expectedHelperSid.Equals(security.GetOwner(typeof(SecurityIdentifier))))
                return Response(HelperResult.Denied, "helper_identity_mismatch");
            // Partial writes are already uncertain; never silently retry.
            sent = true;
            await HelperProtocol.WriteFrameAsync(stream, payload, deadline.Token);
            return HelperProtocol.ParseResponse(await HelperProtocol.ReadFrameAsync(stream, deadline.Token), request.RequestId);
        }
        catch (OperationCanceledException) { return Response(sent ? HelperResult.Indeterminate : HelperResult.TimedOut, "helper_timeout_or_cancelled"); }
        catch (UnauthorizedAccessException) { return Response(HelperResult.Denied, "helper_access_denied"); }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or
            ArgumentException or InvalidOperationException or FormatException)
        { return Response(sent ? HelperResult.Indeterminate : HelperResult.Unavailable, "helper_unavailable_or_invalid"); }
    }
}
