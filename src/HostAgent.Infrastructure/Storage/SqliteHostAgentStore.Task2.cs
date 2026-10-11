using System.Text.Json;
using HostAgent.Core.Models.Baseline;
using HostAgent.Core.Models.Incidents;
using HostAgent.Core.Models.Telemetry;

namespace HostAgent.Infrastructure.Storage;

public sealed partial class SqliteHostAgentStore
{
    public async ValueTask<IReadOnlyList<MetricSample>> ReadMetricSamplesAsync(DateTimeOffset fromUtc,
        DateTimeOffset throughUtc, int limit, CancellationToken cancellationToken)
    {
        if (fromUtc > throughUtc || limit < 1 || limit > 1_000_000) throw new ArgumentOutOfRangeException(nameof(limit));
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sample_id, metric_name, scope, sampled_at_utc, value FROM baseline_samples
            WHERE sampled_at_utc >= $from AND sampled_at_utc <= $through
            ORDER BY sampled_at_utc, sample_id LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$from", FormatTimestamp(fromUtc));
        command.Parameters.AddWithValue("$through", FormatTimestamp(throughUtc));
        command.Parameters.AddWithValue("$limit", limit + 1);
        var samples = new List<MetricSample>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            samples.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), ParseTimestamp(reader.GetString(3)), reader.GetDouble(4)));
        if (samples.Count > limit) throw new InvalidOperationException("Metric restore exceeded its bounded read capacity.");
        return samples;
    }

    public async ValueTask<IReadOnlyList<MetricSample>> ReadMetricSeriesAsync(string metricName, string scope,
        DateTimeOffset fromUtc, DateTimeOffset throughUtc, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metricName);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (fromUtc > throughUtc || limit < 1 || limit > 50_000) throw new ArgumentOutOfRangeException(nameof(limit));
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sample_id, metric_name, scope, sampled_at_utc, value FROM baseline_samples
            WHERE metric_name=$metric AND scope=$scope AND sampled_at_utc >= $from AND sampled_at_utc <= $through
            ORDER BY sampled_at_utc DESC, sample_id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$metric", metricName);
        command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$from", FormatTimestamp(fromUtc));
        command.Parameters.AddWithValue("$through", FormatTimestamp(throughUtc));
        command.Parameters.AddWithValue("$limit", limit);
        var samples = new List<MetricSample>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            samples.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), ParseTimestamp(reader.GetString(3)), reader.GetDouble(4)));
        samples.Reverse();
        return samples;
    }

    public async ValueTask<IReadOnlyList<BaselineOrigin>> ReadBaselineOriginsAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT metric_name, scope, started_at_utc FROM baseline_origins;";
        var origins = new List<BaselineOrigin>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            origins.Add(new(reader.GetString(0), reader.GetString(1), ParseTimestamp(reader.GetString(2))));
        return origins;
    }

    public async ValueTask SaveIncidentBundleAsync(Incident incident, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentException.ThrowIfNullOrWhiteSpace(incident.IncidentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(incident.CorrelationKey);
        if (incident.Snapshots.Any(snapshot => snapshot.IncidentId != incident.IncidentId))
            throw new ArgumentException("Snapshot belongs to a different incident.");
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await ExecuteNonQueryAsync(connection, transaction, """
            INSERT INTO incidents (incident_id, correlation_key, state, created_at_utc, updated_at_utc, payload_json)
            VALUES ($id, $key, $state, $created, $updated, $payload)
            ON CONFLICT(incident_id) DO UPDATE SET state=excluded.state, updated_at_utc=excluded.updated_at_utc,
                payload_json=excluded.payload_json WHERE excluded.updated_at_utc >= incidents.updated_at_utc;
            """, cancellationToken,
            ("$id", incident.IncidentId), ("$key", incident.CorrelationKey), ("$state", incident.State.ToString()),
            ("$created", FormatTimestamp(incident.CreatedAtUtc)), ("$updated", FormatTimestamp(incident.UpdatedAtUtc)),
            ("$payload", JsonSerializer.Serialize(incident with { Snapshots = [] })));
        foreach (var snapshot in incident.Snapshots)
        {
            await ExecuteNonQueryAsync(connection, transaction, """
                INSERT INTO incident_snapshots (snapshot_id, incident_id, captured_at_utc, payload_json)
                VALUES ($id, $incident, $time, $payload) ON CONFLICT(snapshot_id) DO NOTHING;
                """, cancellationToken, ("$id", snapshot.SnapshotId), ("$incident", incident.IncidentId),
                ("$time", FormatTimestamp(snapshot.CapturedAtUtc)), ("$payload", JsonSerializer.Serialize(snapshot)));
            foreach (var evidence in snapshot.DiagnosticEvidence)
            {
                await ExecuteNonQueryAsync(connection, transaction, """
                    INSERT INTO diagnostic_evidence (evidence_id, incident_id, evidence_type, recorded_at_utc, payload_json)
                    VALUES ($id, $incident, $source, $time, $payload) ON CONFLICT(evidence_id) DO NOTHING;
                    """, cancellationToken, ("$id", evidence.EvidenceId), ("$incident", incident.IncidentId),
                    ("$source", evidence.Source), ("$time", FormatTimestamp(evidence.ObservedAtUtc)), ("$payload", JsonSerializer.Serialize(evidence)));
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public ValueTask<IReadOnlyList<Incident>> ReadIncidentsAsync(CancellationToken cancellationToken) =>
        ReadIncidentsCoreAsync(false, cancellationToken);

    public ValueTask<IReadOnlyList<Incident>> ReadIncidentHeadsAsync(CancellationToken cancellationToken) =>
        ReadIncidentsCoreAsync(true, cancellationToken);

    private async ValueTask<IReadOnlyList<Incident>> ReadIncidentsCoreAsync(bool operational, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        var incidents = new List<Incident>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT payload_json FROM incidents ORDER BY created_at_utc, incident_id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                incidents.Add(JsonSerializer.Deserialize<Incident>(reader.GetString(0)) ?? throw new JsonException("Missing incident."));
        }
        for (var index = 0; index < incidents.Count; index++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = operational
                ? "SELECT payload_json FROM incident_snapshots WHERE incident_id=$id ORDER BY captured_at_utc DESC, snapshot_id DESC LIMIT 64;"
                : "SELECT payload_json FROM incident_snapshots WHERE incident_id=$id ORDER BY captured_at_utc, snapshot_id;";
            command.Parameters.AddWithValue("$id", incidents[index].IncidentId);
            var snapshots = new List<IncidentSnapshot>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                snapshots.Add(JsonSerializer.Deserialize<IncidentSnapshot>(reader.GetString(0)) ?? throw new JsonException("Missing snapshot."));
            if (operational) snapshots.Reverse();
            incidents[index] = incidents[index] with { Snapshots = snapshots };
        }
        await transaction.CommitAsync(cancellationToken);
        return incidents;
    }

    private const string BaselineOriginSchema = """
        CREATE TABLE baseline_origins (
            metric_name TEXT NOT NULL, scope TEXT NOT NULL, started_at_utc TEXT NOT NULL,
            PRIMARY KEY(metric_name, scope));
        INSERT INTO baseline_origins SELECT metric_name, scope, MIN(sampled_at_utc)
            FROM baseline_samples GROUP BY metric_name, scope;
        CREATE TRIGGER baseline_origin_on_sample AFTER INSERT ON baseline_samples BEGIN
            INSERT INTO baseline_origins (metric_name, scope, started_at_utc)
            VALUES (NEW.metric_name, NEW.scope, NEW.sampled_at_utc)
            ON CONFLICT(metric_name, scope) DO UPDATE SET started_at_utc=MIN(started_at_utc, excluded.started_at_utc);
        END;
        """;
}
