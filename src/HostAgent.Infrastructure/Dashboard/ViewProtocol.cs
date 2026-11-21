using System.Text.Json;
using HostAgent.Core.Models.Incidents;
using HostAgent.Core.Models.Inventory;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Pipes;
namespace HostAgent.Infrastructure.Dashboard;

public static class ViewProtocol
{
    public const int MaximumIncidentPageBytes = 16 * 1024;

    public static AdminMessage Parse(byte[] bytes)
    {
        try
        {
            if(bytes.Length is <=0 or >HelperProtocol.MaximumFrameBytes)throw new InvalidDataException();
            using var doc=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=8});
            var root=doc.RootElement;HelperProtocol.Exact(root,"version","command","body");
            if(root.GetProperty("version").GetInt32()!=1)throw new InvalidDataException();
            var command=root.GetProperty("command").GetString();var body=root.GetProperty("body");
            switch(command)
            {
                case "overview":case "investigate":case "process-load":HelperProtocol.Exact(body);break;
                case "disk-history":
                    HelperProtocol.Exact(body,"group");
                    if(body.GetProperty("group").GetString() is not ("throughput" or "latency"))throw new InvalidDataException();
                    break;
                case "hardware-inventory":
                    HelperProtocol.Exact(body,"category");
                    if(HardwareInventorySources.ForCategory(body.GetProperty("category").GetString()) is null)
                        throw new InvalidDataException();
                    break;
                case "history":
                    HelperProtocol.Exact(body,"metric");
                    if(body.GetProperty("metric").GetString() is not ("cpu" or "memory"))throw new InvalidDataException();break;
                case "incident":
                    HelperProtocol.Exact(body,"incidentId");
                    if(!Guid.TryParseExact(body.GetProperty("incidentId").GetString(),"N",out var id)||id==Guid.Empty)throw new InvalidDataException();break;
                case "incident-page":
                    if (bytes.Length > MaximumIncidentPageBytes) throw new InvalidDataException();
                    _ = ReadIncidentCursor(body);
                    break;
                default:throw new InvalidDataException();
            }
            return new(command!,body.Clone());
        }
        catch(Exception e) when(e is JsonException or InvalidDataException or InvalidOperationException or FormatException or ArgumentException)
        {throw new InvalidDataException("view_invalid_request");}
    }

    public static IncidentHistoryCursor? ReadIncidentCursor(JsonElement body)
    {
        HelperProtocol.Exact(body, "before");
        var cursor = body.GetProperty("before");
        if (cursor.ValueKind == JsonValueKind.Null) return null;
        HelperProtocol.Exact(cursor, "createdAtUtcTicks", "incidentId");
        var before = new IncidentHistoryCursor(cursor.GetProperty("createdAtUtcTicks").GetInt64(),
            cursor.GetProperty("incidentId").GetString() ?? "");
        return before.IsValid() ? before : throw new InvalidDataException("view_invalid_request");
    }
    public static byte[] Request(string command,object body)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new{version=1,command,body});_=Parse(bytes);return bytes;
    }
}
