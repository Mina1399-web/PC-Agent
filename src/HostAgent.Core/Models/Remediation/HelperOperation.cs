namespace HostAgent.Core.Models.Remediation;

// A closed set: adding a capability requires a protocol and security review.
public enum HelperOperation { StartService, StopService, RestartService, TerminateProcess }
public abstract record OperationParameters;
public sealed record ServiceParameters(string ServiceId) : OperationParameters;
public sealed record ProcessParameters(string ProcessId, int Pid, long StartTimeUtcTicks) : OperationParameters;
public sealed record HelperOperationRequest(int Version, Guid RequestId, string IncidentId,
    string DiagnosisId, int Confidence, string[] EvidenceReferences, HelperOperation Operation,
    OperationParameters Parameters, Guid? ApprovalId);
public enum HelperResult { Succeeded, Denied, Unavailable, TimedOut, Failed, Indeterminate }
public sealed record HelperOperationResponse(int Version, Guid RequestId, HelperResult Result, string Code);

public sealed record OperationApproval(Guid ApprovalId, Guid RequestId, string IncidentId,
    string DiagnosisId, HelperOperation Operation, string RequestDigest, string ApproverSid,
    DateTimeOffset ApprovedAtUtc, DateTimeOffset ExpiresAtUtc);
