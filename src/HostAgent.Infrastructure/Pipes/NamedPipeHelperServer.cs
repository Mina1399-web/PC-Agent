using HostAgent.Infrastructure.Security;
using System.IO.Pipes;
using HostAgent.Core.Models.Remediation;
namespace HostAgent.Infrastructure.Pipes;
public sealed class NamedPipeHelperServer(string pipeName, PipeAuthorization authorization,
    HelperDispatcher dispatcher, TimeSpan timeout, Action<string>? stageObserver = null)
{
    private void ReportStage(string stage)
    {
        try { stageObserver?.Invoke(stage); }
        catch { /* Diagnostics cannot alter pipe protocol behavior. */ }
    }
    public async Task ServeOnceAsync(CancellationToken cancellationToken)
    {
        HelperProtocol.ValidateEndpoint(pipeName, timeout);
        ReportStage("server_endpoint_validated");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        await using var stream = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            HelperProtocol.MaximumFrameBytes, HelperProtocol.MaximumFrameBytes, authorization.CreateSecurity());
        try
        {
            await stream.WaitForConnectionAsync(deadline.Token);
            ReportStage("server_client_connected");
            var payload = await HelperProtocol.ReadFrameAsync(stream, deadline.Token);
            ReportStage("server_request_frame_read");
            var request = HelperProtocol.ParseRequest(payload);
            ReportStage("server_request_parsed");
            string sid;
            try { sid = authorization.ReadAuthenticatedSid(stream); }
            catch (UnauthorizedAccessException)
            {
                await HelperProtocol.WriteFrameAsync(stream, HelperProtocol.SerializeResponse(new(1, request.RequestId,
                    HelperResult.Denied, "unauthorized_client")), deadline.Token);
                ReportStage("server_unauthorized_response_written");
                return;
            }
            ReportStage("server_client_authenticated");
            var response = await dispatcher.ExecuteAsync(request, sid, deadline.Token);
            ReportStage("server_dispatch_completed");
            await HelperProtocol.WriteFrameAsync(stream, HelperProtocol.SerializeResponse(response), deadline.Token);
            ReportStage("server_response_written");
        }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidDataException or
            OperationCanceledException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or FormatException)
        {
            // The connection is rejected/closed. No raw payload or exception text is logged.
            LastRejectionCode = error is OperationCanceledException ? "connection_timeout_or_cancelled" : "connection_rejected";
            ReportStage(LastRejectionCode == "connection_timeout_or_cancelled" ? "server_connection_timeout" : "server_connection_rejected");
        }
    }
    public string? LastRejectionCode { get; private set; }
}
