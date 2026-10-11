using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Diagnosis;
using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace HostAgent.Infrastructure.Storage;
public sealed class SqliteNotificationStore(string path) : INotificationStore
{
    public async ValueTask<bool> HasFailuresAsync(CancellationToken cancellationToken)
    {
        await using var connection=await OpenAsync(cancellationToken);
        using var command=connection.CreateCommand();
        command.CommandText="SELECT EXISTS(SELECT 1 FROM notifications WHERE state IN ($failed,$uncertain,$expired,$disabled) OR code='receipt_unavailable');";
        command.Parameters.AddWithValue("$failed",(int)DeliveryState.Failed);
        command.Parameters.AddWithValue("$uncertain",(int)DeliveryState.Uncertain);
        command.Parameters.AddWithValue("$expired",(int)DeliveryState.Expired);
        command.Parameters.AddWithValue("$disabled",(int)DeliveryState.Disabled);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))! != 0;
    }
    private async ValueTask<SqliteConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Pooling = false, DefaultTimeout = 2, ForeignKeys = true }.ToString());
        try
        {
            await connection.OpenAsync(token);
            using var command = connection.CreateCommand();
            command.CommandText="CREATE TABLE IF NOT EXISTS notification_schema(version INTEGER PRIMARY KEY CHECK(version=1));";
            await command.ExecuteNonQueryAsync(token);
            command.CommandText="SELECT COALESCE(MAX(version),0) FROM notification_schema;";
            var version=(long)(await command.ExecuteScalarAsync(token))!;
            if(version is not (0 or 1))throw new NotSupportedException("notification_schema_unsupported");
            command.CommandText = """
                PRAGMA synchronous=FULL;
                CREATE TABLE IF NOT EXISTS notification_schema(version INTEGER PRIMARY KEY CHECK(version=1));
                INSERT OR IGNORE INTO notification_schema VALUES(1);
                CREATE TABLE IF NOT EXISTS notifications(
                    id TEXT PRIMARY KEY,payload TEXT NOT NULL,state INTEGER NOT NULL,code TEXT NOT NULL,
                    receipt TEXT,attempts INTEGER NOT NULL,created INTEGER NOT NULL,next INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS notification_due ON notifications(state,next);
                CREATE TABLE IF NOT EXISTS notification_events(
                    sequence INTEGER PRIMARY KEY,id TEXT NOT NULL,state INTEGER NOT NULL,code TEXT NOT NULL,at INTEGER NOT NULL);
                CREATE TRIGGER IF NOT EXISTS notification_events_no_update BEFORE UPDATE ON notification_events BEGIN SELECT RAISE(ABORT,'immutable'); END;
                CREATE TRIGGER IF NOT EXISTS notification_events_no_delete BEFORE DELETE ON notification_events BEGIN SELECT RAISE(ABORT,'immutable'); END;
                """;
            await command.ExecuteNonQueryAsync(token);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    public async ValueTask EnqueueAsync(Notification notification, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (notification.Id.Length is < 1 or > 200 || notification.IncidentId.Length is < 1 or > 128 ||
            notification.Message.Length is < 1 or > 1024 || !Enum.IsDefined(notification.Severity)) throw new ArgumentException("notification_invalid");
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO notifications VALUES($id,$payload,$state,$code,NULL,0,$now,$now);
            INSERT INTO notification_events(id,state,code,at)
                SELECT $id,$state,$code,$now WHERE changes()=1;
            """;
        command.Parameters.AddWithValue("$id", notification.Id);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(notification));
        command.Parameters.AddWithValue("$state", (int)(notification.Severity == DiagnosisSeverity.Information ? DeliveryState.Local : DeliveryState.Queued));
        command.Parameters.AddWithValue("$code", notification.Severity == DiagnosisSeverity.Information ? "local_only" : "queued");
        command.Parameters.AddWithValue("$now", now.UtcTicks);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private static NotificationRecord Record(SqliteDataReader reader) =>
        new(JsonSerializer.Deserialize<Notification>(reader.GetString(1))!,
            new((DeliveryState)reader.GetInt32(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)),
            reader.GetInt32(5), new(reader.GetInt64(6), TimeSpan.Zero), new(reader.GetInt64(7), TimeSpan.Zero));

    public async ValueTask<NotificationRecord?> ReadAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM notifications WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Record(reader) : null;
    }
    public async ValueTask<IReadOnlyList<NotificationRecord>> ReadDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM notifications WHERE next<=$now AND
            (state=$queued OR (state IN ($failed,$disabled) AND attempts<3) OR state=$pending)
            ORDER BY next,id LIMIT 32;
            """;
        command.Parameters.AddWithValue("$now", now.UtcTicks);
        command.Parameters.AddWithValue("$queued", (int)DeliveryState.Queued);
        command.Parameters.AddWithValue("$failed", (int)DeliveryState.Failed);
        command.Parameters.AddWithValue("$disabled", (int)DeliveryState.Disabled);
        command.Parameters.AddWithValue("$pending", (int)DeliveryState.PendingAcknowledgement);
        var results = new List<NotificationRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(Record(reader));
        return results;
    }
    public async ValueTask<bool> ClaimAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE notifications SET state=$uncertain,code='push_in_progress_or_uncertain',attempts=attempts+1
            WHERE id=$id AND state IN ($queued,$failed,$disabled) AND attempts<3;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$uncertain", (int)DeliveryState.Uncertain);
        command.Parameters.AddWithValue("$queued", (int)DeliveryState.Queued);
        command.Parameters.AddWithValue("$failed", (int)DeliveryState.Failed);
        command.Parameters.AddWithValue("$disabled", (int)DeliveryState.Disabled);
        var claimed = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (claimed)
        {
            command.CommandText = "INSERT INTO notification_events(id,state,code,at) VALUES($id,$uncertain,'push_in_progress_or_uncertain',$now);";
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.UtcTicks);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return claimed;
    }
    public async ValueTask CompleteAsync(string id, DeliveryResult result, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE notifications SET state=$state,code=$code,receipt=$receipt,next=$next WHERE id=$id;
            INSERT INTO notification_events(id,state,code,at) VALUES($id,$state,$code,$now);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$state", (int)result.State);
        command.Parameters.AddWithValue("$code", result.Code);
        command.Parameters.AddWithValue("$receipt", (object?)result.Receipt ?? DBNull.Value);
        command.Parameters.AddWithValue("$next", now.AddMinutes(1).UtcTicks);
        command.Parameters.AddWithValue("$now", now.UtcTicks);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
