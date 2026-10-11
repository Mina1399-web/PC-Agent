using System.Text.Json;
using HostAgent.Infrastructure.Pipes;
using System.Security.Principal;
namespace HostAgent.Infrastructure.Security;
public enum OperatingMode { Shadow, Confirmation }
public sealed record ServiceConfiguration
{
    public OperatingMode Mode { get; init; } = OperatingMode.Shadow;
    public bool ReadOnly { get; init; } = true;
    public bool KillSwitch { get; init; }
    public bool ConfigurationAvailable { get; init; }
    public int CollectionIntervalSeconds { get; init; } = 60;
    public int HeartbeatIntervalSeconds { get; init; } = 60;
    public int RequestTimeoutSeconds { get; init; } = 10;
    public bool EnablePushover { get; init; }
    public bool EnableHeartbeat { get; init; }
    public bool EnableNetworkProbes { get; init; }
    public static ServiceConfiguration Parse(byte[] bytes)
    {
        if (bytes.Length is < 1 or > 16_384) throw new InvalidDataException("configuration_invalid");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var root = document.RootElement;
        string[] fields=["mode","readOnly","killSwitch","collectionIntervalSeconds","heartbeatIntervalSeconds",
            "requestTimeoutSeconds","enablePushover","enableHeartbeat"];
        var hasProbes=root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("enableNetworkProbes",out _);
        HelperProtocol.Exact(root,hasProbes?[..fields,"enableNetworkProbes"]:fields);
        var mode = root.GetProperty("mode").GetString() switch
        {
            "Shadow" => OperatingMode.Shadow,
            "Confirmation" => OperatingMode.Confirmation,
            _ => throw new InvalidDataException("configuration_invalid")
        };
        var config = new ServiceConfiguration
        {
            Mode=mode,ReadOnly=root.GetProperty("readOnly").GetBoolean(),KillSwitch=root.GetProperty("killSwitch").GetBoolean(),
            CollectionIntervalSeconds=root.GetProperty("collectionIntervalSeconds").GetInt32(),
            HeartbeatIntervalSeconds=root.GetProperty("heartbeatIntervalSeconds").GetInt32(),
            RequestTimeoutSeconds=root.GetProperty("requestTimeoutSeconds").GetInt32(),
            EnablePushover=root.GetProperty("enablePushover").GetBoolean(),EnableHeartbeat=root.GetProperty("enableHeartbeat").GetBoolean(),
            EnableNetworkProbes=hasProbes&&root.GetProperty("enableNetworkProbes").GetBoolean(),
            ConfigurationAvailable=true
        };
        if (config.CollectionIntervalSeconds is < 5 or > 3600 || config.HeartbeatIntervalSeconds is < 30 or > 3600 ||
            config.RequestTimeoutSeconds is < 1 or > 30) throw new InvalidDataException("configuration_invalid");
        return config;
    }
    public static ServiceConfiguration LoadSafe(string path, SecurityIdentifier identity)
    {
        try
        {
            HelperProtectedFiles.ValidateExisting(path,identity);
            using var stream=File.OpenRead(path);
            if (stream.Length > 16_384) return new();
            var bytes=new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            return Parse(bytes);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
            ArgumentException or InvalidOperationException or FormatException)
        { return new(); }
    }
}
