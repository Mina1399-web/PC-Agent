using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Diagnosis;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace HostAgent.Infrastructure.Notifications;
public sealed class PushoverNotifier(HttpClient http, ISecretStore secrets, TimeSpan timeout) : INotificationSender
{
    private static bool SafeReceipt(string? receipt) => receipt is not null &&
        Regex.IsMatch(receipt, "\\A[A-Za-z0-9]{1,128}\\z", RegexOptions.CultureInvariant);
    private static void ValidateResponse(JsonElement root)
    {
        if(root.ValueKind!=JsonValueKind.Object) throw new InvalidDataException("push_response_invalid");
        var names=root.EnumerateObject().Select(value=>value.Name).ToArray();
        if(names.Length!=names.Distinct(StringComparer.Ordinal).Count()) throw new InvalidDataException("push_response_invalid");
    }

    public async ValueTask<DeliveryResult> SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (notification.Severity == DiagnosisSeverity.Information) return new(DeliveryState.Local, "local_only");
        if (!Enum.IsDefined(notification.Severity) || string.IsNullOrWhiteSpace(notification.Message) || notification.Message.Length > 1024)
            return new(DeliveryState.Failed, "push_invalid");
        using var deadline = OutboundHttp.Deadline(timeout, cancellationToken);
        try
        {
            var token = await secrets.ReadAsync(SecretName.PushoverToken, deadline.Token);
            var user = await secrets.ReadAsync(SecretName.PushoverUser, deadline.Token);
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(user)) return new(DeliveryState.Disabled, "push_not_configured");
            var values = new Dictionary<string, string>
            {
                ["token"] = token, ["user"] = user, ["message"] = notification.Message,
                ["priority"] = notification.Severity switch { DiagnosisSeverity.Warning => "0", DiagnosisSeverity.High => "1", _ => "2" }
            };
            if (notification.Severity == DiagnosisSeverity.Critical) { values["retry"] = "60"; values["expire"] = "3600"; }
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.pushover.net/1/messages.json")
                { Content = new FormUrlEncodedContent(values) };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) return new(DeliveryState.Failed, "push_rejected");
            using var document = JsonDocument.Parse(await OutboundHttp.ReadBoundedAsync(response.Content, deadline.Token), new() { MaxDepth = 8 });
            ValidateResponse(document.RootElement);
            if (document.RootElement.GetProperty("status").GetInt32() != 1) return new(DeliveryState.Failed, "push_rejected");
            if (notification.Severity != DiagnosisSeverity.Critical) return new(DeliveryState.Sent, "push_sent");
            var receipt = document.RootElement.GetProperty("receipt").GetString();
            return SafeReceipt(receipt) ? new(DeliveryState.PendingAcknowledgement, "push_pending_ack", receipt) :
                new(DeliveryState.Uncertain, "push_response_invalid");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or
            InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException or IOException)
        { return new(DeliveryState.Uncertain, "push_unavailable_or_uncertain"); }
    }

    public async ValueTask<DeliveryResult> PollAsync(string receipt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SafeReceipt(receipt)) return new(DeliveryState.Failed, "receipt_invalid");
        using var deadline = OutboundHttp.Deadline(timeout, cancellationToken);
        try
        {
            var token = await secrets.ReadAsync(SecretName.PushoverToken, deadline.Token);
            if (string.IsNullOrWhiteSpace(token)) return new(DeliveryState.Disabled, "push_not_configured");
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.pushover.net/1/receipts/" + receipt + ".json?token=" + Uri.EscapeDataString(token));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) return new(DeliveryState.Failed, "receipt_unavailable");
            using var document = JsonDocument.Parse(await OutboundHttp.ReadBoundedAsync(response.Content, deadline.Token), new() { MaxDepth = 8 });
            var root = document.RootElement;
            ValidateResponse(root);
            if (root.GetProperty("status").GetInt32() != 1) return new(DeliveryState.Failed, "receipt_unavailable");
            return root.GetProperty("acknowledged").GetInt32() == 1 ? new(DeliveryState.Acknowledged, "push_acknowledged", receipt) :
                root.GetProperty("expired").GetInt32() == 1 ? new(DeliveryState.Expired, "push_expired", receipt) :
                new(DeliveryState.PendingAcknowledgement, "push_pending_ack", receipt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or
            InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException or IOException)
        { return new(DeliveryState.Failed, "receipt_unavailable"); }
    }
}
