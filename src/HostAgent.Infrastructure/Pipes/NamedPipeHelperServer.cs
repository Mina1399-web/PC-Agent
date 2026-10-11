using HostAgent.Infrastructure.Security;
using System.IO.Pipes;
using HostAgent.Core.Models.Remediation;
namespace HostAgent.Infrastructure.Pipes;
public sealed class NamedPipeHelperServer(string pipeName, PipeAuthorization authorization,
    HelperDispatcher dispatcher, TimeSpan timeout)
{
    public async Task ServeOnceAsync(CancellationToken cancellationToken)
    {
        HelperProtocol.ValidateEndpoint(pipeName, timeout);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        await using var stream = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            HelperProtocol.MaximumFrameBytes, HelperProtocol.MaximumFrameBytes, authorization.CreateSecurity());
        try
        {
            await stream.WaitForConnectionAsync(deadline.Token);
            var payload = await HelperProtocol.ReadFrameAsync(stream, deadline.Token);
            var request = HelperProtocol.ParseRequest(payload);
            string sid;
            try { sid = authorization.ReadAuthenticatedSid(stream); }
            catch (UnauthorizedAccessException)
            {
                await HelperProtocol.WriteFrameAsync(stream, HelperProtocol.SerializeResponse(new(1, request.RequestId,
                    HelperResult.Denied, "unauthorized_client")), deadline.Token);
                return;
            }
            var response = await dispatcher.ExecuteAsync(request, sid, deadline.Token);
            await HelperProtocol.WriteFrameAsync(stream, HelperProtocol.SerializeResponse(response), deadline.Token);
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidDataException or
            OperationCanceledException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or FormatException)
        {
            // The connection is rejected/closed. No raw payload or exception text is logged.
            LastRejectionCode = error is OperationCanceledException ? "connection_timeout_or_cancelled" : "connection_rejected";
        }
    }
    public string? LastRejectionCode { get; private set; }
}
