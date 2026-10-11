using System.Reflection;
using System.Text.Json;
using HostAgent.Core.Models.Policy;
using HostAgent.Core.Models.Remediation;
using HostAgent.Core.Policy;
using Xunit;

namespace HostAgent.Core.Tests;

public sealed class PolicyEngineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-11T12:00:00Z");
    private static PolicyContext Context() => new() { ReadOnly = false, Shadow = false,
        StorageHealthy = true, ConfigurationAvailable = true, OperationEnabled = true, Confidence = 95,
        IncidentId = "incident-1", DiagnosisId = "diagnosis-1", RequestId = Guid.NewGuid(),
        Operation = HelperOperation.RestartService, TargetKey = "svc:test", RequestDigest = new string('A', 64), NowUtc = Now };

    [Fact]
    public void Only_fresh_exact_approval_allows_execution_and_unsafe_contexts_deny()
    {
        var engine = new PolicyEngine();
        var context = Context();
        var approval = new OperationApproval(Guid.NewGuid(), context.RequestId, context.IncidentId,
            context.DiagnosisId, context.Operation, context.RequestDigest, "S-1-5-21-123-456-789-1001",
            Now.AddMinutes(-1), Now.AddMinutes(4));
        context = context with { Approval = approval };
        Assert.Equal(PolicyDecision.AUTO_ALLOWED, engine.Evaluate(context));
        foreach (var invalid in new[] {
            context with { Approval = approval with { IncidentId = "other" } },
            context with { Approval = approval with { DiagnosisId = "other" } },
            context with { Approval = approval with { RequestId = Guid.NewGuid() } },
            context with { Approval = approval with { Operation = HelperOperation.StopService } },
            context with { Approval = approval with { RequestDigest = new string('B', 64) } },
            context with { Approval = approval with { ExpiresAtUtc = Now } },
            context with { Approval = approval with { ApprovedAtUtc = Now.AddSeconds(1) } },
            context with { Approval = approval with { ApprovedAtUtc = Now.AddMinutes(-11) } },
            context with { Approval = approval with { ApproverSid = "" } },
            context with { StorageHealthy = false }, context with { ConfigurationAvailable = false },
            context with { Confidence = 79 }, context with { OperationEnabled = false },
            context with { Operation = (HelperOperation)999 }, context with { KillSwitch = true } })
            Assert.Equal(PolicyDecision.DENIED, engine.Evaluate(invalid));
        foreach (var operation in Enum.GetValues<HelperOperation>())
            Assert.Equal(PolicyDecision.READ_ONLY, engine.Evaluate(context with { ReadOnly = true, Operation = operation }));
        Assert.Equal(PolicyDecision.READ_ONLY, engine.Evaluate(context with { Shadow = true }));
        Assert.Equal(PolicyDecision.DENIED, engine.Evaluate(null));
    }
    // Missing or permissive policy must never authorize an unapproved change.
    [Theory]
    [InlineData(true, true, "READ_ONLY")]
    [InlineData(false, true, "DENIED")]
    [InlineData(false, false, "CONFIRM_REQUIRED")]
    public void Policy_defaults_and_kill_switch_fail_closed(bool readOnly, bool killSwitch, string expected)
    {
        var assembly = typeof(PolicyDecision).Assembly;
        var type = assembly.GetType("HostAgent.Core.Policy.PolicyEngine");
        Assert.NotNull(type);
        var contextType = assembly.GetType("HostAgent.Core.Models.Policy.PolicyContext");
        Assert.NotNull(contextType);
        var json = JsonSerializer.Serialize(new { ReadOnly = readOnly, KillSwitch = killSwitch,
            Shadow = false, StorageHealthy = true, ConfigurationAvailable = true,
            OperationEnabled = true, Confidence = 95, IncidentId = "incident-1", DiagnosisId = "diagnosis-1",
            RequestId = Guid.Parse("d12f46a7-b934-4aa1-813f-40bbff445a68"), Operation = 2,
            TargetKey = "svc:test", NowUtc = DateTimeOffset.Parse("2026-10-11T12:00:00Z") });
        var context = JsonSerializer.Deserialize(json, contextType);
        var engine = Activator.CreateInstance(type);
        Assert.Equal(expected, type.GetMethod("Evaluate")!.Invoke(engine, [context])!.ToString());
    }
}
