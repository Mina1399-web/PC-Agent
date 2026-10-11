using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Principal;
using HostAgent.Infrastructure.Pipes;
namespace HostAgent.Infrastructure.Security;
public sealed record HelperConfiguration
{
    public bool ReadOnly { get; init; } = true;
    public bool Shadow { get; init; } = true;
    public bool KillSwitch { get; init; }
    public int ExecutionTimeoutSeconds { get; init; } = 15;
    public ServiceRegistration[] Services { get; init; } = [];
    public ProcessRegistration[] Processes { get; init; } = [];
    public string[] ApproverSids { get; init; } = [];
    public bool ConfigurationAvailable { get; init; }
    public static HelperConfiguration Parse(byte[] json)
    {
        if (json.Length is <= 0 or > HelperProtocol.MaximumFrameBytes) throw new InvalidDataException("invalid_configuration");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        HelperProtocol.Exact(root, "readOnly", "shadow", "killSwitch", "executionTimeoutSeconds", "services", "processes", "approverSids");
        foreach (var service in root.GetProperty("services").EnumerateArray())
            HelperProtocol.Exact(service, "id", "serviceName", "enabledOperations");
        foreach (var process in root.GetProperty("processes").EnumerateArray())
            HelperProtocol.Exact(process, "id", "executablePath", "sha256", "enabled");
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };
        options.Converters.Add(new JsonStringEnumConverter<HostAgent.Core.Models.Remediation.HelperOperation>(allowIntegerValues: false));
        var config = JsonSerializer.Deserialize<HelperConfiguration>(json, options) ?? throw new InvalidDataException("invalid_configuration");
        if (config.ExecutionTimeoutSeconds is < 1 or > 30 || config.Services.Length > 64 || config.Processes.Length > 64 || config.ApproverSids.Length > 16)
            throw new InvalidDataException("invalid_configuration");
        foreach (var sid in config.ApproverSids) _ = new SecurityIdentifier(sid);
        _ = new HelperAllowlist(config.Services, config.Processes);
        return config with { ConfigurationAvailable = true };
    }
    public static HelperConfiguration LoadSafe(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > HelperProtocol.MaximumFrameBytes) return new();
            var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
            return Parse(bytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or InvalidDataException or InvalidOperationException or NullReferenceException)
        { return new(); }
    }
}
