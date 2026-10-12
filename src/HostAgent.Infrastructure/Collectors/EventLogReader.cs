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
    public ParsedBugCheckData? BugCheck { get; init; }
}

internal sealed record ParsedBugCheckData(uint Code, IReadOnlyList<ulong> Parameters, string? DumpFileName);

internal sealed class WindowsEventLogReaderFactory : IEventLogReaderFactory
{
    internal static ParsedBugCheckData? ReadBugCheckData(string xml)
    {
        if (xml.Length > 262_144) return null;
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
            if ((string?)system?.Element(ns + "Provider")?.Attribute("Name") != "Microsoft-Windows-WER-SystemErrorReporting"
                || (string?)system?.Element(ns + "EventID") != "1001") return null;

            var values = (root.Element(ns + "EventData")?.Elements(ns + "Data") ?? [])
                .Where(item => (string?)item.Attribute("Name") is "param1" or "param2")
                .ToDictionary(item => (string)item.Attribute("Name")!, item => item.Value, StringComparer.Ordinal);
            if (!values.TryGetValue("param1", out var bugcheckText)
                || !TryParseBugCheck(bugcheckText, out var code, out var parameters)) return null;
            var dumpFileName = values.TryGetValue("param2", out var dumpPath) ? SafeDumpFileName(dumpPath) : null;
            return new ParsedBugCheckData(code, parameters, dumpFileName);
        }
        catch (XmlException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool TryParseBugCheck(string value, out uint code, out IReadOnlyList<ulong> parameters)
    {
        code = 0;
        parameters = [];
        if (value.Length is 0 or > 256) return false;
        var open = value.IndexOf('(');
        var close = value.LastIndexOf(')');
        if (open <= 0 || close != value.Length - 1 || !TryParseHex(value[..open], out var rawCode) || rawCode > uint.MaxValue)
            return false;
        var parts = value[(open + 1)..close].Split(',');
        if (parts.Length != 4) return false;
        var parsed = new ulong[4];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!TryParseHex(parts[index], out parsed[index])) return false;
        }
        code = (uint)rawCode;
        parameters = Array.AsReadOnly(parsed);
        return true;
    }

    private static bool TryParseHex(string value, out ulong number)
    {
        number = 0;
        value = value.Trim();
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ulong.TryParse(value.AsSpan(2), System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture, out number);
    }

    private static string? SafeDumpFileName(string value)
    {
        if (value.Length is 0 or > 512) return null;
        var normalized = value.Trim().Replace('/', '\\');
        var separator = normalized.LastIndexOf('\\');
        var windowsRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (separator < 0 || string.IsNullOrWhiteSpace(windowsRoot)) return null;
        var expectedDirectory = Path.Combine(windowsRoot, "Minidump").TrimEnd('\\');
        if (!normalized[..separator].Equals(expectedDirectory, StringComparison.OrdinalIgnoreCase)) return null;
        var fileName = normalized[(separator + 1)..];
        var stem = fileName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase) ? fileName[..^4] : string.Empty;
        var parts = stem.Split('-');
        return parts.Length == 3 && parts[0].Length == 6 && parts.All(part => part.Length is > 0 and <= 12 && part.All(char.IsAsciiDigit))
            ? fileName
            : null;
    }

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
            ParsedBugCheckData? bugCheck = null;
            try
            {
                // Retain only a digest for checkpoint generation checks; the event XML is never persisted.
                var xml = record.ToXml();
                eventFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)));
                if (record.Id is 7031 or 7034 && record.ProviderName == "Service Control Manager")
                    affectedServiceName = ReadAffectedServiceName(xml);
                if (record.Id == 1001 && record.ProviderName == "Microsoft-Windows-WER-SystemErrorReporting")
                    bugCheck = ReadBugCheckData(xml);
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
                eventFingerprint) { AffectedServiceName = affectedServiceName, BugCheck = bugCheck };
        }

        public void Dispose() => reader.Dispose();
    }
}
