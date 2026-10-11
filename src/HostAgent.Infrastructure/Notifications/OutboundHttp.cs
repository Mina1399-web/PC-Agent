namespace HostAgent.Infrastructure.Notifications;

public static class OutboundHttp
{
    // No factory diagnostics handlers: request URIs can contain watchdog/receipt credentials.
    public static HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(5)
    }) { Timeout = Timeout.InfiniteTimeSpan };

    public static CancellationTokenSource Deadline(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout < TimeSpan.FromMilliseconds(50) || timeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    internal static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > 16_384) throw new InvalidDataException("response_too_large");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
