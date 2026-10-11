using System.Reflection;
using System.Text;
using HostAgent.Infrastructure.Storage;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Core.Models.Remediation;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class HelperProtocolTests
{
    [Fact]
    public async Task Framing_reads_exact_length_and_rejects_truncated_or_oversized_frames()
    {
        var method = typeof(HelperProtocol).GetMethod("ReadFrameAsync");
        Assert.NotNull(method);
        var read = method.CreateDelegate<Func<Stream, CancellationToken, Task<byte[]>>>();
        using var valid = new MemoryStream(new byte[] { 3, 0, 0, 0, 1, 2, 3 });
        Assert.Equal(new byte[] { 1, 2, 3 }, await read(valid, CancellationToken.None));
        foreach (var bytes in new[] { new byte[] { 1, 64, 0, 0 }, new byte[] { 0, 0, 0, 0 },
            new byte[] { 255, 255, 255, 255 }, new byte[] { 3, 0, 0, 0, 1 }, new byte[] { 1, 0 } })
        {
            using var invalid = new MemoryStream(bytes);
            var error = await Record.ExceptionAsync(() => read(invalid, CancellationToken.None));
            Assert.True(error is InvalidDataException or EndOfStreamException);
        }
    }
    public const string ValidJson = """{"version":1,"requestId":"d12f46a7-b934-4aa1-813f-40bbff445a68","incidentId":"incident-1","diagnosisId":"diagnosis-1","confidence":95,"evidenceReferences":["e1"],"operation":"RestartService","parameters":{"serviceId":"test-service"},"approvalId":null}""";

    [Fact]
    public void Valid_typed_request_parses_but_ambiguous_or_extra_fields_do_not()
    {
        var request = HelperProtocol.ParseRequest(Encoding.UTF8.GetBytes(ValidJson));
        Assert.Equal(HelperOperation.RestartService, request.Operation);
        Assert.Equal("test-service", Assert.IsType<ServiceParameters>(request.Parameters).ServiceId);
        foreach (var invalid in new[] {
            ValidJson.Replace("\"version\":1", "\"version\":2"),
            ValidJson.Replace("RestartService", "RunCommand"),
            ValidJson.Replace("\"version\":1", "\"version\":1,\"version\":1"),
            ValidJson.Replace("\"serviceId\":\"test-service\"", "\"serviceId\":\"test-service\",\"command\":\"cmd.exe\""),
            ValidJson.Replace("\"serviceId\":\"test-service\"", "\"serviceId\":\"../escape\""),
            ValidJson.Replace("\"confidence\":95", "\"confidence\":101"),
            ValidJson.Replace("\"version\":1", "\"$type\":\"System.Object\",\"version\":1"),
            ValidJson.Replace("\"e1\"", ""),
            ValidJson.Replace("\"approvalId\":null", "\"approvalId\":null,\"approved\":true") })
            Assert.ThrowsAny<Exception>(() => HelperProtocol.ParseRequest(Encoding.UTF8.GetBytes(invalid)));
        Assert.Throws<InvalidDataException>(() => HelperProtocol.ParseRequest(new byte[16385]));
    }
    // Unknown/oversized/malformed input must never become an executable request.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\":999,\"operation\":\"RunCommand\"}")]
    [InlineData("not-json")]
    public void Invalid_request_is_rejected_before_dispatch(string json)
    {
        var type = typeof(SqliteHostAgentStore).Assembly.GetType("HostAgent.Infrastructure.Pipes.HelperProtocol");
        Assert.NotNull(type);
        var exception = Assert.Throws<TargetInvocationException>(() =>
            type.GetMethod("ParseRequest")!.Invoke(null, [Encoding.UTF8.GetBytes(json)]));
        Assert.IsAssignableFrom<Exception>(exception.InnerException);
    }
}
