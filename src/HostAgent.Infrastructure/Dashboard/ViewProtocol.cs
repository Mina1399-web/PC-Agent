using System.Text.Json;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Pipes;
namespace HostAgent.Infrastructure.Dashboard;

public static class ViewProtocol
{
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
                case "overview":case "investigate":HelperProtocol.Exact(body);break;
                case "history":
                    HelperProtocol.Exact(body,"metric");
                    if(body.GetProperty("metric").GetString() is not ("cpu" or "memory"))throw new InvalidDataException();break;
                case "incident":
                    HelperProtocol.Exact(body,"incidentId");
                    if(!Guid.TryParseExact(body.GetProperty("incidentId").GetString(),"N",out var id)||id==Guid.Empty)throw new InvalidDataException();break;
                default:throw new InvalidDataException();
            }
            return new(command!,body.Clone());
        }
        catch(Exception e) when(e is JsonException or InvalidDataException or InvalidOperationException or FormatException or ArgumentException)
        {throw new InvalidDataException("view_invalid_request");}
    }
    public static byte[] Request(string command,object body)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new{version=1,command,body});_=Parse(bytes);return bytes;
    }
}
