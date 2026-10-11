using System.Text.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Buffers.Binary;
using HostAgent.Core.Models.Remediation;

namespace HostAgent.Infrastructure.Pipes;

public static class HelperProtocol
{
    public const int MaximumFrameBytes = 16_384;
    public static byte[] SerializeResponse(HelperOperationResponse response) => JsonSerializer.SerializeToUtf8Bytes(
        new { version = response.Version, requestId = response.RequestId, result = response.Result.ToString(), code = response.Code });
    public static HelperOperationResponse ParseResponse(byte[] payload, Guid expectedRequestId)
    {
        if (payload.Length is <= 0 or > MaximumFrameBytes) throw Invalid();
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 2 });
        var root = document.RootElement; Exact(root, "version", "requestId", "result", "code");
        var text = root.GetProperty("result").GetString();
        if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("requestId").GetGuid() != expectedRequestId ||
            !Enum.TryParse<HelperResult>(text, false, out var result) || !Enum.IsDefined(result) || result.ToString() != text) throw Invalid();
        return new(1, expectedRequestId, result, Identifier(root.GetProperty("code")));
    }
    internal static void ValidateEndpoint(string name, TimeSpan timeout)
    {
        if (!Regex.IsMatch(name, "\\A[A-Za-z0-9][A-Za-z0-9_.-]{0,99}\\z", RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100)) || timeout < TimeSpan.FromMilliseconds(50) || timeout > TimeSpan.FromSeconds(30))
            throw new ArgumentException("invalid_pipe_configuration");
    }
    public static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaximumFrameBytes) throw new InvalidDataException("invalid_frame_length");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return payload;
    }
    public static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        if (payload.Length is <= 0 or > MaximumFrameBytes) throw new InvalidDataException("invalid_frame_length");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
    public static HelperOperationRequest ParseRequest(byte[] payload)
    {
        if (payload.Length is 0 or > MaximumFrameBytes) throw new InvalidDataException("invalid_frame");
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        Exact(root, "version", "requestId", "incidentId", "diagnosisId", "confidence", "evidenceReferences", "operation", "parameters", "approvalId");
        if (root.GetProperty("version").GetInt32() != 1) throw Invalid();
        var requestId = root.GetProperty("requestId").GetGuid();
        if (requestId == Guid.Empty) throw Invalid();
        var incident = Identifier(root.GetProperty("incidentId"));
        var diagnosis = Identifier(root.GetProperty("diagnosisId"));
        var confidence = root.GetProperty("confidence").GetInt32();
        if (confidence is < 0 or > 100) throw Invalid();
        var evidenceElement = root.GetProperty("evidenceReferences");
        if (evidenceElement.ValueKind != JsonValueKind.Array || evidenceElement.GetArrayLength() is < 1 or > 32) throw Invalid();
        var evidence = evidenceElement.EnumerateArray().Select(Identifier).ToArray();
        var operationText = root.GetProperty("operation").GetString();
        if (!Enum.TryParse<HelperOperation>(operationText, false, out var operation) ||
            !Enum.IsDefined(operation) || operation.ToString() != operationText) throw Invalid();
        var parameters = root.GetProperty("parameters");
        OperationParameters typed;
        if (operation == HelperOperation.TerminateProcess)
        {
            Exact(parameters, "processId", "pid", "startTimeUtcTicks");
            var pid = parameters.GetProperty("pid").GetInt32();
            var ticks = parameters.GetProperty("startTimeUtcTicks").GetInt64();
            if (pid <= 4 || ticks <= 0 || ticks > DateTimeOffset.MaxValue.UtcTicks) throw Invalid();
            typed = new ProcessParameters(Identifier(parameters.GetProperty("processId")), pid, ticks);
        }
        else
        {
            Exact(parameters, "serviceId");
            typed = new ServiceParameters(Identifier(parameters.GetProperty("serviceId")));
        }
        var approvalElement = root.GetProperty("approvalId");
        Guid? approvalId = approvalElement.ValueKind == JsonValueKind.Null ? null : approvalElement.GetGuid();
        if (approvalId == Guid.Empty) throw Invalid();
        return new(1, requestId, incident, diagnosis, confidence, evidence, operation, typed, approvalId);
    }

    public static byte[] SerializeRequest(HelperOperationRequest request)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { version = request.Version, requestId = request.RequestId,
            incidentId = request.IncidentId, diagnosisId = request.DiagnosisId, confidence = request.Confidence,
            evidenceReferences = request.EvidenceReferences, operation = request.Operation.ToString(),
            parameters = request.Parameters switch {
                ServiceParameters p => (object)new { serviceId = p.ServiceId },
                ProcessParameters p => new { processId = p.ProcessId, pid = p.Pid, startTimeUtcTicks = p.StartTimeUtcTicks },
                _ => throw Invalid() }, approvalId = request.ApprovalId });
        _ = ParseRequest(bytes); // The same validation applies to in-process callers.
        return bytes;
    }

    public static string Digest(HelperOperationRequest request) =>
        Convert.ToHexString(SHA256.HashData(SerializeRequest(request with { ApprovalId = null })));

    public static string TargetKey(HelperOperationRequest request) => request.Parameters switch {
        ServiceParameters p => "service:" + p.ServiceId,
        ProcessParameters p => "process:" + p.ProcessId,
        _ => throw Invalid() };

    internal static void Exact(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var properties = element.EnumerateObject().Select(p => p.Name).ToArray();
        if (properties.Length != names.Length || properties.Distinct(StringComparer.Ordinal).Count() != names.Length ||
            properties.Except(names, StringComparer.Ordinal).Any()) throw Invalid();
    }
    internal static string Identifier(JsonElement element)
    {
        var value = element.GetString();
        if (value is null || !Regex.IsMatch(value, "\\A[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}\\z", RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100)) || value.Contains("..", StringComparison.Ordinal)) throw Invalid();
        return value;
    }
    private static InvalidDataException Invalid() => new("invalid_request");
}
