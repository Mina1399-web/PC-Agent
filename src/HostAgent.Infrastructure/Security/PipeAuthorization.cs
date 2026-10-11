using System.IO.Pipes;
using System.Security.Principal;
using System.Security.AccessControl;
namespace HostAgent.Infrastructure.Security;
public sealed class PipeAuthorization
{
    private readonly SecurityIdentifier _serviceSid;
    private readonly SecurityIdentifier _helperSid;
    public PipeAuthorization(SecurityIdentifier serviceSid, SecurityIdentifier helperSid)
    { _serviceSid = serviceSid; _helperSid = helperSid; }
    public bool IsAuthorized(string? authenticatedSid) => string.Equals(authenticatedSid, _serviceSid.Value, StringComparison.Ordinal);
    public PipeSecurity CreateSecurity()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(_helperSid);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(_helperSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        if (!_serviceSid.Equals(_helperSid)) security.AddAccessRule(new PipeAccessRule(_serviceSid,
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return security;
    }
    public string ReadAuthenticatedSid(NamedPipeServerStream stream)
    {
        string? sid = null;
        // No await or privileged work occurs while impersonating the client.
        stream.RunAsClient(() => { using var identity = WindowsIdentity.GetCurrent(true); sid = identity?.User?.Value; });
        if (!IsAuthorized(sid)) throw new UnauthorizedAccessException("unauthorized_client");
        return sid!;
    }
}
