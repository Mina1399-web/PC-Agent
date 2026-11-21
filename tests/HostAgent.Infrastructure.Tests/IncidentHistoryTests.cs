using System.Text.Json;
using HostAgent.Core.Dashboard;
using HostAgent.Core.Models.Diagnosis;
using HostAgent.Core.Models.Incidents;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Storage;
using HostAgent.Service;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class IncidentHistoryTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddYears(50);
    private static string Id(int value) => value.ToString("x32");

    [Fact]
    public async Task V3_migration_uses_bounded_index_reads_and_keeps_creation_order_after_update()
    {
        var path = Path.Combine(Path.GetTempPath(), $"HostAgent-History-{Guid.NewGuid():N}.db");
        try
        {
            await using (var initial = new SqliteHostAgentStore(path))
            {
                for (var i = 1; i <= 23; i++) await initial.SaveIncidentBundleAsync(IncidentAt(i), default);
            }
            // Version four adds only this index and one migration marker. Remove
            // them from the disposable fixture to reproduce the exact v3 schema.
            await Execute(path, "DROP INDEX ix_incidents_history; DELETE FROM schema_migrations WHERE version=4;");
            Assert.Equal("3", await Scalar(path, "SELECT MAX(version) FROM schema_migrations;"));
            var unchanged = await Scalar(path, "SELECT payload_json FROM incidents WHERE incident_id='00000000000000000000000000000001';");

            await using (var store = new SqliteHostAgentStore(path))
            {
                Assert.Equal(4, await store.GetSchemaVersionAsync(default));
                var dispatcher = new DashboardDispatcher(store, new(), new(), TimeProvider.System);
                var client = new DashboardController(dispatcher.DispatchAsync);
                await client.RefreshAsync(default);
                var overview = Assert.IsType<DashboardSnapshot>(client.Snapshot);
                Assert.True(overview.SupportsIncidentHistory);
                Assert.All(overview.Incidents, row => Assert.Null(row.CreatedAtUtc));

                var first = Assert.IsType<ViewIncidentPage>(await client.ReadIncidentPageAsync(null, default));
                Assert.Equal(Enumerable.Range(4, 20).Reverse().Select(Id), first.Rows.Select(x => x.Id));
                Assert.Equal(new IncidentHistoryCursor(At.UtcTicks, Id(4)), first.Next);
                Assert.All(first.Rows, row => Assert.Equal(At, row.CreatedAtUtc));

                var reopened = IncidentAt(3) with
                {
                    CreatedAtUtc = At.AddMinutes(1), UpdatedAtUtc = At.AddHours(2),
                    State = IncidentLifecycleState.Active, OccurrenceStartedAtUtc = At.AddHours(1)
                };
                await store.SaveIncidentBundleAsync(reopened, default);
                await store.SaveIncidentBundleAsync(IncidentAt(24) with
                {
                    CreatedAtUtc = At.AddHours(1), UpdatedAtUtc = At.AddHours(1)
                }, default);
                var second = Assert.IsType<ViewIncidentPage>(await client.ReadIncidentPageAsync(first.Next, default));
                Assert.Equal(new[] {Id(3), Id(2), Id(1)}, second.Rows.Select(x => x.Id));
                var row = second.Rows[0];
                Assert.Equal(At, row.CreatedAtUtc);
                Assert.Equal(reopened.UpdatedAtUtc, row.UpdatedAtUtc);
                Assert.Equal("Active", row.State);
                Assert.Null(second.Next);
                var fresh = Assert.IsType<ViewIncidentPage>(await client.ReadIncidentPageAsync(null, default));
                Assert.Equal(Id(24), fresh.Rows[0].Id);
                Assert.NotNull(await client.ReadAsync<ViewIncidentDetail>("incident", new { incidentId = Id(3) }, default));

                Assert.Equal(unchanged, await Scalar(path, "SELECT payload_json FROM incidents WHERE incident_id='00000000000000000000000000000001';"));
                await AssertIndexPlan(path, SqliteHostAgentStore.IncidentHistoryFirstSql, before: false);
                await AssertIndexPlan(path, SqliteHostAgentStore.IncidentHistoryBeforeSql, before: true);
            }

            await Execute(path, "DROP INDEX ix_incidents_history; DELETE FROM schema_migrations WHERE version=4;");
            Assert.Equal("3", await Scalar(path, "SELECT MAX(version) FROM schema_migrations;"));
            Assert.Equal(unchanged, await Scalar(path, "SELECT payload_json FROM incidents WHERE incident_id='00000000000000000000000000000001';"));
            Assert.Equal("24", await Scalar(path, "SELECT COUNT(*) FROM incidents;"));
        }
        finally
        {
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task Protocol_rejects_invalid_cursors_and_client_distinguishes_empty_from_bad_pages()
    {
        Assert.Throws<InvalidDataException>(() => ViewProtocol.Request("incident-page", new { limit = 21, before = (object?)null }));
        Assert.Throws<InvalidDataException>(() => ViewProtocol.Request("incident-page",
            new { before = new { createdAtUtcTicks = 0, incidentId = Id(1) } }));
        Assert.Throws<InvalidDataException>(() => ViewProtocol.Request("incident-page",
            new { before = new { createdAtUtcTicks = At.UtcTicks, incidentId = "not-an-id" } }));
        var bytes = ViewProtocol.Request("incident-page", new { before = (object?)null });
        var oversized = bytes.Concat(Enumerable.Repeat((byte)' ', ViewProtocol.MaximumIncidentPageBytes)).ToArray();
        Assert.Throws<InvalidDataException>(() => ViewProtocol.Parse(oversized));

        var row = new ViewIncident(Id(1), "Active", "memory_pressure", 90, "Warning", At, 1)
            { CreatedAtUtc = At, UpdatedAtUtc = At };
        var page = new ViewIncidentPage([], null);
        var client = new DashboardController((_, _) => ValueTask.FromResult(
            HostAgent.Infrastructure.Administration.AdminProtocol.Reply(true, "incident-page", page)));
        Assert.Empty(Assert.IsType<ViewIncidentPage>(await client.ReadIncidentPageAsync(null, default)).Rows);
        page = new ViewIncidentPage([row, row], null);
        Assert.Null(await client.ReadIncidentPageAsync(null, default));
        page = new ViewIncidentPage([row], new IncidentHistoryCursor(At.UtcTicks, Id(1)));
        Assert.Null(await client.ReadIncidentPageAsync(null, default));
        page = new ViewIncidentPage([row], null);
        Assert.Null(await client.ReadIncidentPageAsync(new IncidentHistoryCursor(At.UtcTicks, Id(1)), default));
    }

    private static Incident IncidentAt(int number)
    {
        var key = $"history-{number}";
        var diagnosis = new Diagnosis(Id(number), key, At, DiagnosisSeverity.Warning, 90, [],
            new("memory_pressure", "private diagnosis", null), [], new("measure", "private suggestion"),
            IncidentLifecycleState.Active);
        return new Incident(Id(number), key, IncidentLifecycleState.Active, At, At, 1, diagnosis, []);
    }

    private static async Task AssertIndexPlan(string path, string sql, bool before)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        command.Parameters.AddWithValue("$limit", 21);
        if (before)
        {
            command.Parameters.AddWithValue("$created", At.ToString("O"));
            command.Parameters.AddWithValue("$id", Id(4));
        }
        await using var reader = await command.ExecuteReaderAsync();
        var plans = new List<string>();
        while (await reader.ReadAsync()) plans.Add(reader.GetString(3));
        Assert.Contains(plans, entry => entry.Contains("ix_incidents_history"));
        Assert.DoesNotContain(plans, entry => entry.Contains("TEMP B-TREE"));
        if (before) Assert.Contains(plans, entry => entry.Contains("SEARCH"));
    }

    private static async Task Execute(string path, string sql) => _ = await WithCommand(path, sql, cmd => cmd.ExecuteNonQueryAsync());
    private static Task<string?> Scalar(string path, string sql) =>
        WithCommand(path, sql, async cmd => (await cmd.ExecuteScalarAsync())?.ToString());

    private static async Task<T> WithCommand<T>(string path, string sql, Func<SqliteCommand, Task<T>> run)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await run(command);
    }
}
