using HostAgent.Core.Dashboard;
using System.Text.Json;
using HostAgent.Infrastructure.Pipes;
namespace HostAgent.Infrastructure.Dashboard;

public sealed class DashboardController(Func<byte[],CancellationToken,ValueTask<byte[]>> send)
{
    private long _processLoadRequestVersion;
    public ViewProcessLoad? ProcessLoad {get;private set;}
    public DashboardSnapshot? Snapshot {get;private set;}
    public string State {get;private set;}="disconnected";
    public async ValueTask RefreshAsync(CancellationToken token)
    {
        _processLoadRequestVersion++;
        Snapshot=null;State="disconnected";
        var value=await ReadAsync<DashboardSnapshot>("overview",new{},token);
        if(value is null){ProcessLoad=null;return;}
        if(!ViewFreshness.IsFresh(value.GeneratedAtUtc,DateTimeOffset.UtcNow)){ProcessLoad=null;State="stale";return;}
        if(value.Mode is not ("Shadow" or "Confirmation") || value.Disks is null || value.Incidents is null || value.Health is null
            || value.RecentCpu is null || value.RecentMemory is null || value.Processes is null || value.Services is null){ProcessLoad=null;return;}
        Snapshot=value;State="connected";
    }
    public async ValueTask<bool> RefreshProcessLoadAsync(CancellationToken token)
    {
        if (Snapshot is not { } snapshot) return false;
        var version=++_processLoadRequestVersion;
        var value=await ReadAsync<ViewProcessLoad>("process-load",new{},token);
        if (version!=_processLoadRequestVersion || !ReferenceEquals(Snapshot,snapshot)) return false;
        ProcessLoad=value is { TopCpu.Count: <=10, TopRam.Count: <=10, CpuState: not null, RamState: not null }
            ? value : null;
        return true;
    }
    public async ValueTask<T?> ReadAsync<T>(string command,object body,CancellationToken token) where T:class
    {
        try
        {
            var bytes=await send(ViewProtocol.Request(command,body),token);
            if(bytes.Length>HelperProtocol.MaximumFrameBytes)return null;
            using var doc=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=16});
            var root=doc.RootElement;HelperProtocol.Exact(root,"version","ok","code","data");
            if(root.GetProperty("version").GetInt32()!=1 || !root.GetProperty("ok").GetBoolean()
                || root.GetProperty("code").GetString()!=command)return null;
            return root.GetProperty("data").Deserialize<T>();
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch{return null;}
    }
    public static string Export(ViewIncidentDetail detail)=>JsonSerializer.Serialize(new{format=1,incident=detail.Incident,
        confidenceScope=detail.ConfidenceScope,evidence=detail.Evidence,truncated=detail.Truncated},new JsonSerializerOptions{WriteIndented=true});
}
