using Microsoft.Data.Sqlite;

// Test-only transaction seam; never wired into production service binaries.
internal sealed class IsolatedScmCrashProof : IDisposable
{
    private readonly List<SqliteConnection> connections = [];
    private readonly List<SqliteTransaction> transactions = [];
    internal void Prepare(string[] databases, string runId)
    {
        if (!Guid.TryParseExact(runId,"N",out var id) || id == Guid.Empty || databases.Length != 2
            || Path.GetFileName(databases[0]) != "host-agent.db" || Path.GetFileName(databases[1]) != "notifications.db"
            || Path.GetDirectoryName(databases[0]) != Path.GetDirectoryName(databases[1])
            || databases.Any(path => !File.Exists(path))) throw new ArgumentException("invalid_crash_fixture");
        foreach (var path in databases)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource=path, Mode=SqliteOpenMode.ReadWrite, Pooling=false, DefaultTimeout=2 }.ToString());
            connections.Add(connection); connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS dev_scm_crash_proof(run_id TEXT NOT NULL,kind TEXT NOT NULL,PRIMARY KEY(run_id,kind));";
            command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO dev_scm_crash_proof VALUES($id,'committed');";
            command.Parameters.AddWithValue("$id",runId); command.ExecuteNonQuery();
        }
        foreach (var connection in connections)
        {
            var transaction = connection.BeginTransaction(); transactions.Add(transaction);
            using var command = connection.CreateCommand(); command.Transaction=transaction;
            command.CommandText="INSERT INTO dev_scm_crash_proof VALUES($id,'pending');";
            command.Parameters.AddWithValue("$id",runId); command.ExecuteNonQuery();
        }
    }
    public void Dispose()
    {
        foreach (var transaction in transactions) transaction.Dispose();
        foreach (var connection in connections) connection.Dispose();
    }
}
