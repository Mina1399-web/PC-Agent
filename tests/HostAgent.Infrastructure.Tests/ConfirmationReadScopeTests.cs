using System.Text.Json;
using HostAgent.Core.Diagnostics;
using HostAgent.Core.Models.Diagnosis;
using HostAgent.Core.Models.Incidents;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Infrastructure.Security;
using HostAgent.Service;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class ConfirmationReadScopeTests
{
    // An unrelated archive's invalid payload must not prevent bounded admin
    // metadata reads or an exact, durable Shadow proposal for a healthy incident.
    [Theory]
    [InlineData("incidents")]
    [InlineData("incident")]
    [InlineData("propose")]
    public async Task Admin_metadata_and_shadow_proposal_do_not_load_unrelated_archives(string command)
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        var unrelatedId = await AddUnrelatedIncident(fixture);
        await InvalidateArchive(fixture, unrelatedId);
        using var remediation = new RemediationCoordinator(fixture.Store, fixture.Helper, new(), TimeProvider.System);
        var dispatcher = new AdminDispatcher(fixture.Store, remediation, new(), new(), (_, _, _) => ValueTask.CompletedTask);
        object body = command switch
        {
            "incidents" => new { },
            "incident" => new { incidentId = fixture.Request.IncidentId },
            _ => new { incidentId = fixture.Request.IncidentId, operation = "RestartService", parameters = new { serviceId = "svc" } }
        };

        using var reply = JsonDocument.Parse(await dispatcher.DispatchAsync(AdminProtocol.Request(command, body), default));

        Assert.True(reply.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(command == "propose" ? "shadow_proposal" : command, reply.RootElement.GetProperty("code").GetString());
        var data = reply.RootElement.GetProperty("data");
        if (command == "incidents")
        {
            Assert.Equal(2, data.GetArrayLength());
            var selected = Assert.Single(data.EnumerateArray(), item => item.GetProperty("IncidentId").GetString() == fixture.Request.IncidentId);
            Assert.Equal(90, selected.GetProperty("Confidence").GetInt32());
        }
        else if (command == "incident")
        {
            Assert.Equal(fixture.Request.IncidentId, data.GetProperty("incidentId").GetString());
            Assert.Equal("diag", data.GetProperty("diagnosisId").GetString());
            Assert.Equal(90, data.GetProperty("confidence").GetInt32());
            Assert.Equal("evidence", Assert.Single(data.GetProperty("evidence").EnumerateArray()).GetProperty("evidenceId").GetString());
        }
        else
        {
            var proposed = HelperProtocol.ParseRequest(JsonSerializer.SerializeToUtf8Bytes(data));
            AssertExactOperation(fixture.Request, proposed);
            Assert.Null(proposed.ApprovalId);
        }
        Assert.Equal(0, fixture.Helper.Calls);
        Assert.Equal(command == "propose" ? 1L : 0L, await AuditCount(fixture));
        // These metadata/proposal calls must neither repair nor hide the bad archive.
        await Assert.ThrowsAsync<JsonException>(() => fixture.Store.ReadIncidentsAsync(default).AsTask());
    }

    // Bypassing AdminDispatcher must not reintroduce global archive dependence
    // before the coordinator can record an exact Shadow decision.
    [Fact]
    public async Task Coordinator_shadow_proposal_preserves_exact_evidence_with_unrelated_invalid_archive()
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        await InvalidateArchive(fixture, await AddUnrelatedIncident(fixture));
        using var coordinator = new RemediationCoordinator(fixture.Store, fixture.Helper, new(), TimeProvider.System);

        var result = await coordinator.ProposeAsync(fixture.Request, null, default);

        Assert.Equal(HelperResult.Denied, result.Result);
        Assert.Equal("shadow_proposal", result.Code);
        Assert.Equal(0, fixture.Helper.Calls);
        Assert.Equal(1L, await AuditCount(fixture));
        await Assert.ThrowsAsync<JsonException>(() => fixture.Store.ReadIncidentsAsync(default).AsTask());
    }

    // Both initial validation and each persisted lifecycle transition must read
    // only the requested incident. The real privileged boundary remains a fake.
    [Fact]
    public async Task Exact_confirmation_survives_unrelated_bad_archive_and_persists_selected_lifecycle()
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        var unrelatedId = await AddUnrelatedIncident(fixture);
        await InvalidateArchive(fixture, unrelatedId);
        fixture.Helper.Response = HelperResult.Succeeded;
        var request = fixture.Request;
        var now = DateTimeOffset.UtcNow;
        var approval = new OperationApproval(Guid.NewGuid(), request.RequestId, request.IncidentId, request.DiagnosisId,
            request.Operation, HelperProtocol.Digest(request), "S-1-5-21-100-100-100-1001", now.AddSeconds(-1), now.AddMinutes(1));
        using var coordinator = new RemediationCoordinator(fixture.Store, fixture.Helper,
            new() { Mode = OperatingMode.Confirmation, ReadOnly = false, ConfigurationAvailable = true }, TimeProvider.System);

        var result = await coordinator.ProposeAsync(request, approval, default);

        Assert.Equal(HelperResult.Succeeded, result.Result);
        Assert.Equal("succeeded", result.Code);
        Assert.Equal(1, fixture.Helper.Calls);
        AssertExactOperation(request, Assert.IsType<HelperOperationRequest>(fixture.Helper.Last));
        Assert.Equal(approval.ApprovalId, fixture.Helper.Last!.ApprovalId);
        await using var database = await Open(fixture);
        await using var state = database.CreateCommand();
        state.CommandText = "SELECT state FROM incidents WHERE incident_id=$id;";
        state.Parameters.AddWithValue("$id", request.IncidentId);
        Assert.Equal("Resolved", await state.ExecuteScalarAsync());
        state.Parameters["$id"].Value = unrelatedId;
        Assert.Equal("Detected", await state.ExecuteScalarAsync());
        await using var snapshots = database.CreateCommand();
        snapshots.CommandText = "SELECT payload_json FROM incident_snapshots WHERE incident_id=$id ORDER BY captured_at_utc, snapshot_id;";
        snapshots.Parameters.AddWithValue("$id", request.IncidentId);
        await using var rows = await snapshots.ExecuteReaderAsync();
        var selectedSnapshots = new List<IncidentSnapshot>();
        while (await rows.ReadAsync()) selectedSnapshots.Add(JsonSerializer.Deserialize<IncidentSnapshot>(rows.GetString(0))!);
        Assert.True(selectedSnapshots.Count >= 2);
        Assert.All(selectedSnapshots, snapshot =>
        {
            Assert.Equal(request.IncidentId, snapshot.IncidentId);
            Assert.Equal("evidence", Assert.Single(snapshot.DiagnosticEvidence).EvidenceId);
        });
        await Assert.ThrowsAsync<JsonException>(() => fixture.Store.ReadIncidentsAsync(default).AsTask());
    }

    // Selective reads must not weaken authoritative diagnosis/evidence checks.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unrelated_archive_isolation_does_not_accept_changed_confidence_or_evidence(bool changeConfidence)
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        await InvalidateArchive(fixture, await AddUnrelatedIncident(fixture));
        var request = changeConfidence
            ? fixture.Request with { Confidence = 89 }
            : fixture.Request with { EvidenceReferences = ["different-evidence"] };
        using var coordinator = new RemediationCoordinator(fixture.Store, fixture.Helper,
            new() { Mode = OperatingMode.Confirmation, ReadOnly = false, ConfigurationAvailable = true }, TimeProvider.System);

        var result = await coordinator.ProposeAsync(request, null, default);

        Assert.Equal(HelperResult.Denied, result.Result);
        Assert.Equal("incident_not_actionable", result.Code);
        Assert.Equal(0, fixture.Helper.Calls);
        Assert.Equal(0L, await AuditCount(fixture));
    }

    // The selected incident's durable snapshots remain mandatory to a real
    // approved transition; a summary must never substitute an empty archive.
    [Fact]
    public async Task Invalid_selected_archive_denies_confirmation_before_helper_execution()
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        await InvalidateArchive(fixture, fixture.Request.IncidentId);
        var request = fixture.Request;
        var now = DateTimeOffset.UtcNow;
        var approval = new OperationApproval(Guid.NewGuid(), request.RequestId, request.IncidentId, request.DiagnosisId,
            request.Operation, HelperProtocol.Digest(request), "S-1-5-21-100-100-100-1001", now.AddSeconds(-1), now.AddMinutes(1));
        using var coordinator = new RemediationCoordinator(fixture.Store, fixture.Helper,
            new() { Mode = OperatingMode.Confirmation, ReadOnly = false, ConfigurationAvailable = true }, TimeProvider.System);

        var result = await coordinator.ProposeAsync(request, approval, default);

        Assert.Equal(HelperResult.Denied, result.Result);
        Assert.Equal("remediation_storage_or_validation_unavailable", result.Code);
        Assert.Equal(0, fixture.Helper.Calls);
        Assert.Equal(0L, await AuditCount(fixture));
    }

    private static void AssertExactOperation(HelperOperationRequest expected, HelperOperationRequest actual)
    {
        Assert.Equal(expected.IncidentId, actual.IncidentId);
        Assert.Equal("diag", actual.DiagnosisId);
        Assert.Equal(90, actual.Confidence);
        Assert.Equal(new[] { "evidence" }, actual.EvidenceReferences);
        Assert.Equal(HelperOperation.RestartService, actual.Operation);
        Assert.Equal("svc", Assert.IsType<ServiceParameters>(actual.Parameters).ServiceId);
    }

    private static async Task<string> AddUnrelatedIncident(OperatingModeTests.Fixture fixture)
    {
        var at = DateTimeOffset.UtcNow.AddMinutes(-2);
        var diagnosis = new Diagnosis("unrelated-diag", "unrelated-service", at, DiagnosisSeverity.High, 78,
            [new("unrelated-evidence", "fixture", "Synthetic unrelated", null, at)],
            new("service_crash", "Synthetic unrelated", "other-svc"), [], new("inspect", "Synthetic"), IncidentLifecycleState.Detected);
        var incident = await new IncidentEngine().ApplyAsync(diagnosis,
            new("unrelated-snapshot", "", at, null, null, null, null, null, null, [], diagnosis.Evidence));
        await fixture.Store.SaveIncidentBundleAsync(incident, default);
        return incident.IncidentId;
    }

    private static async Task InvalidateArchive(OperatingModeTests.Fixture fixture, string incidentId)
    {
        await using var database = await Open(fixture);
        await using var command = database.CreateCommand();
        // Valid JSON, invalid IncidentSnapshot shape: exercises archive-only
        // deserialization without corrupting SQLite or the valid incident head.
        command.CommandText = "UPDATE incident_snapshots SET payload_json='[]' WHERE incident_id=$id;";
        command.Parameters.AddWithValue("$id", incidentId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<long> AuditCount(OperatingModeTests.Fixture fixture)
    {
        await using var database = await Open(fixture);
        await using var command = database.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM audit_entries;";
        return Assert.IsType<long>(await command.ExecuteScalarAsync());
    }

    private static async Task<SqliteConnection> Open(OperatingModeTests.Fixture fixture)
    {
        var database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fixture.Path, Pooling = false }.ToString());
        await database.OpenAsync();
        return database;
    }
}
