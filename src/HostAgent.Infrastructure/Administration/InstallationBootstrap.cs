using System.Security.Principal;
using System.Text;
using HostAgent.Infrastructure.Security;
namespace HostAgent.Infrastructure.Administration;
public static class InstallationBootstrap
{
    public static void Provision(string programData, SecurityIdentifier monitorSid, SecurityIdentifier systemSid)
    {
        var parent=new DirectoryInfo(Path.GetFullPath(programData));
        for(var cursor=parent;cursor is not null;cursor=cursor.Parent)
            if((cursor.Attributes&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("installation_reparse_point");
        var monitor=HostAgentDataDirectory.EnsureSecure(Path.Combine(parent.FullName,"HostAgent"),monitorSid);
        var helper=HostAgentDataDirectory.EnsureSecure(Path.Combine(parent.FullName,"HostAgent.Helper"),systemSid);
        HostAgentDataDirectory.EnsureSecure(Path.Combine(parent.FullName,"HostAgent.Operator"),systemSid);
        CreateOnce(Path.Combine(monitor,"service.json"),monitorSid,
            """{"mode":"Shadow","readOnly":true,"killSwitch":false,"collectionIntervalSeconds":60,"heartbeatIntervalSeconds":60,"requestTimeoutSeconds":10,"enablePushover":false,"enableHeartbeat":false}""");
        CreateOnce(Path.Combine(helper,"helper.json"),systemSid,
            """{"readOnly":true,"shadow":true,"killSwitch":false,"executionTimeoutSeconds":15,"services":[],"processes":[],"approverSids":[]}""");
    }
    private static void CreateOnce(string path,SecurityIdentifier sid,string json)
    {
        HelperProtectedFiles.ValidateExisting(path,sid);
        if(File.Exists(path))return;
        using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        stream.Write(Encoding.UTF8.GetBytes(json));stream.Flush(flushToDisk:true);
    }
}
