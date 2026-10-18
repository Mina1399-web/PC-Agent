using Microsoft.Data.Sqlite;
using HostAgent.Infrastructure.Storage;

// Disposable SQLite process-interruption fixture only. Parent must perform
// the stronger VM/resource/path preflight before invoking this test utility.
internal static class IsolatedPersistenceProbe
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 2 || Environment.MachineName != "AGENTPC"
            || !Guid.TryParseExact(args[1], "N", out var id)) return 2;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HostAgent-DEV-evidence", "persistence-candidate-" + id.ToString("N"));
        if (Directory.Exists(directory)) return 3;
        Directory.CreateDirectory(directory);
        for (DirectoryInfo? parent = new(directory); parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) return 3;
        var path = Path.Combine(directory, "fixture.db");
        await using var store = new SqliteHostAgentStore(path);
        await store.SaveMetricSampleAsync(new(Guid.NewGuid(), "cpu.usage.percent", "host", DateTimeOffset.UtcNow, 42), default);
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO telemetry_snapshots(snapshot_id,collector_id,payload_schema_version,sampled_at_utc,payload_json) VALUES($id,'uncommitted-fixture',1,$at,$payload);";
        var parameter = command.Parameters.Add("$id", SqliteType.Text);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$payload", System.Text.Json.JsonSerializer.Serialize(new string('x', 8192)));
        for (var i = 0; i < 512; i++)
        {
            parameter.Value = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await command.ExecuteNonQueryAsync();
        }
        Console.WriteLine("{\"ready\":true,\"committedMetrics\":1,\"uncommittedRows\":512,\"payloadBytesPerRow\":8192}");
        // Bounded automatic rollback if the parent fails to inject termination.
        await Task.Delay(TimeSpan.FromSeconds(60));
        return 4; // Natural exit is not a successful process-crash injection.
    }
}
