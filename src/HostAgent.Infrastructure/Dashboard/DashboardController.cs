using HostAgent.Core.Dashboard;
using HostAgent.Core.Models.Incidents;
using System.Text.Json;
using HostAgent.Infrastructure.Pipes;
namespace HostAgent.Infrastructure.Dashboard;

public sealed class DashboardController(Func<byte[],CancellationToken,ValueTask<byte[]>> send)
{
    public DashboardSnapshot? Snapshot {get;private set;}
    public string State {get;private set;}="disconnected";
    public async ValueTask RefreshAsync(CancellationToken token)
    {
        Snapshot=null;State="disconnected";
        var value=await ReadAsync<DashboardSnapshot>("overview",new{},token);
        if(value is null)return;
        if(!ViewFreshness.IsFresh(value.GeneratedAtUtc,DateTimeOffset.UtcNow)){State="stale";return;}
        if(value.Mode is not ("Shadow" or "Confirmation") || value.Disks is null || value.Incidents is null || value.Health is null
            || value.RecentCpu is null || value.RecentMemory is null || value.Processes is null || value.Services is null)return;
        Snapshot=value;State="connected";
    }
    public async ValueTask<T?> ReadAsync<T>(string command,object body,CancellationToken token) where T:class
    {
        try
        {
            var bytes=await send(ViewProtocol.Request(command,body),token);
            var maxBytes = command == "incident-page" ? ViewProtocol.MaximumIncidentPageBytes : HelperProtocol.MaximumFrameBytes;
            if(bytes.Length>maxBytes)return null;
            using var doc=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=16});
            var root=doc.RootElement;HelperProtocol.Exact(root,"version","ok","code","data");
            if(root.GetProperty("version").GetInt32()!=1 || !root.GetProperty("ok").GetBoolean()
                || root.GetProperty("code").GetString()!=command)return null;
            return root.GetProperty("data").Deserialize<T>();
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch{return null;}
    }

    public async ValueTask<ViewIncidentPage?> ReadIncidentPageAsync(IncidentHistoryCursor? before, CancellationToken token)
    {
        var page = await ReadAsync<ViewIncidentPage>("incident-page", new
        {
            before = before is null ? null : new { createdAtUtcTicks = before.CreatedAtUtcTicks, incidentId = before.IncidentId }
        }, token);
        if (page?.Rows is not { Count: <= IncidentHistoryCursor.PageSize }) return null;

        var previous = before;
        foreach (var row in page.Rows)
        {
            if (row?.CreatedAtUtc is not { } created || row.UpdatedAtUtc is not { } updated || updated < created)
                return null;
            var cursor = new IncidentHistoryCursor(created.UtcTicks, row.Id);
            if (!cursor.IsValid() || previous is not null
                && (cursor.CreatedAtUtcTicks > previous.CreatedAtUtcTicks ||
                    cursor.CreatedAtUtcTicks == previous.CreatedAtUtcTicks &&
                    string.CompareOrdinal(cursor.IncidentId, previous.IncidentId) >= 0))
                return null;
            previous = cursor;
        }
        if (page.Next is not null && (page.Rows.Count != IncidentHistoryCursor.PageSize || page.Next != previous))
            return null;
        return page;
    }
    public static string Export(ViewIncidentDetail detail)=>JsonSerializer.Serialize(new{format=1,incident=detail.Incident,
        confidenceScope=detail.ConfidenceScope,evidence=detail.Evidence,truncated=detail.Truncated},new JsonSerializerOptions{WriteIndented=true});
}
