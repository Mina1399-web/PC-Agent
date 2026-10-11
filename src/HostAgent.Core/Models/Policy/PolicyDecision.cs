namespace HostAgent.Core.Models.Policy;

public enum PolicyDecision
{
    READ_ONLY,
    AUTO_ALLOWED,
    CONFIRM_REQUIRED,
    DENIED
}

public enum ApprovalStatus
{
    NotRequired,
    Pending,
    Approved,
    Denied,
    Expired
}
