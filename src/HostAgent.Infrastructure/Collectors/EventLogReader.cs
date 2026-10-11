using System.Diagnostics.Eventing.Reader;
using System.Security.Cryptography;
using System.Text;

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
    string? EventFingerprint = null);

internal sealed class WindowsEventLogReaderFactory : IEventLogReaderFactory
{
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
            try
            {
                // Retain only a digest for checkpoint generation checks; the event XML is never persisted.
                eventFingerprint = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(record.ToXml())));
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
                eventFingerprint);
        }

        public void Dispose() => reader.Dispose();
    }
}
