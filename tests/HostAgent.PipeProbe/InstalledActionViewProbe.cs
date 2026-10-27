using System.Text.Json;
using System.Security.Principal;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;

internal sealed record InstalledActionViewEvidence(string Name, string State, DateTimeOffset GeneratedAtUtc);

internal static class InstalledActionViewProbe
{
    private static JsonElement Property(JsonElement element, string name) => element.EnumerateObject()
        .Single(value => value.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    internal static InstalledActionViewEvidence Read(JsonElement response, string runId, DateTimeOffset now)
    {
        try
        {
            if (!Guid.TryParseExact(runId, "N", out var id) || id == Guid.Empty || runId != id.ToString("N"))
                throw new InvalidDataException();
            if (response.GetProperty("version").GetInt32() != 1 || !response.GetProperty("ok").GetBoolean()
                || response.GetProperty("code").GetString() != "overview") throw new InvalidDataException();
            var data = response.GetProperty("data");
            var at = Property(data, "generatedAtUtc").GetDateTimeOffset();
            if (at > now || now - at > TimeSpan.FromMinutes(3)
                || Property(data, "mode").GetString() != "Confirmation" || Property(data, "readOnly").GetBoolean())
                throw new InvalidDataException();
            var name = "0HostAgentDevAcceptance_" + runId;
            var matches = Property(data, "services").EnumerateArray()
                .Where(x => Property(x, "name").GetString() == name).ToArray();
            if (matches.Length != 1) throw new InvalidDataException();
            var state = Property(matches[0], "state").GetString();
            if (state is not ("Running" or "Stopped")) throw new InvalidDataException();
            return new(name, state, at);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException
            or FormatException or ArgumentException)
        { throw new InvalidDataException("owned_view_invalid", error); }
    }

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 2) return 2;
        try
        {
            CompleteProbeGuard.ValidateIdentity();
            if (CompleteProbeGuard.FreeMiB() <= 512 || new DriveInfo("C:\\").AvailableFreeSpace < 10L * 1024 * 1024 * 1024)
                throw new InvalidDataException();
            if (!Guid.TryParseExact(args[1], "N", out var id) || id == Guid.Empty || args[1] != id.ToString("N")) return 2;
            var owner = (SecurityIdentifier)new NTAccount("NT SERVICE", "HostAgent").Translate(typeof(SecurityIdentifier));
            var bytes = await new ViewPipeClient("HostAgent.View.v1", owner, TimeSpan.FromSeconds(10))
                .SendAsync(ViewProtocol.Request("overview", new { }), CancellationToken.None);
            using var reply = JsonDocument.Parse(bytes);
            var evidence = Read(reply.RootElement, args[1], DateTimeOffset.UtcNow);
            Console.WriteLine(JsonSerializer.Serialize(new { valid = true, name = evidence.Name,
                state = evidence.State, generatedAtUtc = evidence.GeneratedAtUtc }));
            return 0;
        }
        catch { Console.WriteLine("{\"valid\":false,\"code\":\"owned_view_unavailable\"}"); return 1; }
    }
}
