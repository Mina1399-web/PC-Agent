using HostAgent.Core.Diagnostics;
using HostAgent.Core.Models.Diagnosis;
using HostAgent.Core.Models.Incidents;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Infrastructure.Security;
using HostAgent.Service;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class ConfirmationFreshnessTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-21T13:00:00Z");

    // A recent matching approval must not authorize a diagnosis outside the
    // configured sampling allowance. Exact boundary is inclusive; +1 tick fails.
    [Theory]
    [InlineData(5, 180, 0, true)]
    [InlineData(5, 180, 1, false)]
    [InlineData(60, 180, 0, true)]
    [InlineData(60, 180, 1, false)]
    [InlineData(3600, 7200, 0, true)]
    [InlineData(3600, 7200, 1, false)]
    public async Task Confirmation_uses_validated_collection_cadence_and_exact_diagnosis_age_boundary(
        int intervalSeconds, int allowedAgeSeconds, int ticksBeyond, bool allowed)
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        var request = await AddIncident(fixture, Now.AddSeconds(-allowedAgeSeconds).AddTicks(-ticksBeyond));
        fixture.Helper.Response = HelperResult.Succeeded;
        using var coordinator = new RemediationCoordinator(fixture.Store, fixture.Helper,
            Confirmation with { CollectionIntervalSeconds = intervalSeconds }, new FixedClock());

        var result = await coordinator.ProposeAsync(request, Approval(request), default);

        Assert.Equal(allowed ? HelperResult.Succeeded : HelperResult.Denied, result.Result);
        Assert.Equal(allowed ? "succeeded" : "incident_evidence_stale", result.Code);
        Assert.Equal(allowed ? 1 : 0, fixture.Helper.Calls);
        var saved = Assert.IsType<Incident>(await fixture.Store.ReadIncidentAsync(request.IncidentId, default));
        Assert.Equal(allowed ? IncidentLifecycleState.Resolved : IncidentLifecycleState.Active, saved.State);
        if (allowed) AssertExactRequest(request, fixture.Helper.Last!);
    }

    // A later lifecycle/snapshot timestamp cannot refresh the underlying old
    // diagnosis that the operator is being asked to approve.
    [Fact]
    public async Task Fresh_snapshot_and_fresh_approval_cannot_revive_three_day_old_diagnosis()
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        var request = await AddIncident(fixture, Now.AddDays(-3), capturedAt: Now);
        fixture.Helper.Response = HelperResult.Succeeded;
        using var coordinator = new RemediationCoordinator(fixture.Store, fixture.Helper, Confirmation, new FixedClock());

        var result = await coordinator.ProposeAsync(request, Approval(request), default);

        Assert.Equal(HelperResult.Denied, result.Result);
        Assert.Equal("incident_evidence_stale", result.Code);
        Assert.Equal(0, fixture.Helper.Calls);
        var saved = Assert.IsType<Incident>(await fixture.Store.ReadIncidentAsync(request.IncidentId, default));
        Assert.Equal(IncidentLifecycleState.Active, saved.State);
        Assert.Equal(Now.AddDays(-3), saved.LatestDiagnosis.ObservedAtUtc);
        Assert.Single(saved.Snapshots);
    }

    // Future diagnostic evidence is not a current actionable state, even when
    // its timestamp exceeds the trusted service clock by only one tick.
    [Fact]
    public async Task Future_diagnosis_is_denied_before_helper_without_lifecycle_transition()
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        var request = await AddIncident(fixture, Now.AddTicks(1));
        fixture.Helper.Response = HelperResult.Succeeded;
        using var coordinator = new RemediationCoordinator(fixture.Store, fixture.Helper, Confirmation, new FixedClock());

        var result = await coordinator.ProposeAsync(request, Approval(request), default);

        Assert.Equal(HelperResult.Denied, result.Result);
        Assert.Equal("incident_evidence_stale", result.Code);
        Assert.Equal(0, fixture.Helper.Calls);
        Assert.Equal(IncidentLifecycleState.Active,
            Assert.IsType<Incident>(await fixture.Store.ReadIncidentAsync(request.IncidentId, default)).State);
    }

    // Invalid in-memory configuration must not create an effectively unbounded
    // allowance or enable an operation just because a matching approval exists.
    [Theory]
    [InlineData(4)]
    [InlineData(3601)]
    public async Task Invalid_collection_interval_denies_confirmation_without_helper_execution(int intervalSeconds)
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        var request = await AddIncident(fixture, Now);
        fixture.Helper.Response = HelperResult.Succeeded;
        using var coordinator = new RemediationCoordinator(fixture.Store, fixture.Helper,
            Confirmation with { CollectionIntervalSeconds = intervalSeconds }, new FixedClock());

        var result = await coordinator.ProposeAsync(request, Approval(request), default);

        Assert.Equal(HelperResult.Denied, result.Result);
        Assert.Equal(0, fixture.Helper.Calls);
        Assert.Equal(IncidentLifecycleState.Active,
            Assert.IsType<Incident>(await fixture.Store.ReadIncidentAsync(request.IncidentId, default)).State);
    }

    // Shadow/read-only historical investigation remains available and audited;
    // it can never contact the helper regardless of evidence age.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Historical_shadow_and_readonly_proposals_remain_auditable(bool shadow)
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        var request = await AddIncident(fixture, Now.AddDays(-3));
        using var coordinator = new RemediationCoordinator(fixture.Store, fixture.Helper,
            new() { Mode = shadow ? OperatingMode.Shadow : OperatingMode.Confirmation, ReadOnly = true,
                ConfigurationAvailable = true }, new FixedClock());

        var result = await coordinator.ProposeAsync(request, null, default);

        Assert.Equal(HelperResult.Denied, result.Result);
        Assert.Equal(shadow ? "shadow_proposal" : "read_only", result.Code);
        Assert.Equal(0, fixture.Helper.Calls);
        Assert.Equal(IncidentLifecycleState.Active,
            Assert.IsType<Incident>(await fixture.Store.ReadIncidentAsync(request.IncidentId, default)).State);
    }

    // A current diagnosis can legitimately cite a sustained CPU window or a
    // recent historical SCM event; do not apply the diagnosis age to every item.
    [Theory]
    [InlineData(5)]
    [InlineData(1380)]
    public async Task Current_diagnosis_can_preserve_older_correlated_evidence(int evidenceAgeMinutes)
    {
        await using var fixture = await OperatingModeTests.Fixture.Create();
        var request = await AddIncident(fixture, Now, evidenceAt: Now.AddMinutes(-evidenceAgeMinutes));
        fixture.Helper.Response = HelperResult.Succeeded;
        using var coordinator = new RemediationCoordinator(fixture.Store, fixture.Helper, Confirmation, new FixedClock());

        var result = await coordinator.ProposeAsync(request, Approval(request), default);

        Assert.Equal(HelperResult.Succeeded, result.Result);
        Assert.Equal(1, fixture.Helper.Calls);
        AssertExactRequest(request, fixture.Helper.Last!);
    }

    private static ServiceConfiguration Confirmation =>
        new() { Mode = OperatingMode.Confirmation, ReadOnly = false, ConfigurationAvailable = true };

    private static OperationApproval Approval(HelperOperationRequest request) => new(Guid.NewGuid(), request.RequestId,
        request.IncidentId, request.DiagnosisId, request.Operation, HelperProtocol.Digest(request),
        "S-1-5-21-100-100-100-1001", Now.AddSeconds(-1), Now.AddMinutes(1));

    private static async Task<HelperOperationRequest> AddIncident(OperatingModeTests.Fixture fixture,
        DateTimeOffset observedAt, DateTimeOffset? capturedAt = null, DateTimeOffset? evidenceAt = null)
    {
        var diagnosis = new Diagnosis("freshness-diag", "freshness-service", observedAt, DiagnosisSeverity.High, 90,
            [new("freshness-evidence", "fixture", "Synthetic bounded evidence", null, evidenceAt ?? observedAt)],
            new("service_crash", "Synthetic", "svc"), [], new("inspect", "Synthetic"), IncidentLifecycleState.Detected);
        var incident = await new IncidentEngine().ApplyAsync(diagnosis,
            new("freshness-snapshot", "", capturedAt ?? observedAt, null, null, null, null, null, null, [], diagnosis.Evidence));
        incident = incident with { State = IncidentLifecycleState.Active,
            LatestDiagnosis = incident.LatestDiagnosis with { IncidentState = IncidentLifecycleState.Active } };
        await fixture.Store.SaveIncidentBundleAsync(incident, default);
        return new(1, Guid.NewGuid(), incident.IncidentId, "freshness-diag", 90, ["freshness-evidence"],
            HelperOperation.RestartService, new ServiceParameters("svc"), null);
    }

    private static void AssertExactRequest(HelperOperationRequest expected, HelperOperationRequest actual)
    {
        Assert.Equal(expected.RequestId, actual.RequestId);
        Assert.Equal(expected.IncidentId, actual.IncidentId);
        Assert.Equal("freshness-diag", actual.DiagnosisId);
        Assert.Equal(90, actual.Confidence);
        Assert.Equal(new[] { "freshness-evidence" }, actual.EvidenceReferences);
        Assert.Equal(HelperOperation.RestartService, actual.Operation);
        Assert.Equal("svc", Assert.IsType<ServiceParameters>(actual.Parameters).ServiceId);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
