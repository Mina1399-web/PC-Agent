using System.Net;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Diagnosis;
using HostAgent.Infrastructure.Notifications;
using HostAgent.Infrastructure.Heartbeat;
using Xunit;
namespace HostAgent.Infrastructure.Tests;

public sealed class OutboundTransportTests
{
    // Wrong priority, transmitting INFO, missing emergency receipt or losing cancellation are observable transport defects.
    [Theory]
    [InlineData(DiagnosisSeverity.Warning, "0")]
    [InlineData(DiagnosisSeverity.High, "1")]
    [InlineData(DiagnosisSeverity.Critical, "2")]
    public async Task Pushover_maps_priorities_and_emergency_retry_receipt(DiagnosisSeverity severity, string priority)
    {
        var handler = new FakeHttp();
        using var http = new HttpClient(handler);
        var sender = new PushoverNotifier(http, new TestSecrets(), TimeSpan.FromSeconds(1));
        var result = await sender.SendAsync(new("notice", "incident", severity, "Synthetic test incident"), default);
        Assert.Equal(severity == DiagnosisSeverity.Critical ? DeliveryState.PendingAcknowledgement : DeliveryState.Sent, result.State);
        Assert.Equal("https://api.pushover.net/1/messages.json", handler.Url);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Contains("priority=" + priority, handler.Body);
        Assert.Contains("token=synthetic-token", handler.Body);
        Assert.Contains("user=synthetic-user", handler.Body);
        if (severity == DiagnosisSeverity.Critical)
        {
            Assert.Contains("retry=60", handler.Body);
            Assert.Contains("expire=3600", handler.Body);
            Assert.Equal("syntheticreceipt", result.Receipt);
        }
    }
    [Fact]
    public async Task Info_and_missing_credentials_never_use_network()
    {
        var handler = new FakeHttp();
        using var http = new HttpClient(handler);
        var sender = new PushoverNotifier(http, new TestSecrets(), TimeSpan.FromSeconds(1));
        Assert.Equal(DeliveryState.Local, (await sender.SendAsync(new("n", "i", DiagnosisSeverity.Information, "test"), default)).State);
        var missing = new PushoverNotifier(http, new TestSecrets(false), TimeSpan.FromSeconds(1));
        Assert.Equal(DeliveryState.Disabled, (await missing.SendAsync(new("n", "i", DiagnosisSeverity.High, "test"), default)).State);
        Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public async Task Critical_poll_observes_acknowledged_and_expired_without_resending()
    {
        var handler = new FakeHttp { Json = """{"status":1,"acknowledged":1,"expired":0}""" };
        using var http = new HttpClient(handler);
        var sender = new PushoverNotifier(http, new TestSecrets(), TimeSpan.FromSeconds(1));
        Assert.Equal(DeliveryState.Acknowledged, (await sender.PollAsync("syntheticreceipt", default)).State);
        Assert.Contains("/receipts/syntheticreceipt.json", handler.Url);
        handler.Json = """{"status":1,"acknowledged":0,"expired":1}""";
        Assert.Equal(DeliveryState.Expired, (await sender.PollAsync("syntheticreceipt", default)).State);
        Assert.DoesNotContain("/messages", handler.Url);
    }
    [Theory]
    [InlineData("http://hc-ping.com/synthetic", true, false)]
    [InlineData("https://user:password@hc-ping.com/synthetic", true, false)]
    [InlineData("https://hc-ping.com/synthetic", true, true)]
    [InlineData("https://hc-ping.com/synthetic", false, true)]
    public async Task Heartbeat_requires_safe_https_and_reports_health(string endpoint, bool degraded, bool allowed)
    {
        var handler = new FakeHttp { Json = "OK" };
        using var http = new HttpClient(handler);
        var sender = new HealthchecksHeartbeat(http, new TestSecrets(endpoint: endpoint), TimeSpan.FromSeconds(1));
        var result = await sender.SendAsync(new(degraded), default);
        Assert.Equal(allowed ? DeliveryState.Sent : DeliveryState.Disabled, result.State);
        Assert.Equal(allowed ? 1 : 0, handler.Calls);
        if (allowed)
        {
            Assert.Equal(endpoint + (degraded ? "/fail" : ""), handler.Url);
            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal("", handler.Body);
        }
    }
    [Fact]
    public async Task Failure_timeout_and_cancellation_return_only_fixed_redacted_codes()
    {
        var handler = new FakeHttp { Status = HttpStatusCode.InternalServerError, Json = "sensitive synthetic payload" };
        using var http = new HttpClient(handler);
        var sender = new PushoverNotifier(http, new TestSecrets(), TimeSpan.FromMilliseconds(80));
        Assert.Equal(new DeliveryResult(DeliveryState.Failed, "push_rejected"), await sender.SendAsync(new("n","i",DiagnosisSeverity.High,"test"), default));
        handler.Hang = true;
        Assert.Equal(DeliveryState.Uncertain, (await sender.SendAsync(new("n","i",DiagnosisSeverity.High,"test"), default)).State);
        var heartbeat = new HealthchecksHeartbeat(http, new TestSecrets(), TimeSpan.FromMilliseconds(80));
        Assert.Equal(new DeliveryResult(DeliveryState.Failed, "heartbeat_unavailable"), await heartbeat.SendAsync(new(false), default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await heartbeat.SendAsync(new(false), cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sender.SendAsync(new("n","i",DiagnosisSeverity.High,"test"), cancelled.Token));
    }
    [Fact]
    public async Task Malformed_or_oversized_push_response_and_invalid_receipt_fail_closed()
    {
        var handler = new FakeHttp { Json = new string('x', 20_000) };
        using var http = new HttpClient(handler);
        var sender = new PushoverNotifier(http, new TestSecrets(), TimeSpan.FromSeconds(1));
        Assert.Equal(DeliveryState.Uncertain, (await sender.SendAsync(new("n","i",DiagnosisSeverity.Critical,"test"), default)).State);
        var before = handler.Calls;
        Assert.Equal(DeliveryState.Failed, (await sender.PollAsync("../injection?token=bad", default)).State);
        Assert.Equal(before, handler.Calls);
    }
    [Theory]
    [InlineData("{\"status\":1,\"status\":0,\"receipt\":\"syntheticreceipt\"}")]
    [InlineData("{\"status\":1,\"receipt\":\"bad\\n\"}")]
    public async Task Ambiguous_or_non_alphanumeric_receipt_response_cannot_become_a_trusted_delivery(string json)
    {
        using var http=new HttpClient(new FakeHttp{Json=json});
        var sender=new PushoverNotifier(http,new TestSecrets(),TimeSpan.FromSeconds(1));
        Assert.Equal(DeliveryState.Uncertain,(await sender.SendAsync(new("n","i",DiagnosisSeverity.Critical,"Synthetic"),default)).State);
    }
    internal sealed class TestSecrets(bool available = true, string endpoint = "https://hc-ping.com/synthetic") : ISecretStore
    {
        public ValueTask<string?> ReadAsync(SecretName name, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(!available ? null : name switch
            { SecretName.PushoverToken => "synthetic-token", SecretName.PushoverUser => "synthetic-user", _ => endpoint });
    }
    internal sealed class FakeHttp : HttpMessageHandler
    {
        public string Json { get; set; } = """{"status":1,"receipt":"syntheticreceipt"}""";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool Hang { get; set; }
        public int Calls { get; private set; }
        public string? Url { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Url = request.RequestUri!.AbsoluteUri;
            Method = request.Method;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (Hang) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new(Status) { Content = new StringContent(Json) };
        }
    }
}
