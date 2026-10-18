using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Remediation;
using System.Security.Principal;
using System.IO.Pipes;
namespace HostAgent.Infrastructure.Pipes;
public sealed class NamedPipeHelperClient(string pipeName, SecurityIdentifier expectedHelperSid,
    TimeSpan timeout, Action<string>? stageObserver = null) : IHelperClient
{
    private void ReportStage(string stage)
    {
        try { stageObserver?.Invoke(stage); }
        catch { /* Diagnostics cannot alter pipe protocol behavior. */ }
    }
    public async ValueTask<HelperOperationResponse> ExecuteAsync(HelperOperationRequest request, CancellationToken cancellationToken)
    {
        var sent = false;
        HelperOperationResponse Response(HelperResult result, string code) => new(1, request.RequestId, result, code);
        try
        {
            HelperProtocol.ValidateEndpoint(pipeName, timeout);
            var payload = HelperProtocol.SerializeRequest(request);
            ReportStage("client_request_serialized");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            await using var stream = new NamedPipeClientStream(".", pipeName,
                PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification, HandleInheritability.None);
            ReportStage("client_connect_started");
            await stream.ConnectAsync(deadline.Token);
            ReportStage("client_connected");
            var security = stream.GetAccessControl();
            if (!expectedHelperSid.Equals(security.GetOwner(typeof(SecurityIdentifier))))
                return Response(HelperResult.Denied, "helper_identity_mismatch");
            ReportStage("client_owner_validated");
            // Partial writes are already uncertain; never silently retry.
            sent = true;
            await HelperProtocol.WriteFrameAsync(stream, payload, deadline.Token);
            ReportStage("client_request_frame_written");
            var response = await HelperProtocol.ReadFrameAsync(stream, deadline.Token);
            ReportStage("client_response_frame_read");
            var parsed = HelperProtocol.ParseResponse(response, request.RequestId);
            ReportStage("client_response_validated");
            return parsed;
        }
        catch (OperationCanceledException) { ReportStage(sent ? "client_timeout_after_send" : "client_timeout_before_send"); return Response(sent ? HelperResult.Indeterminate : HelperResult.TimedOut, "helper_timeout_or_cancelled"); }
        catch (UnauthorizedAccessException) { return Response(HelperResult.Denied, "helper_access_denied"); }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or
            ArgumentException or InvalidOperationException or FormatException)
        { ReportStage(sent ? "client_protocol_failure_after_send" : "client_protocol_failure_before_send"); return Response(sent ? HelperResult.Indeterminate : HelperResult.Unavailable, "helper_unavailable_or_invalid"); }
    }
}
