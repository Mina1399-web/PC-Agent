namespace HostAgent.Core.Models.Telemetry;

public enum CollectorStatus
{
    Success,
    Partial,
    Unavailable,
    Failed
}

public sealed record CollectorResult<T> where T : class
{
    private CollectorResult(
        string collectorId,
        DateTimeOffset sampledAtUtc,
        CollectorStatus status,
        T? value,
        string? errorCode,
        IReadOnlyList<string> warnings)
    {
        CollectorId = collectorId;
        SampledAtUtc = sampledAtUtc.ToUniversalTime();
        Status = status;
        Value = value;
        ErrorCode = errorCode;
        Warnings = warnings;
    }

    public string CollectorId { get; }

    public DateTimeOffset SampledAtUtc { get; }

    public CollectorStatus Status { get; }

    public T? Value { get; }

    public string? ErrorCode { get; }

    public IReadOnlyList<string> Warnings { get; }

    public static CollectorResult<T> Success(
        string collectorId,
        DateTimeOffset sampledAtUtc,
        T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new CollectorResult<T>(
            collectorId,
            sampledAtUtc,
            CollectorStatus.Success,
            value,
            null,
            Array.Empty<string>());
    }

    public static CollectorResult<T> Partial(
        string collectorId,
        DateTimeOffset sampledAtUtc,
        T value,
        IEnumerable<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(warnings);
        var warningList = warnings.Where(static warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (warningList.Length == 0)
        {
            throw new ArgumentException("Partial results require at least one warning code.", nameof(warnings));
        }

        return new CollectorResult<T>(
            collectorId,
            sampledAtUtc,
            CollectorStatus.Partial,
            value,
            null,
            Array.AsReadOnly(warningList));
    }

    public static CollectorResult<T> Unavailable(
        string collectorId,
        DateTimeOffset sampledAtUtc,
        string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return new CollectorResult<T>(
            collectorId,
            sampledAtUtc,
            CollectorStatus.Unavailable,
            null,
            errorCode,
            Array.Empty<string>());
    }

    public static CollectorResult<T> Failed(
        string collectorId,
        DateTimeOffset sampledAtUtc,
        string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return new CollectorResult<T>(
            collectorId,
            sampledAtUtc,
            CollectorStatus.Failed,
            null,
            errorCode,
            Array.Empty<string>());
    }
}
