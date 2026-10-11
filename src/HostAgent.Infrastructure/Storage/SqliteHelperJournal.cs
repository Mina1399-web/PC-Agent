using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Audit;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Core.Models.Policy;
using HostAgent.Core.Policy;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;
namespace HostAgent.Infrastructure.Storage;
// Separate helper-owned database: the monitoring service cannot mint approvals here.
public sealed class SqliteHelperJournal : IAuditStore, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly HashSet<string> _approvers;
    private readonly SemaphoreSlim _initialize = new(1, 1);
    private bool _initialized;
    private bool _disposed;
    public SqliteHelperJournal(string databasePath, IReadOnlyCollection<string> approverSids)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true, Pooling = false, DefaultTimeout = 5 }.ToString();
        _approvers = new(approverSids, StringComparer.Ordinal);
    }
    private async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(token);
            await NonQuery(connection, null, "PRAGMA busy_timeout=5000; PRAGMA synchronous=FULL;", token);
            await _initialize.WaitAsync(token);
            try
            {
                if (!_initialized)
                {
                    await using var transaction = connection.BeginTransaction();
                    await NonQuery(connection, transaction, Schema, token);
                    foreach (var table in new[] { "helper_audit", "helper_approvals", "helper_claims" })
                    foreach (var action in new[] { "UPDATE", "DELETE" })
                        await NonQuery(connection, transaction, $"CREATE TRIGGER IF NOT EXISTS {table}_no_{action} BEFORE {action} ON {table} BEGIN SELECT RAISE(ABORT, 'immutable helper journal'); END;", token);
                    await transaction.CommitAsync(token);
                    _initialized = true;
                }
            }
            finally { _initialize.Release(); }
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    public async ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await Append(connection, null, record, cancellationToken);
    }
    // Trusted local provisioning seam only. No pipe operation can call this method.
    public async ValueTask RecordApprovalAsync(OperationApproval approval, CancellationToken cancellationToken)
    {
        if (!_approvers.Contains(approval.ApproverSid)) throw new UnauthorizedAccessException("untrusted_approver");
        if (approval.ApprovalId == Guid.Empty || approval.RequestId == Guid.Empty ||
            approval.ExpiresAtUtc <= approval.ApprovedAtUtc || approval.ExpiresAtUtc > approval.ApprovedAtUtc.AddMinutes(10) ||
            approval.RequestDigest.Length != 64 || !Enum.IsDefined(approval.Operation)) throw new ArgumentException("invalid_approval");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await NonQuery(connection, transaction, "INSERT INTO helper_approvals(approval_id,request_id,payload_json) VALUES($id,$request,$payload);", cancellationToken,
            ("$id", approval.ApprovalId.ToString()), ("$request", approval.RequestId.ToString()), ("$payload", JsonSerializer.Serialize(approval)));
        await Append(connection, transaction, new(Guid.NewGuid(), approval.ApprovedAtUtc, AuditEvent.Approval,
            approval.RequestId, approval.IncidentId, approval.DiagnosisId, 0, [], approval.Operation,
            approval.RequestDigest, approval.ApproverSid, PolicyDecision.CONFIRM_REQUIRED,
            approval.ApprovalId, approval.ApproverSid, approval.ApprovedAtUtc, "approved"), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    public async ValueTask<OperationApproval?> ReadApprovalAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await Approval(connection, null, approvalId, cancellationToken);
    }
    public async ValueTask<string?> ReserveAsync(HelperOperationRequest request, OperationApproval approval,
        GuardLimits limits, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        // Immediate transaction serializes reservations across helper instances.
        await using var transaction = connection.BeginTransaction(deferred: false);
        var exists = await Scalar(connection, transaction, "SELECT COUNT(*) FROM helper_claims WHERE request_id=$id;", cancellationToken, ("$id", request.RequestId.ToString()));
        if (Convert.ToInt64(exists, CultureInfo.InvariantCulture) != 0) return "replayed_request";
        var stored = await Approval(connection, transaction, approval.ApprovalId, cancellationToken);
        if (stored != approval || !_approvers.Contains(approval.ApproverSid) || request.ApprovalId != approval.ApprovalId) return "approval_mismatch";
        var context = new PolicyContext { ReadOnly = false, Shadow = false, StorageHealthy = true,
            ConfigurationAvailable = true, OperationEnabled = true, Confidence = request.Confidence,
            RequestId = request.RequestId, IncidentId = request.IncidentId, DiagnosisId = request.DiagnosisId,
            Operation = request.Operation, RequestDigest = HelperProtocol.Digest(request), Approval = approval, NowUtc = nowUtc };
        if (new PolicyEngine().Evaluate(context) != PolicyDecision.AUTO_ALLOWED) return "approval_mismatch";
        var target = HelperProtocol.TargetKey(request);
        var unresolved = await Scalar(connection, transaction, """
            SELECT COUNT(*) FROM helper_claims c WHERE c.target_key=$target AND NOT EXISTS (
                SELECT 1 FROM helper_audit a WHERE a.request_id=c.request_id
                  AND json_extract(a.payload_json,'$.Event')=4
                  AND json_extract(a.payload_json,'$.Code') IN ('succeeded','execution_failed','not_executed'));
            """, cancellationToken, ("$target", target));
        if (Convert.ToInt64(unresolved, CultureInfo.InvariantCulture) > 0) return "unresolved_attempt";
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM helper_claims WHERE incident_id=$incident AND operation=$operation),
              (SELECT COUNT(*) FROM helper_claims WHERE target_key=$target AND started_at_utc>$cutoff),
              (SELECT COUNT(*) FROM helper_claims WHERE target_key=$target AND operation='RestartService' AND started_at_utc>$cutoff),
              (SELECT MAX(started_at_utc) FROM helper_claims WHERE target_key=$target);
            """;
        command.Parameters.AddWithValue("$incident", request.IncidentId);
        command.Parameters.AddWithValue("$operation", request.Operation.ToString());
        command.Parameters.AddWithValue("$target", target);
        command.Parameters.AddWithValue("$cutoff", Timestamp(nowUtc.AddHours(-1)));
        GuardHistory history;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            history = new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture));
        }
        var denial = new RemediationGuard().Denial(history, limits, request.Operation == HelperOperation.RestartService, nowUtc);
        if (denial is not null) return denial;
        await NonQuery(connection, transaction, """
            INSERT INTO helper_claims(request_id,approval_id,incident_id,operation,target_key,started_at_utc,request_digest)
            VALUES($request,$approval,$incident,$operation,$target,$time,$digest);
            """, cancellationToken, ("$request", request.RequestId.ToString()), ("$approval", approval.ApprovalId.ToString()),
            ("$incident", request.IncidentId), ("$operation", request.Operation.ToString()), ("$target", target),
            ("$time", Timestamp(nowUtc)), ("$digest", HelperProtocol.Digest(request)));
        await transaction.CommitAsync(cancellationToken);
        return null;
    }
    public async ValueTask<IReadOnlyList<AuditRecord>> ReadAuditAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM helper_audit WHERE request_id=$request ORDER BY sequence;";
        command.Parameters.AddWithValue("$request", requestId.ToString());
        var records = new List<AuditRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) records.Add(JsonSerializer.Deserialize<AuditRecord>(reader.GetString(0))!);
        return records;
    }
    private static async Task Append(SqliteConnection connection, SqliteTransaction? transaction, AuditRecord record, CancellationToken token)
    {
        if (record.AuditId == Guid.Empty || record.RequestId == Guid.Empty || record.Confidence is < 0 or > 100 ||
            !Enum.IsDefined(record.Event) || !Enum.IsDefined(record.Decision) || !Enum.IsDefined(record.Operation) ||
            record.Code.Length > 64 || record.InitiatorSid.Length > 184 || record.EvidenceReferences.Length > 32) throw new ArgumentException("invalid_audit");
        await NonQuery(connection, transaction, "INSERT INTO helper_audit(audit_id,request_id,payload_json) VALUES($id,$request,$payload);", token,
            ("$id", record.AuditId.ToString()), ("$request", record.RequestId.ToString()), ("$payload", JsonSerializer.Serialize(record)));
    }
    private static async Task<OperationApproval?> Approval(SqliteConnection connection, SqliteTransaction? transaction, Guid id, CancellationToken token)
    {
        var payload = await Scalar(connection, transaction, "SELECT payload_json FROM helper_approvals WHERE approval_id=$id;", token, ("$id", id.ToString()));
        return payload is string json ? JsonSerializer.Deserialize<OperationApproval>(json) : null;
    }
    private static async Task<object?> Scalar(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken token, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        return await command.ExecuteScalarAsync(token);
    }
    private static async Task NonQuery(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken token, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        await command.ExecuteNonQueryAsync(token);
    }
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }
    private static string Timestamp(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    public ValueTask DisposeAsync() { _disposed = true; return ValueTask.CompletedTask; }
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS helper_journal_schema(version INTEGER PRIMARY KEY CHECK(version=1));
        INSERT OR IGNORE INTO helper_journal_schema VALUES(1);
        CREATE TABLE IF NOT EXISTS helper_audit(sequence INTEGER PRIMARY KEY AUTOINCREMENT,
            audit_id TEXT UNIQUE NOT NULL,request_id TEXT NOT NULL,payload_json TEXT NOT NULL CHECK(json_valid(payload_json)));
        CREATE INDEX IF NOT EXISTS ix_helper_audit_request ON helper_audit(request_id,sequence);
        CREATE TABLE IF NOT EXISTS helper_approvals(approval_id TEXT PRIMARY KEY NOT NULL,request_id TEXT UNIQUE NOT NULL,
            payload_json TEXT NOT NULL CHECK(json_valid(payload_json)));
        CREATE TABLE IF NOT EXISTS helper_claims(request_id TEXT PRIMARY KEY NOT NULL,approval_id TEXT UNIQUE NOT NULL
            REFERENCES helper_approvals(approval_id),incident_id TEXT NOT NULL,operation TEXT NOT NULL,
            target_key TEXT NOT NULL,started_at_utc TEXT NOT NULL,request_digest TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_helper_claims_target_time ON helper_claims(target_key,started_at_utc);
        CREATE INDEX IF NOT EXISTS ix_helper_claims_incident_operation ON helper_claims(incident_id,operation);
        """;
}
