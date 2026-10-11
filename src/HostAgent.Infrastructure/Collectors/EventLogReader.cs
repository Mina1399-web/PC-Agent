using System.Diagnostics.Eventing.Reader;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace HostAgent.Infrastructure.Collectors;

internal interface IEventLogReaderFactory
{
    IEventLogReader Create(string logName, string? bookmarkXml, string queryText, bool reverseDirection = false);
}

internal interface IEventLogReader : IDisposable
{
    EventLogRecordData? ReadEvent();
}

internal sealed record EventLogRecordData(
    long? RecordId,
    int? Level,
    int EventId,
    string? ProviderName,
    DateTime? TimeCreated,
    string? MachineName,
    string? BookmarkXml,
    int? ProcessId = null,
    int? ThreadId = null,
    string? EventFingerprint = null)
{
    public string? AffectedServiceName { get; init; }
}

internal sealed class WindowsEventLogReaderFactory : IEventLogReaderFactory
{
    internal static string? ReadAffectedServiceName(string xml)
    {
        try
        {
            using var input = new StringReader(xml);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 262_144
            });
            var document = XDocument.Load(reader);
            var root = document.Root;
            if (root is null) return null;
            var ns = root.Name.Namespace;
            var system = root.Element(ns + "System");
            if ((string?)system?.Element(ns + "Provider")?.Attribute("Name") != "Service Control Manager"
                || (string?)system?.Element(ns + "EventID") is not ("7031" or "7034")) return null;
            var name = root.Element(ns + "EventData")?.Elements(ns + "Data")
                .FirstOrDefault(item => (string?)item.Attribute("Name") == "param1")?.Value.Trim();
            return string.IsNullOrWhiteSpace(name) || name.Length > 256 || name.Any(char.IsControl) ? null : name;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    public IEventLogReader Create(string logName, string? bookmarkXml, string queryText, bool reverseDirection = false)
    {
        var query = new EventLogQuery(logName, PathType.LogName, queryText)
        {
            ReverseDirection = reverseDirection
        };
        var bookmark = bookmarkXml is null ? null : new EventBookmark(bookmarkXml);
        return new WindowsEventLogReader(new EventLogReader(query, bookmark));
    }

    private sealed class WindowsEventLogReader(EventLogReader reader) : IEventLogReader
    {
        public EventLogRecordData? ReadEvent()
        {
            using var record = reader.ReadEvent();
            if (record is null)
            {
                return null;
            }

            string? eventFingerprint = null;
            string? affectedServiceName = null;
            try
            {
                // Retain only a digest for checkpoint generation checks; the event XML is never persisted.
                var xml = record.ToXml();
                eventFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)));
                if (record.Id is 7031 or 7034 && record.ProviderName == "Service Control Manager")
                    affectedServiceName = ReadAffectedServiceName(xml);
            }
            catch (EventLogException)
            {
                // Keep the metadata-based fingerprint fallback when XML rendering is unavailable.
            }

            return new EventLogRecordData(
                record.RecordId,
                record.Level,
                record.Id,
                record.ProviderName,
                record.TimeCreated,
                record.MachineName,
                record.Bookmark?.BookmarkXml,
                record.ProcessId,
                record.ThreadId,
                eventFingerprint) { AffectedServiceName = affectedServiceName };
        }

        public void Dispose() => reader.Dispose();
    }
}
