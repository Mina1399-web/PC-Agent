using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;

public static class IsolatedScmDashboardDispatch
{
    public static ValueTask<byte[]> DispatchAsync(
        byte[] payload,
        Func<byte[], CancellationToken, ValueTask<byte[]>> overviewDispatcher,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(overviewDispatcher);
        if (token.IsCancellationRequested) return ValueTask.FromCanceled<byte[]>(token);

        AdminMessage request;
        try { request = ViewProtocol.Parse(payload); }
        catch (InvalidDataException)
        {
            return ValueTask.FromResult(AdminProtocol.Reply(false, "view_request_rejected", null));
        }

        if (request.Command != "overview")
            return ValueTask.FromResult(AdminProtocol.Reply(false, "view_read_only", null));

        return overviewDispatcher(payload, token);
    }
}
