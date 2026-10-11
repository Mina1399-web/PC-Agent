using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Storage;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Core.Contracts;
using HostAgent.Core.Policy;
using HostAgent.Core.Models.Policy;
using HostAgent.Core.Models.Audit;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class RemediationGuardTests
{
    [Fact]
    public async Task Synchronously_blocked_executor_does_not_block_protocol_deadline()
    {
        await using var fixture = new Task3Fixture();
        using var release = new ManualResetEventSlim(); fixture.Executor.BlockSynchronously = release;
        var request = await fixture.Approve(Task3Fixture.Request());
        var executing = Task.Run(() => fixture.Dispatcher(settings: new(false, false, ExecutionTimeoutSeconds: 1))
            .ExecuteAsync(request, Task3Fixture.CallerSid, default).AsTask());
        try
        {
            var completed = await Task.WhenAny(executing, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(executing, completed);
            Assert.Equal(HelperResult.Indeterminate, (await executing).Result);
        }
        finally { release.Set(); await executing; }
    }
    [Fact]
    public async Task Crash_after_reservation_blocks_new_mutations_of_target_after_reopen()
    {
        await using var fixture = new Task3Fixture(); var abandoned = await fixture.Approve(Task3Fixture.Request());
        var approval = await fixture.Journal.ReadApprovalAsync(abandoned.ApprovalId!.Value, default);
        Assert.Null(await fixture.Journal.ReserveAsync(abandoned, approval!, new(), fixture.Clock.Now, default));
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(11);
        var next = await fixture.Approve(Task3Fixture.Request() with { Operation = HelperOperation.StartService, IncidentId = "incident-2" });
        await using var reopened = new SqliteHelperJournal(fixture.Path, [Task3Fixture.ApproverSid]);
        var response = await fixture.Dispatcher(journal: reopened).ExecuteAsync(next, Task3Fixture.CallerSid, default);
        Assert.Equal(HelperResult.Denied, response.Result); Assert.Equal("unresolved_attempt", response.Code);
        Assert.Equal(0, fixture.Executor.Calls);
    }
    [Fact]
    public async Task Execution_timeout_is_indeterminate_audited_and_never_retried()
    {
        await using var fixture = new Task3Fixture(); fixture.Executor.Block = true;
        var request = await fixture.Approve(Task3Fixture.Request());
        var response = await fixture.Dispatcher(settings: new(false, false, ExecutionTimeoutSeconds: 1))
            .ExecuteAsync(request, Task3Fixture.CallerSid, default);
        Assert.Equal(HelperResult.Indeterminate, response.Result); Assert.Equal("execution_interrupted", response.Code);
        var audit = await fixture.Journal.ReadAuditAsync(request.RequestId, default);
        Assert.Equal(AuditEvent.ExecutionEnded, audit[^1].Event);
        Assert.Equal("execution_interrupted", audit[^1].Code);
        Assert.Equal("replayed_request", (await fixture.Dispatcher().ExecuteAsync(request, Task3Fixture.CallerSid, default)).Code);
        Assert.Equal(1, fixture.Executor.Calls);
    }
    [Fact]
    public void Guard_enforces_attempt_rate_cooldown_and_restart_backoff_boundaries()
    {
        var guard = new RemediationGuard(); var limits = new GuardLimits();
        var now = DateTimeOffset.Parse("2026-10-11T12:00:00Z");
        Assert.Null(guard.Denial(new(0, 0, 0, null), limits, false, now));
        Assert.Equal("attempt_limit", guard.Denial(new(3, 0, 0, null), limits, false, now));
        Assert.Equal("rate_limit", guard.Denial(new(0, 5, 0, null), limits, false, now));
        Assert.Equal("cooldown", guard.Denial(new(1, 1, 0, now.AddSeconds(-59)), limits, false, now));
        Assert.Null(guard.Denial(new(1, 1, 0, now.AddSeconds(-60)), limits, false, now));
        Assert.Equal("restart_loop", guard.Denial(new(0, 2, 3, null), limits, true, now));
        Assert.Equal("restart_backoff", guard.Denial(new(0, 1, 1, now.AddMinutes(-4)), limits, true, now));
        Assert.Null(guard.Denial(new(0, 1, 1, now.AddMinutes(-5)), limits, true, now));
        Assert.Equal("restart_backoff", guard.Denial(new(0, 2, 2, now.AddMinutes(-9)), limits, true, now));
        Assert.Equal("clock_regression", guard.Denial(new(0, 1, 0, now.AddSeconds(1)), limits, false, now));
    }

    [Fact]
    public void Helper_allowlist_binds_local_targets_and_individual_operations()
    {
        var allowlist = new HelperAllowlist([new("test-service", "FixtureService", [HelperOperation.RestartService])], []);
        var request = Task3Fixture.Request();
        Assert.Equal("FixtureService", allowlist.Resolve(request)!.ServiceName);
        Assert.Null(allowlist.Resolve(request with { Operation = HelperOperation.StopService }));
        Assert.Null(allowlist.Resolve(request with { Parameters = new ServiceParameters("Spooler") }));
        Assert.Null(allowlist.Resolve(request with { Operation = HelperOperation.TerminateProcess,
            Parameters = new ProcessParameters("FixtureService", 123, 123) }));
    }

    [Fact]
    public async Task Exact_approval_executes_once_audits_all_stages_and_replay_survives_reopen()
    {
        await using var fixture = new Task3Fixture();
        var request = await fixture.Approve(Task3Fixture.Request());
        var response = await fixture.Dispatcher().ExecuteAsync(request, Task3Fixture.CallerSid, default);
        Assert.Equal(HelperResult.Succeeded, response.Result); Assert.Equal(1, fixture.Executor.Calls);
        var audit = await fixture.Journal.ReadAuditAsync(request.RequestId, default);
        Assert.Equal(new[] { AuditEvent.Approval, AuditEvent.Proposal, AuditEvent.Decision,
            AuditEvent.ExecutionStarted, AuditEvent.ExecutionEnded }, audit.Select(a => a.Event));
        Assert.Equal("succeeded", audit[^1].Code);
        Assert.Equal(request.IncidentId, audit[^1].IncidentId);
        Assert.Equal(request.DiagnosisId, audit[^1].DiagnosisId);
        Assert.Equal(Task3Fixture.ApproverSid, audit[^1].ApproverSid);
        Assert.Equal(HelperProtocol.Digest(request), audit[^1].RequestDigest);
        var parameterProperty = typeof(AuditRecord).GetProperty("ParametersJson");
        Assert.NotNull(parameterProperty);
        Assert.Equal("{\"serviceId\":\"test-service\"}", parameterProperty.GetValue(audit[^1]));
        Assert.Equal(15, typeof(AuditRecord).GetProperty("TimeoutSeconds")!.GetValue(audit[^1]));
        await using var reopened = new SqliteHelperJournal(fixture.Path, [Task3Fixture.ApproverSid]);
        var replay = await fixture.Dispatcher(journal: reopened).ExecuteAsync(request, Task3Fixture.CallerSid, default);
        Assert.Equal(HelperResult.Denied, replay.Result); Assert.Equal("replayed_request", replay.Code);
        Assert.Equal(1, fixture.Executor.Calls);
        await using var connection = new SqliteConnection("Pooling=False;Data Source=" + fixture.Path);
        await connection.OpenAsync();
        foreach (var table in new[] { "helper_audit", "helper_approvals", "helper_claims" })
        foreach (var sql in new[] { $"DELETE FROM {table}", $"UPDATE {table} SET request_id = request_id" })
        { await using var command = connection.CreateCommand(); command.CommandText = sql;
            await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync()); }
    }

    [Fact]
    public async Task Missing_forged_stale_or_mismatched_approval_and_policy_errors_never_execute()
    {
        await using var fixture = new Task3Fixture(); var request = Task3Fixture.Request();
        Assert.Equal(HelperResult.Denied, (await fixture.Dispatcher().ExecuteAsync(request, Task3Fixture.CallerSid, default)).Result);
        Assert.Equal(HelperResult.Denied, (await fixture.Dispatcher().ExecuteAsync(request with { ApprovalId = Guid.NewGuid() }, Task3Fixture.CallerSid, default)).Result);
        var approved = await fixture.Approve(request);
        Assert.Equal(HelperResult.Denied, (await fixture.Dispatcher().ExecuteAsync(approved with { Confidence = 96 }, Task3Fixture.CallerSid, default)).Result);
        Assert.Equal(HelperResult.Denied, (await fixture.Dispatcher(settings: new(ReadOnly: true, Shadow: false)).ExecuteAsync(approved, Task3Fixture.CallerSid, default)).Result);
        Assert.Equal(HelperResult.Denied, (await fixture.Dispatcher(settings: new(ReadOnly: false, Shadow: true)).ExecuteAsync(approved, Task3Fixture.CallerSid, default)).Result);
        Assert.Equal(HelperResult.Denied, (await fixture.Dispatcher(settings: new(false, false, true)).ExecuteAsync(approved, Task3Fixture.CallerSid, default)).Result);
        Assert.Equal(HelperResult.Denied, (await fixture.Dispatcher(policy: new ThrowingPolicy()).ExecuteAsync(approved, Task3Fixture.CallerSid, default)).Result);
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(11);
        Assert.Equal(HelperResult.Denied, (await fixture.Dispatcher().ExecuteAsync(approved, Task3Fixture.CallerSid, default)).Result);
        Assert.Equal(0, fixture.Executor.Calls);
    }

    [Fact]
    public async Task Failed_attempts_are_durable_and_stop_after_limit_with_no_automatic_retry()
    {
        await using var fixture = new Task3Fixture(); fixture.Executor.Fail = true;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var request = await fixture.Approve(Task3Fixture.Request() with { Operation = HelperOperation.StartService });
            var response = await fixture.Dispatcher().ExecuteAsync(request, Task3Fixture.CallerSid, default);
            Assert.Equal(attempt < 3 ? HelperResult.Failed : HelperResult.Denied, response.Result);
            if (attempt == 3)
            {
                Assert.Equal("attempt_limit", response.Code);
                var audit = await fixture.Journal.ReadAuditAsync(request.RequestId, default);
                Assert.Equal("attempt_limit", audit[^1].Code);
                Assert.Equal(PolicyDecision.DENIED, audit[^1].Decision);
            }
            fixture.Clock.Now = fixture.Clock.Now.AddMinutes(2);
        }
        Assert.Equal(3, fixture.Executor.Calls);
    }

    [Fact]
    public async Task Concurrent_identical_requests_cannot_execute_twice()
    {
        await using var fixture = new Task3Fixture(); var request = await fixture.Approve(Task3Fixture.Request());
        var results = await Task.WhenAll(fixture.Dispatcher().ExecuteAsync(request, Task3Fixture.CallerSid, default).AsTask(),
            fixture.Dispatcher().ExecuteAsync(request, Task3Fixture.CallerSid, default).AsTask());
        Assert.Single(results, r => r.Result == HelperResult.Succeeded);
        Assert.Equal(1, fixture.Executor.Calls);
    }

    [Fact]
    public async Task Audit_failure_prevents_executor_and_untrusted_approval_cannot_be_recorded()
    {
        await using var fixture = new Task3Fixture();
        var request = await fixture.Approve(Task3Fixture.Request());
        await using var connection = new SqliteConnection("Pooling=False;Data Source=" + fixture.Path); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER fail_audit BEFORE INSERT ON helper_audit BEGIN SELECT RAISE(ABORT, 'fixture'); END;";
        await command.ExecuteNonQueryAsync();
        Assert.Equal(HelperResult.Denied, (await fixture.Dispatcher().ExecuteAsync(request, Task3Fixture.CallerSid, default)).Result);
        Assert.Equal(0, fixture.Executor.Calls);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Journal.RecordApprovalAsync(
            new(Guid.NewGuid(), request.RequestId, request.IncidentId, request.DiagnosisId, request.Operation,
                HelperProtocol.Digest(request), Task3Fixture.CallerSid, fixture.Clock.Now, fixture.Clock.Now.AddMinutes(5)), default).AsTask());
    }

    private sealed class ThrowingPolicy : IPolicyEngine
    { public PolicyDecision Evaluate(PolicyContext? context) => throw new InvalidOperationException("sensitive fixture text"); }
    [Fact]
    public void Helper_local_guard_and_persistent_journal_are_required_before_execution()
    {
        var assembly = typeof(SqliteHostAgentStore).Assembly;
        Assert.NotNull(assembly.GetType("HostAgent.Infrastructure.Security.RemediationGuard"));
        Assert.NotNull(assembly.GetType("HostAgent.Infrastructure.Storage.SqliteHelperJournal"));
        Assert.NotNull(assembly.GetType("HostAgent.Infrastructure.Security.HelperAllowlist"));
        Assert.NotNull(assembly.GetType("HostAgent.Infrastructure.Security.HelperDispatcher"));
    }
}

internal sealed class Task3Fixture : IAsyncDisposable
{
    public const string CallerSid = "S-1-5-21-123-456-789-1001";
    public const string ApproverSid = "S-1-5-21-123-456-789-1002";
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HostAgent-task3-" + Guid.NewGuid() + ".db");
    public SqliteHelperJournal Journal { get; }
    public FakeExecutor Executor { get; } = new();
    public FixtureClock Clock { get; } = new();
    public Task3Fixture() => Journal = new(Path, [ApproverSid]);
    public static HelperOperationRequest Request() => HelperProtocol.ParseRequest(System.Text.Encoding.UTF8.GetBytes(HelperProtocolTests.ValidJson)) with { RequestId = Guid.NewGuid() };
    public HelperDispatcher Dispatcher(HelperSettings? settings = null, IPolicyEngine? policy = null, SqliteHelperJournal? journal = null) =>
        new(journal ?? Journal, new HelperAllowlist([new("test-service", "FixtureService", [HelperOperation.StartService, HelperOperation.RestartService])], []),
            Executor, policy ?? new PolicyEngine(), () => settings ?? new(false, false), timeProvider: Clock);
    public async Task<HelperOperationRequest> Approve(HelperOperationRequest request)
    {
        var approval = new OperationApproval(Guid.NewGuid(), request.RequestId, request.IncidentId, request.DiagnosisId,
            request.Operation, HelperProtocol.Digest(request), ApproverSid, Clock.Now, Clock.Now.AddMinutes(5));
        await Journal.RecordApprovalAsync(approval, default);
        return request with { ApprovalId = approval.ApprovalId };
    }
    public async ValueTask DisposeAsync()
    {
        await Journal.DisposeAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(Path + suffix)) File.Delete(Path + suffix);
    }
    internal sealed class FakeExecutor : IOperationExecutor
    {
        public int Calls; public bool Fail; public bool Block; public ManualResetEventSlim? BlockSynchronously;
        public async ValueTask ExecuteAsync(HelperOperationRequest request, OperationBinding binding, CancellationToken cancellationToken)
        { Interlocked.Increment(ref Calls); BlockSynchronously?.Wait(); if (Fail) throw new InvalidOperationException("sensitive fixture text");
            if (Block) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
    }
    internal sealed class FixtureClock : TimeProvider
    { public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-11T12:00:00Z"); public override DateTimeOffset GetUtcNow() => Now; }
}
