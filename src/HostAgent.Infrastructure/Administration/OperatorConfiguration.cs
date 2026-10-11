using System.Security.Principal;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HostAgent.Core.Models.Remediation;
using HostAgent.Infrastructure.Security;
namespace HostAgent.Infrastructure.Administration;
public static class OperatorConfiguration
{
    public static void Apply(string programData,SecurityIdentifier monitor,SecurityIdentifier system,string approverSid,string[] args)
    {
        _=new SecurityIdentifier(approverSid);
        var servicePath=Path.Combine(HostAgentDataDirectory.EnsureSecure(Path.Combine(programData,"HostAgent"),monitor),"service.json");
        var helperPath=Path.Combine(HostAgentDataDirectory.EnsureSecure(Path.Combine(programData,"HostAgent.Helper"),system),"helper.json");
        HelperProtectedFiles.ValidateExisting(servicePath,monitor);HelperProtectedFiles.ValidateExisting(helperPath,system);
        var service=ServiceConfiguration.LoadSafe(servicePath,monitor);var helper=HelperConfiguration.LoadSafe(helperPath);
        if(!service.ConfigurationAvailable||!helper.ConfigurationAvailable)throw new InvalidDataException("configuration_unavailable");
        switch(args)
        {
            case ["configure",var mode,var pushover,var heartbeat] when mode is "Shadow" or "Confirmation" && pushover is "on" or "off" && heartbeat is "on" or "off":
                var shadow=mode=="Shadow";
                service=service with{Mode=shadow?OperatingMode.Shadow:OperatingMode.Confirmation,ReadOnly=shadow,EnablePushover=pushover=="on",EnableHeartbeat=heartbeat=="on"};
                helper=helper with{Shadow=shadow,ReadOnly=shadow};break;
            case ["allow-service",var alias,var name,var operations]:
                var enabled=new List<HelperOperation>();
                foreach(var operation in operations.Split(','))
                {
                    if(!Enum.TryParse<HelperOperation>(operation,false,out var parsed)||!Enum.IsDefined(parsed)||parsed==HelperOperation.TerminateProcess||parsed.ToString()!=operation)
                        throw new InvalidDataException("configuration_invalid");
                    enabled.Add(parsed);
                }
                helper=helper with{Services=[..helper.Services.Where(x=>x.Id!=alias),new(alias,name,enabled.Distinct().ToArray())]};break;
            case ["allow-process",var alias,var executable]:
                if(!Path.IsPathFullyQualified(executable)||executable.StartsWith(@"\\",StringComparison.Ordinal))throw new InvalidDataException("configuration_invalid");
                using(var file=File.OpenRead(executable))
                    helper=helper with{Processes=[..helper.Processes.Where(x=>x.Id!=alias),new(alias,Path.GetFullPath(executable),Convert.ToHexString(SHA256.HashData(file)),true)]};
                break;
            case ["remove-service",var alias]:helper=helper with{Services=helper.Services.Where(x=>x.Id!=alias).ToArray()};break;
            case ["remove-process",var alias]:helper=helper with{Processes=helper.Processes.Where(x=>x.Id!=alias).ToArray()};break;
            default:throw new InvalidDataException("configuration_invalid");
        }
        helper=helper with{ApproverSids=helper.ApproverSids.Append(approverSid).Distinct(StringComparer.Ordinal).ToArray()};
        var options=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
        options.Converters.Add(new JsonStringEnumConverter<HelperOperation>(allowIntegerValues:false));
        var helperBytes=JsonSerializer.SerializeToUtf8Bytes(new{helper.ReadOnly,helper.Shadow,helper.KillSwitch,helper.ExecutionTimeoutSeconds,helper.Services,helper.Processes,helper.ApproverSids},options);
        var serviceBytes=JsonSerializer.SerializeToUtf8Bytes(new{mode=service.Mode.ToString(),service.ReadOnly,service.KillSwitch,service.CollectionIntervalSeconds,service.HeartbeatIntervalSeconds,service.RequestTimeoutSeconds,service.EnablePushover,service.EnableHeartbeat},options);
        _=HelperConfiguration.Parse(helperBytes);_=ServiceConfiguration.Parse(serviceBytes);
        // Enabling the helper first cannot authorize a mutation: the monitor remains Shadow until its own atomic replacement.
        // Disabling the helper first blocks mutation before the monitor is changed. Approvals remain mandatory in both cases.
        Replace(helperPath,helperBytes);Replace(servicePath,serviceBytes);
    }
    private static void Replace(string path,byte[] bytes)
    {
        var suffix=Guid.NewGuid().ToString("N");var temporary=path+"."+suffix+".tmp";
        try
        {
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(true);}
            File.Replace(temporary,path,path+"."+suffix+".previous");
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
