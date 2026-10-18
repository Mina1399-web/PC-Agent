using Microsoft.Data.Sqlite;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class IsolatedScmCrashProofTests
{
    [Fact]
    public async Task Worker_requires_arm_and_does_not_recreate_proof_after_restart()
    {
        var root=Path.Combine(Path.GetTempPath(),"HostAgent-crash-worker-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var id=Guid.NewGuid().ToString("N");
        try
        {
            foreach(var name in new[]{"host-agent.db","notifications.db"}) {
                using var db=new SqliteConnection($"Data Source={Path.Combine(root,name)};Pooling=False"); db.Open();
                using var command=db.CreateCommand();
                command.CommandText="PRAGMA journal_mode=WAL; CREATE TABLE telemetry_snapshots(snapshot_id TEXT); INSERT INTO telemetry_snapshots VALUES('confirmed-snapshot');";
                command.ExecuteNonQuery();
            }
            using(var worker=new IsolatedScmCrashWorker(root,id)) {
                await worker.StartAsync(default);
                await Task.Delay(150);
                Assert.False(File.Exists(Path.Combine(root,"crash-proof-ready.json")));
                await File.WriteAllTextAsync(Path.Combine(root,"crash-proof-arm.flag"),"arm");
                var deadline=DateTimeOffset.UtcNow.AddSeconds(10);
                while(!File.Exists(Path.Combine(root,"crash-proof-ready.json")) && DateTimeOffset.UtcNow<deadline) await Task.Delay(50);
                Assert.True(File.Exists(Path.Combine(root,"crash-proof-ready.json")));
                await worker.StopAsync(default);
            }
            var ready=await File.ReadAllTextAsync(Path.Combine(root,"crash-proof-ready.json"));
            using(var restarted=new IsolatedScmCrashWorker(root,id)) {
                await restarted.StartAsync(default); await Task.Delay(150); await restarted.StopAsync(default);
            }
            Assert.Equal(ready,await File.ReadAllTextAsync(Path.Combine(root,"crash-proof-ready.json")));
            Assert.True(File.Exists(Path.Combine(root,"crash-proof-expired.flag"))); // Graceful cancellation cannot count as injected crash.
            foreach(var name in new[]{"host-agent.db","notifications.db"}) AssertProof(Path.Combine(root,name),id);
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public void Both_databases_expose_committed_proof_but_not_pending_transaction()
    {
        var root = Path.Combine(Path.GetTempPath(), "HostAgent-crash-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databases = new[] { Path.Combine(root,"host-agent.db"), Path.Combine(root,"notifications.db") };
        var id = Guid.NewGuid().ToString("N");
        try
        {
            foreach (var file in databases)
            {
                using var db = new SqliteConnection($"Data Source={file};Pooling=False"); db.Open();
                using var command = db.CreateCommand(); command.CommandText = "PRAGMA journal_mode=WAL;"; command.ExecuteScalar();
            }
            using (var proof = new IsolatedScmCrashProof())
            {
                proof.Prepare(databases,id);
                foreach (var file in databases) AssertProof(file,id);
            }
            foreach (var file in databases) AssertProof(file,id);
        }
        finally { Directory.Delete(root,true); }
    }

    private static void AssertProof(string file,string id)
    {
        using var db = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False"); db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT kind FROM dev_scm_crash_proof WHERE run_id=$id ORDER BY kind;";
        command.Parameters.AddWithValue("$id",id);
        using var rows = command.ExecuteReader();
        Assert.True(rows.Read()); Assert.Equal("committed",rows.GetString(0)); Assert.False(rows.Read());
    }
}
