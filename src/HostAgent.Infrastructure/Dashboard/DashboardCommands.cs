using HostAgent.Core.Dashboard;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Security;
using System.Diagnostics;
using System.Globalization;
namespace HostAgent.Infrastructure.Dashboard;

public static class DashboardCommands
{
    public static string[] ProposalArguments(DashboardSnapshot snapshot,string incidentId,HelperOperation operation,string alias,int? pid=null,long? ticks=null)
    {
        if(snapshot.Mode!="Confirmation"||snapshot.ReadOnly||snapshot.KillSwitch||!ViewFreshness.IsFresh(snapshot.GeneratedAtUtc,DateTimeOffset.UtcNow))
            throw new UnauthorizedAccessException("desktop_operation_disabled");
        if(!Guid.TryParseExact(incidentId,"N",out var id)||id==Guid.Empty||!Enum.IsDefined(operation)
            ||alias.Length is <1 or >64||!alias.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_' or '.'))
            throw new InvalidDataException("desktop_operation_invalid");
        var args=new List<string>{"propose-and-review",id.ToString("N"),operation.ToString(),alias};
        if(operation==HelperOperation.TerminateProcess)
        {
            if(pid is not >0||ticks is not >0||ticks>DateTimeOffset.MaxValue.UtcTicks)throw new InvalidDataException("desktop_process_identity_required");
            args.Add(pid.Value.ToString(CultureInfo.InvariantCulture));args.Add(ticks.Value.ToString(CultureInfo.InvariantCulture));
        }
        else if(pid is not null||ticks is not null)throw new InvalidDataException("desktop_operation_invalid");
        return args.ToArray();
    }
    public static Process? Propose(DashboardSnapshot snapshot,string incidentId,HelperOperation operation,string alias,int? pid=null,long? ticks=null)
        =>LaunchCli(ProposalArguments(snapshot,incidentId,operation,alias,pid,ticks));
    public static Process? BlockRepairs()=>LaunchCli(["kill-switch","on"]);
    public static bool CanReviewServiceRecovery(DashboardSnapshot snapshot,ViewIncident incident,DateTimeOffset now)
        =>snapshot.Mode=="Confirmation"&&!snapshot.ReadOnly&&!snapshot.KillSwitch
            &&ViewFreshness.IsFresh(snapshot.GeneratedAtUtc,now)&&ViewFreshness.IsFresh(incident.AtUtc,now)
            &&incident.Code=="service_crash"&&incident.Confidence is >=80 and <=100
            &&incident.State is ("Detected" or "Active" or "Acknowledged")
            &&Guid.TryParseExact(incident.Id,"N",out var id)&&id!=Guid.Empty
            &&incident.AffectedService is {Length:>0 and <=128} service
            &&service.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_');

    public static Process? ReviewServiceRecovery(DashboardSnapshot snapshot,ViewIncident incident)
    {
        if(!CanReviewServiceRecovery(snapshot,incident,DateTimeOffset.UtcNow))
            throw new UnauthorizedAccessException("desktop_service_recovery_disabled");
        return LaunchCli(["review-service-crash",incident.Id,incident.AffectedService!]);
    }

    public static Process? ConfigureNetworkProbes(bool enabled)=>LaunchCli(["network-probes",enabled?"on":"off"]);
    private static Process? LaunchCli(string[] args)
    {
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"HostAgent");
        InstallationTreeSecurity.Validate(root,["S-1-5-18","S-1-5-32-544","S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"]);
        var path=Path.Combine(root,"Cli","HostAgent.Cli.exe");
        if(!File.Exists(path))throw new InvalidDataException("desktop_installed_cli_required");
        var start=new ProcessStartInfo(path){UseShellExecute=true,Verb="runas",WorkingDirectory=Path.GetDirectoryName(path)!};
        // All values are already restricted to ASCII non-whitespace tokens, no shell is started.
        start.Arguments=string.Join(" ",args);
        return Process.Start(start);
    }
    public static void OpenSettings(GuidedAction action)
    {
        var uri=action switch{GuidedAction.StorageSettings=>"ms-settings:storagesense",GuidedAction.AppsSettings=>"ms-settings:appsfeatures",
            GuidedAction.StartupSettings=>"ms-settings:startupapps",_=>throw new InvalidDataException("desktop_settings_invalid")};
        using var process=Process.Start(new ProcessStartInfo(uri){UseShellExecute=true});
    }
}
