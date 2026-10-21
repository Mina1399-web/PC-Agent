using System.Text.Json;
using HostAgent.Core.Models.Incidents;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class IncidentReadIdentityTests
{
    // A valid JSON object can still disagree with its durable SQLite row.
    // Exact operational reads must reject this before a lifecycle transition
    // or approval attempt consumes an archive belonging to another identity.
    [Theory]
    [InlineData("IncidentId")]
    [InlineData("SnapshotId")]
    [InlineData("CapturedAtUtc")]
    public async Task Exact_read_rejects_snapshot_payload_identity_that_disagrees_with_its_row(string field)
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        var incidentId = fixture.Request.IncidentId;
        var original = Assert.IsType<Incident>(await fixture.Store.ReadIncidentAsync(incidentId, default));
        var snapshot = Assert.Single(original.Snapshots);
        var changed = field switch
        {
            "IncidentId" => snapshot with { IncidentId = Guid.NewGuid().ToString("N") },
            "SnapshotId" => snapshot with { SnapshotId = "different-durable-snapshot" },
            "CapturedAtUtc" => snapshot with { CapturedAtUtc = snapshot.CapturedAtUtc.AddTicks(1) },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        await using (var database = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.Path,
            Pooling = false
        }.ToString()))
        {
            await database.OpenAsync();
            await using var update = database.CreateCommand();
            update.CommandText = "UPDATE incident_snapshots SET payload_json=$payload WHERE incident_id=$incident AND snapshot_id=$snapshot;";
            update.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(changed));
            update.Parameters.AddWithValue("$incident", incidentId);
            update.Parameters.AddWithValue("$snapshot", snapshot.SnapshotId);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        await Assert.ThrowsAsync<JsonException>(() => fixture.Store.ReadIncidentAsync(incidentId, default).AsTask());
    }

    [Fact]
    public async Task Valid_exact_read_preserves_selected_head_snapshot_and_diagnostic_evidence()
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();

        var incident = Assert.IsType<Incident>(await fixture.Store.ReadIncidentAsync(fixture.Request.IncidentId, default));

        Assert.Equal(fixture.Request.IncidentId, incident.IncidentId);
        Assert.Equal(fixture.Request.DiagnosisId, incident.LatestDiagnosis.DiagnosisId);
        Assert.Equal(90, incident.LatestDiagnosis.Confidence);
        var snapshot = Assert.Single(incident.Snapshots);
        Assert.Equal(incident.IncidentId, snapshot.IncidentId);
        Assert.Equal("snapshot", snapshot.SnapshotId);
        Assert.Equal(incident.UpdatedAtUtc, snapshot.CapturedAtUtc);
        Assert.Equal("evidence", Assert.Single(snapshot.DiagnosticEvidence).EvidenceId);
        Assert.Single(Assert.Single(await fixture.Store.ReadIncidentsAsync(default)).Snapshots);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("' OR 1=1 --")]
    public async Task Missing_exact_read_returns_null_without_returning_another_incident(string incidentId)
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();

        Assert.Null(await fixture.Store.ReadIncidentAsync(incidentId, default));

        Assert.Single(await fixture.Store.ReadIncidentsAsync(default));
    }
}
