using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

// Explicit SCM test-host mode only. No production worker registrations are changed.
internal sealed class IsolatedScmCrashWorker(string data, string runId) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ready = Path.Combine(data,"crash-proof-ready.json");
        if (File.Exists(ready)) return; // A restart must not recreate or re-commit the proof.
        var armDeadline=DateTimeOffset.UtcNow.AddSeconds(90);
        while(!File.Exists(Path.Combine(data,"crash-proof-arm.flag"))) {
            if(DateTimeOffset.UtcNow>=armDeadline) throw new TimeoutException("crash_proof_not_armed");
            await Task.Delay(100,stoppingToken);
        }
        var paths = new[] {Path.Combine(data,"host-agent.db"),Path.Combine(data,"notifications.db")};
        var deadline=DateTimeOffset.UtcNow.AddSeconds(40);
        while (paths.Any(path=>!File.Exists(path)))
        {
            if (DateTimeOffset.UtcNow>=deadline) throw new TimeoutException("crash_proof_databases_unavailable");
            await Task.Delay(100,stoppingToken);
        }
        // Ensure the notification schema is initialized through its existing store API.
        await new HostAgent.Infrastructure.Storage.SqliteNotificationStore(paths[1]).HasFailuresAsync(stoppingToken);
        using var proof = new IsolatedScmCrashProof();
        proof.Prepare(paths,runId);
        using var connection=new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource=paths[0],Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=2 }.ToString());
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText="SELECT snapshot_id FROM telemetry_snapshots ORDER BY snapshot_id LIMIT 65;";
        var snapshots=new List<string>();
        using(var rows=command.ExecuteReader()) while(rows.Read()) snapshots.Add(rows.GetString(0));
        if(snapshots.Count is <1 or >64) throw new InvalidOperationException("crash_proof_snapshot_bounds");
        await File.WriteAllTextAsync(ready,JsonSerializer.Serialize(new {
            runId,committed=true,pendingTransactions=2,snapshotIds=snapshots,readyAtUtc=DateTimeOffset.UtcNow }),stoppingToken);
        // A lost controller cannot hold database writer locks forever. Expiry invalidates the test.
        try { await Task.Delay(TimeSpan.FromSeconds(60),stoppingToken); }
        finally { await File.WriteAllTextAsync(Path.Combine(data,"crash-proof-expired.flag"),"not_crashed",CancellationToken.None); }
    }
}
