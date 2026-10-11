using HostAgent.Core.Models.Remediation;

namespace HostAgent.Core.Models.Policy;

public sealed record PolicyContext
{
    public bool ReadOnly { get; init; } = true;
    public bool Shadow { get; init; } = true;
    public bool KillSwitch { get; init; }
    public bool StorageHealthy { get; init; }
    public bool ConfigurationAvailable { get; init; }
    public bool OperationEnabled { get; init; }
    public int Confidence { get; init; }
    public string IncidentId { get; init; } = "";
    public string DiagnosisId { get; init; } = "";
    public Guid RequestId { get; init; }
    public HelperOperation Operation { get; init; }
    public string TargetKey { get; init; } = "";
    public string RequestDigest { get; init; } = "";
    public DateTimeOffset NowUtc { get; init; }
    public OperationApproval? Approval { get; init; }
}
