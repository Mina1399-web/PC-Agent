using System.Globalization;
using System.Text.Json;
using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Audit;
using HostAgent.Core.Models.Storage;
using HostAgent.Core.Models.Telemetry;
using Microsoft.Data.Sqlite;

namespace HostAgent.Infrastructure.Storage;

public sealed partial class SqliteHostAgentStore : IHostAgentStore
{
    public const int LatestSchemaVersion = 3;
    private static readonly TimeSpan MinimumDetailedRetention = TimeSpan.FromDays(30);
    private const int MaximumCheckpointLength = 16_384;
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private volatile bool _initialized;
    private bool _disposed;

    public SqliteHostAgentStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 15,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_databasePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("The database path must include a parent directory.", nameof(_databasePath));
            }

            Directory.CreateDirectory(directory);
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA journal_mode = WAL;", cancellationToken);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA synchronous = FULL;", cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                null,
                """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    applied_at_utc TEXT NOT NULL
                );
                """,
                cancellationToken);

            await using var transaction = connection.BeginTransaction();
            var version = await ReadSchemaVersionAsync(connection, transaction, cancellationToken);
            if (version > LatestSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Database schema version {version} is newer than supported version {LatestSchemaVersion}.");
            }

            if (version < 1)
            {
                await ExecuteNonQueryAsync(connection, transaction, InitialSchema, cancellationToken);
                await using var migration = connection.CreateCommand();
                migration.Transaction = transaction;
                migration.CommandText =
                    "INSERT INTO schema_migrations (version, applied_at_utc) VALUES ($version, $appliedAt);";
                migration.Parameters.AddWithValue("$version", 1);
                migration.Parameters.AddWithValue("$appliedAt", FormatTimestamp(DateTimeOffset.UtcNow));
                await migration.ExecuteNonQueryAsync(cancellationToken);
            }

            if (version < 2)
            {
                await ExecuteNonQueryAsync(connection, transaction, CheckpointSchema, cancellationToken);
                await using var migration = connection.CreateCommand();
                migration.Transaction = transaction;
                migration.CommandText =
                    "INSERT INTO schema_migrations (version, applied_at_utc) VALUES ($version, $appliedAt);";
                migration.Parameters.AddWithValue("$version", 2);
                migration.Parameters.AddWithValue("$appliedAt", FormatTimestamp(DateTimeOffset.UtcNow));
                await migration.ExecuteNonQueryAsync(cancellationToken);
            }

            if (version < 3)
            {
                await ExecuteNonQueryAsync(connection, transaction, BaselineOriginSchema, cancellationToken);
                await ExecuteNonQueryAsync(connection, transaction,
                    "INSERT INTO schema_migrations (version, applied_at_utc) VALUES (3, $time);",
                    cancellationToken, ("$time", FormatTimestamp(DateTimeOffset.UtcNow)));
            }

            await transaction.CommitAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async ValueTask<int> GetSchemaVersionAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await ReadSchemaVersionAsync(connection, null, cancellationToken);
    }

    public async ValueTask SaveTelemetrySnapshotAsync(
        TelemetrySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.CollectorId);
        if (snapshot.SnapshotId == Guid.Empty || snapshot.PayloadSchemaVersion < 1)
        {
            throw new ArgumentException("A telemetry snapshot requires an ID and positive schema version.", nameof(snapshot));
        }

        ValidateJson(snapshot.PayloadJson, nameof(snapshot.PayloadJson));
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO telemetry_snapshots
                (snapshot_id, collector_id, payload_schema_version, sampled_at_utc, payload_json)
            VALUES
                ($id, $collectorId, $schemaVersion, $sampledAt, $payload);
            """;
        command.Parameters.AddWithValue("$id", snapshot.SnapshotId.ToString("D"));
        command.Parameters.AddWithValue("$collectorId", snapshot.CollectorId);
        command.Parameters.AddWithValue("$schemaVersion", snapshot.PayloadSchemaVersion);
        command.Parameters.AddWithValue("$sampledAt", FormatTimestamp(snapshot.SampledAtUtc));
        command.Parameters.AddWithValue("$payload", snapshot.PayloadJson);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<TelemetrySnapshot>> ReadTelemetrySnapshotsAsync(
        string collectorId,
        DateTimeOffset fromUtc,
        DateTimeOffset throughUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectorId);
        if (fromUtc > throughUtc)
        {
            throw new ArgumentException("The start of a telemetry range must not be later than its end.", nameof(fromUtc));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT snapshot_id, collector_id, payload_schema_version, sampled_at_utc, payload_json
            FROM telemetry_snapshots
            WHERE collector_id = $collectorId
              AND sampled_at_utc >= $fromUtc
              AND sampled_at_utc <= $throughUtc
            ORDER BY sampled_at_utc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$collectorId", collectorId);
        command.Parameters.AddWithValue("$fromUtc", FormatTimestamp(fromUtc));
        command.Parameters.AddWithValue("$throughUtc", FormatTimestamp(throughUtc));
        command.Parameters.AddWithValue("$limit", limit);

        var snapshots = new List<TelemetrySnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            snapshots.Add(new TelemetrySnapshot(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt32(2),
                ParseTimestamp(reader.GetString(3)),
                reader.GetString(4)));
        }

        return snapshots.AsReadOnly();
    }

    public async ValueTask SaveMetricSampleAsync(
        MetricSample sample,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentException.ThrowIfNullOrWhiteSpace(sample.MetricName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sample.Scope);
        if (sample.SampleId == Guid.Empty || !double.IsFinite(sample.Value))
        {
            throw new ArgumentException("A metric sample requires an ID and finite numeric value.", nameof(sample));
        }

        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO baseline_samples (sample_id, metric_name, scope, sampled_at_utc, value)
            VALUES ($id, $metric, $scope, $sampledAt, $value);
            """;
        command.Parameters.AddWithValue("$id", sample.SampleId.ToString("D"));
        command.Parameters.AddWithValue("$metric", sample.MetricName);
        command.Parameters.AddWithValue("$scope", sample.Scope);
        command.Parameters.AddWithValue("$sampledAt", FormatTimestamp(sample.SampledAtUtc));
        command.Parameters.AddWithValue("$value", sample.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<MetricAggregate>> ReadMetricAggregatesAsync(
        AggregateGranularity granularity,
        string metricName,
        string scope,
        DateTimeOffset fromUtc,
        DateTimeOffset throughUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metricName);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (fromUtc > throughUtc)
        {
            throw new ArgumentException("The start of an aggregate range must not be later than its end.", nameof(fromUtc));
        }

        var table = granularity switch
        {
            AggregateGranularity.Hourly => "hourly_metric_aggregates",
            AggregateGranularity.Daily => "daily_metric_aggregates",
            _ => throw new ArgumentOutOfRangeException(nameof(granularity))
        };

        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT metric_name, scope, bucket_start_utc, sample_count, sum_value, minimum, maximum
            FROM {table}
            WHERE metric_name = $metric AND scope = $scope
              AND bucket_start_utc >= $fromUtc AND bucket_start_utc <= $throughUtc
            ORDER BY bucket_start_utc;
            """;
        command.Parameters.AddWithValue("$metric", metricName);
        command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$fromUtc", FormatTimestamp(fromUtc));
        command.Parameters.AddWithValue("$throughUtc", FormatTimestamp(throughUtc));

        var aggregates = new List<MetricAggregate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            aggregates.Add(new MetricAggregate(
                reader.GetString(0),
                reader.GetString(1),
                ParseTimestamp(reader.GetString(2)),
                reader.GetInt64(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                reader.GetDouble(6)));
        }

        return aggregates.AsReadOnly();
    }

    public async ValueTask SaveIncidentAsync(
        IncidentStorageRecord incident,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentException.ThrowIfNullOrWhiteSpace(incident.IncidentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(incident.CorrelationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(incident.State);
        ValidateJson(incident.PayloadJson, nameof(incident.PayloadJson));
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO incidents
                (incident_id, correlation_key, state, created_at_utc, updated_at_utc, payload_json)
            VALUES
                ($id, $correlationKey, $state, $created, $updated, $payload)
            ON CONFLICT (incident_id) DO UPDATE SET
                correlation_key = excluded.correlation_key,
                state = excluded.state,
                updated_at_utc = excluded.updated_at_utc,
                payload_json = excluded.payload_json;
            """;
        command.Parameters.AddWithValue("$id", incident.IncidentId);
        command.Parameters.AddWithValue("$correlationKey", incident.CorrelationKey);
        command.Parameters.AddWithValue("$state", incident.State);
        command.Parameters.AddWithValue("$created", FormatTimestamp(incident.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated", FormatTimestamp(incident.UpdatedAtUtc));
        command.Parameters.AddWithValue("$payload", incident.PayloadJson);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask SaveIncidentSnapshotAsync(
        IncidentSnapshotStorageRecord snapshot,
        CancellationToken cancellationToken)
    {
        await InsertPayloadRecordAsync(
            "incident_snapshots",
            "snapshot_id",
            snapshot?.SnapshotId,
            "incident_id, captured_at_utc",
            "$incidentId, $recordedAt",
            command =>
            {
                command.Parameters.AddWithValue("$incidentId", snapshot!.IncidentId);
                command.Parameters.AddWithValue("$recordedAt", FormatTimestamp(snapshot.CapturedAtUtc));
            },
            snapshot?.IncidentId,
            snapshot?.CapturedAtUtc,
            snapshot?.PayloadJson,
            cancellationToken);
    }

    public async ValueTask SaveDiagnosticEvidenceAsync(
        DiagnosticEvidenceStorageRecord evidence,
        CancellationToken cancellationToken)
    {
        await InsertPayloadRecordAsync(
            "diagnostic_evidence",
            "evidence_id",
            evidence?.EvidenceId,
            "incident_id, evidence_type, recorded_at_utc",
            "$incidentId, $evidenceType, $recordedAt",
            command =>
            {
                command.Parameters.AddWithValue("$incidentId", evidence!.IncidentId);
                command.Parameters.AddWithValue("$evidenceType", evidence.EvidenceType);
                command.Parameters.AddWithValue("$recordedAt", FormatTimestamp(evidence.RecordedAtUtc));
            },
            evidence?.IncidentId,
            evidence?.RecordedAtUtc,
            evidence?.PayloadJson,
            cancellationToken);
    }

    public async ValueTask AppendAuditEntryAsync(
        AuditEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.AuditId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Initiator);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.RequestedAction);
        if (entry.Confidence is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(entry), "Audit confidence must be between 0 and 100.");
        }

        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO audit_entries (
                audit_id, recorded_at_utc, initiator, incident_id, diagnosis, confidence,
                evidence_references_json, requested_action, policy_decision, approval_status,
                execution_started_at_utc, execution_ended_at_utc, result, error
            )
            VALUES (
                $id, $recordedAt, $initiator, $incidentId, $diagnosis, $confidence,
                $evidence, $action, $decision, $approval, $started, $ended, $result, $error
            );
            """;
        command.Parameters.AddWithValue("$id", entry.AuditId);
        command.Parameters.AddWithValue("$recordedAt", FormatTimestamp(entry.RecordedAtUtc));
        command.Parameters.AddWithValue("$initiator", entry.Initiator);
        command.Parameters.AddWithValue("$incidentId", DbValue(entry.IncidentId));
        command.Parameters.AddWithValue("$diagnosis", entry.Diagnosis);
        command.Parameters.AddWithValue("$confidence", entry.Confidence);
        command.Parameters.AddWithValue("$evidence", JsonSerializer.Serialize(entry.EvidenceReferences));
        command.Parameters.AddWithValue("$action", entry.RequestedAction);
        command.Parameters.AddWithValue("$decision", entry.PolicyDecision.ToString());
        command.Parameters.AddWithValue("$approval", entry.ApprovalStatus.ToString());
        command.Parameters.AddWithValue("$started", DbValue(entry.ExecutionStartedAtUtc is { } started
            ? FormatTimestamp(started)
            : null));
        command.Parameters.AddWithValue("$ended", DbValue(entry.ExecutionEndedAtUtc is { } ended
            ? FormatTimestamp(ended)
            : null));
        command.Parameters.AddWithValue("$result", DbValue(entry.Result));
        command.Parameters.AddWithValue("$error", DbValue(entry.Error));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask AppendRemediationAttemptAsync(
        RemediationAttemptStorageRecord attempt,
        CancellationToken cancellationToken)
    {
        await InsertPayloadRecordAsync(
            "remediation_attempts",
            "attempt_id",
            attempt?.AttemptId,
            "incident_id, operation_id, status, requested_at_utc",
            "$incidentId, $operationId, $status, $recordedAt",
            command =>
            {
                command.Parameters.AddWithValue("$incidentId", attempt!.IncidentId);
                command.Parameters.AddWithValue("$operationId", attempt.OperationId);
                command.Parameters.AddWithValue("$status", attempt.Status);
                command.Parameters.AddWithValue("$recordedAt", FormatTimestamp(attempt.RequestedAtUtc));
            },
            attempt?.IncidentId,
            attempt?.RequestedAtUtc,
            attempt?.PayloadJson,
            cancellationToken);
    }

    public async ValueTask SaveServiceHealthAsync(
        ServiceHealthSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await InsertPayloadRecordAsync(
            "service_health",
            "health_id",
            snapshot?.ComponentId + ":" + snapshot?.ObservedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
            "component_id, status, observed_at_utc",
            "$componentId, $status, $recordedAt",
            command =>
            {
                command.Parameters.AddWithValue("$componentId", snapshot!.ComponentId);
                command.Parameters.AddWithValue("$status", snapshot.Status);
                command.Parameters.AddWithValue("$recordedAt", FormatTimestamp(snapshot.ObservedAtUtc));
            },
            snapshot?.ComponentId,
            snapshot?.ObservedAtUtc,
            snapshot?.PayloadJson,
            cancellationToken);
    }

    public async ValueTask SaveTelemetryCollectionAsync(
        TelemetrySnapshot? snapshot,
        IReadOnlyList<MetricSample> metricSamples,
        IReadOnlyList<CollectorCheckpoint> checkpoints,
        ServiceHealthSnapshot health,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metricSamples);
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentException.ThrowIfNullOrWhiteSpace(health.ComponentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(health.Status);
        ValidateJson(health.PayloadJson, nameof(health.PayloadJson));

        if (snapshot is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.CollectorId);
            if (snapshot.SnapshotId == Guid.Empty || snapshot.PayloadSchemaVersion < 1)
            {
                throw new ArgumentException(
                    "A telemetry snapshot requires an ID and positive schema version.",
                    nameof(snapshot));
            }

            if (!string.Equals(snapshot.CollectorId, health.ComponentId, StringComparison.Ordinal)
                || snapshot.SampledAtUtc != health.ObservedAtUtc)
            {
                throw new ArgumentException("Telemetry snapshot and health must belong to the same collection.", nameof(health));
            }

            ValidateJson(snapshot.PayloadJson, nameof(snapshot.PayloadJson));
        }
        else if (metricSamples.Count != 0 || checkpoints.Count != 0)
        {
            throw new ArgumentException("Metrics and checkpoints require a persisted telemetry snapshot.", nameof(snapshot));
        }

        foreach (var sample in metricSamples)
        {
            ArgumentNullException.ThrowIfNull(sample);
            ArgumentException.ThrowIfNullOrWhiteSpace(sample.MetricName);
            ArgumentException.ThrowIfNullOrWhiteSpace(sample.Scope);
            if (sample.SampleId == Guid.Empty || !double.IsFinite(sample.Value))
            {
                throw new ArgumentException("A metric sample requires an ID and finite numeric value.", nameof(metricSamples));
            }

            if (snapshot is not null && sample.SampledAtUtc.ToUniversalTime() != snapshot.SampledAtUtc)
            {
                throw new ArgumentException("Metric samples must match the collection timestamp.", nameof(metricSamples));
            }
        }

        foreach (var checkpoint in checkpoints)
        {
            ArgumentNullException.ThrowIfNull(checkpoint);
            ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.Key);
            ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.Value);
            if (checkpoint.Value.Length > MaximumCheckpointLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(checkpoints),
                    $"A collector checkpoint cannot exceed {MaximumCheckpointLength} characters.");
            }
        }

        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        if (snapshot is not null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO telemetry_snapshots
                    (snapshot_id, collector_id, payload_schema_version, sampled_at_utc, payload_json)
                VALUES
                    ($id, $collectorId, $schemaVersion, $sampledAt, $payload);
                """;
            command.Parameters.AddWithValue("$id", snapshot.SnapshotId.ToString("D"));
            command.Parameters.AddWithValue("$collectorId", snapshot.CollectorId);
            command.Parameters.AddWithValue("$schemaVersion", snapshot.PayloadSchemaVersion);
            command.Parameters.AddWithValue("$sampledAt", FormatTimestamp(snapshot.SampledAtUtc));
            command.Parameters.AddWithValue("$payload", snapshot.PayloadJson);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var sample in metricSamples)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO baseline_samples (sample_id, metric_name, scope, sampled_at_utc, value)
                VALUES ($id, $metric, $scope, $sampledAt, $value);
                """;
            command.Parameters.AddWithValue("$id", sample.SampleId.ToString("D"));
            command.Parameters.AddWithValue("$metric", sample.MetricName);
            command.Parameters.AddWithValue("$scope", sample.Scope);
            command.Parameters.AddWithValue("$sampledAt", FormatTimestamp(sample.SampledAtUtc));
            command.Parameters.AddWithValue("$value", sample.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var checkpoint in checkpoints)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO collector_checkpoints
                    (collector_id, checkpoint_key, checkpoint_value, updated_at_utc)
                VALUES
                    ($collectorId, $checkpointKey, $checkpointValue, $updatedAt)
                ON CONFLICT (collector_id, checkpoint_key) DO UPDATE SET
                    checkpoint_value = excluded.checkpoint_value,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$collectorId", snapshot?.CollectorId ?? health.ComponentId);
            command.Parameters.AddWithValue("$checkpointKey", checkpoint.Key);
            command.Parameters.AddWithValue("$checkpointValue", checkpoint.Value);
            command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(health.ObservedAtUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO service_health
                    (health_id, component_id, status, observed_at_utc, payload_json)
                VALUES
                    ($id, $componentId, $status, $observedAt, $payload);
                """;
            command.Parameters.AddWithValue(
                "$id",
                health.ComponentId + ":" + health.ObservedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$componentId", health.ComponentId);
            command.Parameters.AddWithValue("$status", health.Status);
            command.Parameters.AddWithValue("$observedAt", FormatTimestamp(health.ObservedAtUtc));
            command.Parameters.AddWithValue("$payload", health.PayloadJson);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask<string?> ReadCollectorCheckpointAsync(
        string collectorId,
        string checkpointKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointKey);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT checkpoint_value FROM collector_checkpoints WHERE collector_id = $collectorId AND checkpoint_key = $checkpointKey;";
        command.Parameters.AddWithValue("$collectorId", collectorId);
        command.Parameters.AddWithValue("$checkpointKey", checkpointKey);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    public async ValueTask SaveCollectorCheckpointAsync(
        string collectorId,
        string checkpointKey,
        string checkpointValue,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointValue);
        if (checkpointValue.Length > MaximumCheckpointLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(checkpointValue),
                $"A collector checkpoint cannot exceed {MaximumCheckpointLength} characters.");
        }

        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO collector_checkpoints
                (collector_id, checkpoint_key, checkpoint_value, updated_at_utc)
            VALUES
                ($collectorId, $checkpointKey, $checkpointValue, $updatedAt)
            ON CONFLICT (collector_id, checkpoint_key) DO UPDATE SET
                checkpoint_value = excluded.checkpoint_value,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$collectorId", collectorId);
        command.Parameters.AddWithValue("$checkpointKey", checkpointKey);
        command.Parameters.AddWithValue("$checkpointValue", checkpointValue);
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(updatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<int> ApplyRetentionAsync(
        DateTimeOffset nowUtc,
        TimeSpan detailedRetention,
        CancellationToken cancellationToken)
    {
        if (detailedRetention < MinimumDetailedRetention)
        {
            throw new ArgumentOutOfRangeException(
                nameof(detailedRetention),
                $"Detailed telemetry retention cannot be less than {MinimumDetailedRetention.TotalDays:0} days.");
        }

        await EnsureInitializedAsync(cancellationToken);
        var cutoff = nowUtc.ToUniversalTime() - detailedRetention;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var oldSamples = await ReadOldSamplesAsync(connection, transaction, cutoff, cancellationToken);
        var hourlyGroups = oldSamples.GroupBy(static sample => new AggregateKey(
            sample.MetricName,
            sample.Scope,
            new DateTimeOffset(
                sample.SampledAtUtc.Year,
                sample.SampledAtUtc.Month,
                sample.SampledAtUtc.Day,
                sample.SampledAtUtc.Hour,
                0,
                0,
                TimeSpan.Zero)));
        var dailyGroups = oldSamples.GroupBy(static sample => new AggregateKey(
            sample.MetricName,
            sample.Scope,
            new DateTimeOffset(
                sample.SampledAtUtc.Year,
                sample.SampledAtUtc.Month,
                sample.SampledAtUtc.Day,
                0,
                0,
                0,
                TimeSpan.Zero)));

        foreach (var group in hourlyGroups)
        {
            await UpsertAggregateAsync(
                connection,
                transaction,
                "hourly_metric_aggregates",
                group.Key,
                group.Select(static sample => sample.Value),
                cancellationToken);
        }

        foreach (var group in dailyGroups)
        {
            await UpsertAggregateAsync(
                connection,
                transaction,
                "daily_metric_aggregates",
                group.Key,
                group.Select(static sample => sample.Value),
                cancellationToken);
        }

        var deletedSamples = await ExecuteNonQueryAsync(
            connection,
            transaction,
            "DELETE FROM baseline_samples WHERE sampled_at_utc < $cutoff;",
            cancellationToken,
            ("$cutoff", FormatTimestamp(cutoff)));
        var deletedSnapshots = await ExecuteNonQueryAsync(
            connection,
            transaction,
            "DELETE FROM telemetry_snapshots WHERE sampled_at_utc < $cutoff;",
            cancellationToken,
            ("$cutoff", FormatTimestamp(cutoff)));
        var deletedHealth = await ExecuteNonQueryAsync(
            connection,
            transaction,
            "DELETE FROM service_health WHERE observed_at_utc < $cutoff;",
            cancellationToken,
            ("$cutoff", FormatTimestamp(cutoff)));
        await transaction.CommitAsync(cancellationToken);
        return deletedSamples + deletedSnapshots + deletedHealth;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await InitializeAsync(cancellationToken);
    }

    private async ValueTask<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                null,
                "PRAGMA busy_timeout = 5000; PRAGMA foreign_keys = ON;",
                cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async ValueTask<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private async ValueTask<int> InsertPayloadRecordAsync(
        string table,
        string idColumn,
        string? id,
        string extraColumns,
        string extraValues,
        Action<SqliteCommand> addExtraParameters,
        string? relatedId,
        DateTimeOffset? recordedAtUtc,
        string? payloadJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(relatedId);
        ArgumentNullException.ThrowIfNull(recordedAtUtc);
        ValidateJson(payloadJson, nameof(payloadJson));
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {table} ({idColumn}, {extraColumns}, payload_json)
            VALUES ($id, {extraValues}, $payload);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$payload", payloadJson);
        addExtraParameters(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask<List<MetricSample>> ReadOldSamplesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT sample_id, metric_name, scope, sampled_at_utc, value
            FROM baseline_samples
            WHERE sampled_at_utc < $cutoff;
            """;
        command.Parameters.AddWithValue("$cutoff", FormatTimestamp(cutoff));
        var samples = new List<MetricSample>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            samples.Add(new MetricSample(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                ParseTimestamp(reader.GetString(3)),
                reader.GetDouble(4)));
        }

        return samples;
    }

    private async ValueTask UpsertAggregateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        AggregateKey key,
        IEnumerable<double> values,
        CancellationToken cancellationToken)
    {
        var valueList = values.ToArray();
        if (valueList.Length == 0)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {table}
                (metric_name, scope, bucket_start_utc, sample_count, sum_value, minimum, maximum)
            VALUES
                ($metric, $scope, $bucket, $count, $sum, $minimum, $maximum)
            ON CONFLICT (metric_name, scope, bucket_start_utc) DO UPDATE SET
                sample_count = sample_count + excluded.sample_count,
                sum_value = sum_value + excluded.sum_value,
                minimum = MIN(minimum, excluded.minimum),
                maximum = MAX(maximum, excluded.maximum);
            """;
        command.Parameters.AddWithValue("$metric", key.MetricName);
        command.Parameters.AddWithValue("$scope", key.Scope);
        command.Parameters.AddWithValue("$bucket", FormatTimestamp(key.BucketStartUtc));
        command.Parameters.AddWithValue("$count", valueList.Length);
        command.Parameters.AddWithValue("$sum", valueList.Sum());
        command.Parameters.AddWithValue("$minimum", valueList.Min());
        command.Parameters.AddWithValue("$maximum", valueList.Max());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async ValueTask<int> ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateJson(string? json, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json, parameterName);
        using var _ = JsonDocument.Parse(json);
    }

    private static object DbValue(string? value) => value is null ? DBNull.Value : value;

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string timestamp) =>
        DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .ToUniversalTime();

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record AggregateKey(string MetricName, string Scope, DateTimeOffset BucketStartUtc);

    private const string InitialSchema =
        """
        CREATE TABLE telemetry_snapshots (
            snapshot_id TEXT PRIMARY KEY NOT NULL,
            collector_id TEXT NOT NULL,
            payload_schema_version INTEGER NOT NULL CHECK (payload_schema_version > 0),
            sampled_at_utc TEXT NOT NULL,
            payload_json TEXT NOT NULL CHECK (json_valid(payload_json))
        );
        CREATE INDEX ix_telemetry_collector_time
            ON telemetry_snapshots (collector_id, sampled_at_utc);

        CREATE TABLE baseline_samples (
            sample_id TEXT PRIMARY KEY NOT NULL,
            metric_name TEXT NOT NULL,
            scope TEXT NOT NULL,
            sampled_at_utc TEXT NOT NULL,
            value REAL NOT NULL
        );
        CREATE INDEX ix_baseline_metric_scope_time
            ON baseline_samples (metric_name, scope, sampled_at_utc);

        CREATE TABLE hourly_metric_aggregates (
            metric_name TEXT NOT NULL,
            scope TEXT NOT NULL,
            bucket_start_utc TEXT NOT NULL,
            sample_count INTEGER NOT NULL CHECK (sample_count > 0),
            sum_value REAL NOT NULL,
            minimum REAL NOT NULL,
            maximum REAL NOT NULL,
            PRIMARY KEY (metric_name, scope, bucket_start_utc)
        );
        CREATE TABLE daily_metric_aggregates (
            metric_name TEXT NOT NULL,
            scope TEXT NOT NULL,
            bucket_start_utc TEXT NOT NULL,
            sample_count INTEGER NOT NULL CHECK (sample_count > 0),
            sum_value REAL NOT NULL,
            minimum REAL NOT NULL,
            maximum REAL NOT NULL,
            PRIMARY KEY (metric_name, scope, bucket_start_utc)
        );

        CREATE TABLE incidents (
            incident_id TEXT PRIMARY KEY NOT NULL,
            correlation_key TEXT NOT NULL UNIQUE,
            state TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            payload_json TEXT NOT NULL CHECK (json_valid(payload_json))
        );
        CREATE INDEX ix_incidents_state_update ON incidents (state, updated_at_utc);

        CREATE TABLE incident_snapshots (
            snapshot_id TEXT PRIMARY KEY NOT NULL,
            incident_id TEXT NOT NULL REFERENCES incidents (incident_id) ON DELETE RESTRICT,
            captured_at_utc TEXT NOT NULL,
            payload_json TEXT NOT NULL CHECK (json_valid(payload_json))
        );
        CREATE INDEX ix_incident_snapshots_incident_time
            ON incident_snapshots (incident_id, captured_at_utc);

        CREATE TABLE diagnostic_evidence (
            evidence_id TEXT PRIMARY KEY NOT NULL,
            incident_id TEXT NOT NULL REFERENCES incidents (incident_id) ON DELETE RESTRICT,
            evidence_type TEXT NOT NULL,
            recorded_at_utc TEXT NOT NULL,
            payload_json TEXT NOT NULL CHECK (json_valid(payload_json))
        );
        CREATE INDEX ix_diagnostic_evidence_incident_time
            ON diagnostic_evidence (incident_id, recorded_at_utc);

        CREATE TABLE audit_entries (
            audit_id TEXT PRIMARY KEY NOT NULL,
            recorded_at_utc TEXT NOT NULL,
            initiator TEXT NOT NULL,
            incident_id TEXT,
            diagnosis TEXT NOT NULL,
            confidence INTEGER NOT NULL CHECK (confidence BETWEEN 0 AND 100),
            evidence_references_json TEXT NOT NULL CHECK (json_valid(evidence_references_json)),
            requested_action TEXT NOT NULL,
            policy_decision TEXT NOT NULL,
            approval_status TEXT NOT NULL,
            execution_started_at_utc TEXT,
            execution_ended_at_utc TEXT,
            result TEXT,
            error TEXT
        );
        CREATE TRIGGER audit_entries_no_update
            BEFORE UPDATE ON audit_entries
            BEGIN SELECT RAISE(ABORT, 'audit entries are immutable'); END;
        CREATE TRIGGER audit_entries_no_delete
            BEFORE DELETE ON audit_entries
            BEGIN SELECT RAISE(ABORT, 'audit entries are immutable'); END;

        CREATE TABLE remediation_attempts (
            attempt_id TEXT PRIMARY KEY NOT NULL,
            incident_id TEXT NOT NULL REFERENCES incidents (incident_id) ON DELETE RESTRICT,
            operation_id TEXT NOT NULL,
            status TEXT NOT NULL,
            requested_at_utc TEXT NOT NULL,
            payload_json TEXT NOT NULL CHECK (json_valid(payload_json))
        );
        CREATE INDEX ix_remediation_incident_time
            ON remediation_attempts (incident_id, requested_at_utc);

        CREATE TABLE service_health (
            health_id TEXT PRIMARY KEY NOT NULL,
            component_id TEXT NOT NULL,
            status TEXT NOT NULL,
            observed_at_utc TEXT NOT NULL,
            payload_json TEXT NOT NULL CHECK (json_valid(payload_json))
        );
        CREATE INDEX ix_service_health_component_time
            ON service_health (component_id, observed_at_utc);
        """;

    private const string CheckpointSchema =
        """
        CREATE TABLE collector_checkpoints (
            collector_id TEXT NOT NULL,
            checkpoint_key TEXT NOT NULL,
            checkpoint_value TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL,
            PRIMARY KEY (collector_id, checkpoint_key)
        );
        """;
}
