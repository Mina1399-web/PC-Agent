using HostAgent.Infrastructure.Storage;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Core.Models.Remediation;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class NamedPipeSecurityTests
{
    [Fact]
    public async Task Malformed_oversized_and_partial_actual_frames_never_reach_executor()
    {
        await using var fixture = new Task3Fixture(); var sid = WindowsIdentity.GetCurrent().User!;
        foreach (var payload in new[] { new byte[] { 1, 64, 0, 0 }, new byte[] { 2, 0, 0, 0, 123, 125 }, new byte[] { 9, 0, 0, 0, 1 } })
        {
            var name = "HostAgent-test-" + Guid.NewGuid().ToString("N");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var server = new NamedPipeHelperServer(name, new(sid, sid), fixture.Dispatcher(), TimeSpan.FromMilliseconds(300));
            var serving = server.ServeOnceAsync(deadline.Token);
            using var client = new NamedPipeClientStream(".", name, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Identification, HandleInheritability.None);
            await client.ConnectAsync(deadline.Token); await client.WriteAsync(payload, deadline.Token);
            await serving; Assert.NotNull(server.LastRejectionCode);
        }
        Assert.Equal(0, fixture.Executor.Calls);
    }
    [Fact]
    public void Acl_has_only_pinned_identities_and_service_cannot_create_server_instances()
    {
        var service = new SecurityIdentifier(Task3Fixture.CallerSid);
        var helper = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var authorization = new PipeAuthorization(service, helper);
        Assert.True(authorization.IsAuthorized(service.Value));
        Assert.False(authorization.IsAuthorized(helper.Value));
        Assert.False(authorization.IsAuthorized("S-1-1-0")); Assert.False(authorization.IsAuthorized(null));
        var acl = authorization.CreateSecurity(); Assert.True(acl.AreAccessRulesProtected);
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToArray();
        var serviceRule = Assert.Single(rules, r => r.IdentityReference.Value == service.Value);
        Assert.Equal(AccessControlType.Allow, serviceRule.AccessControlType);
        Assert.Equal(0, (int)(serviceRule.PipeAccessRights & PipeAccessRights.CreateNewInstance));
        Assert.DoesNotContain(rules, r => r.IdentityReference.Value == "S-1-1-0");
        Assert.Contains(rules, r => r.IdentityReference.Value == "S-1-5-2" && r.AccessControlType == AccessControlType.Deny);
    }

    [Fact]
    public async Task Actual_windows_pipe_authenticates_token_and_dispatches_fake_operation_once()
    {
        await using var fixture = new Task3Fixture(); var request = await fixture.Approve(Task3Fixture.Request());
        var sid = WindowsIdentity.GetCurrent().User!;
        var pipe = "HostAgent-test-" + Guid.NewGuid().ToString("N");
        var server = new NamedPipeHelperServer(pipe, new(sid, sid), fixture.Dispatcher(), TimeSpan.FromSeconds(10));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var serving = server.ServeOnceAsync(deadline.Token);
        var client = new NamedPipeHelperClient(pipe, sid, TimeSpan.FromSeconds(10));
        var response = await client.ExecuteAsync(request, deadline.Token); await serving;
        Assert.Equal(HelperResult.Succeeded, response.Result); Assert.Equal(1, fixture.Executor.Calls);
        var audit = await fixture.Journal.ReadAuditAsync(request.RequestId, default);
        Assert.Equal(sid.Value, audit[^1].InitiatorSid);
    }

    [Fact]
    public async Task Actual_server_rejects_unlisted_token_without_executor_call()
    {
        await using var fixture = new Task3Fixture(); var sid = WindowsIdentity.GetCurrent().User!;
        var pipe = "HostAgent-test-" + Guid.NewGuid().ToString("N");
        var server = new NamedPipeHelperServer(pipe, new(new(Task3Fixture.CallerSid), sid),
            fixture.Dispatcher(), TimeSpan.FromSeconds(2));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var serving = server.ServeOnceAsync(deadline.Token);
        var response = await new NamedPipeHelperClient(pipe, sid, TimeSpan.FromMilliseconds(500)).ExecuteAsync(Task3Fixture.Request(), default);
        Assert.NotEqual(HelperResult.Succeeded, response.Result);
        deadline.Cancel(); await serving;
        Assert.Equal(0, fixture.Executor.Calls);
    }

    [Fact]
    public async Task Client_rejects_wrong_server_identity_and_missing_helper()
    {
        await using var fixture = new Task3Fixture(); var sid = WindowsIdentity.GetCurrent().User!;
        var pipe = "HostAgent-test-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serving = new NamedPipeHelperServer(pipe, new(sid, sid), fixture.Dispatcher(), TimeSpan.FromSeconds(2)).ServeOnceAsync(deadline.Token);
        var wrong = await new NamedPipeHelperClient(pipe, new(WellKnownSidType.LocalSystemSid, null), TimeSpan.FromSeconds(1))
            .ExecuteAsync(Task3Fixture.Request(), default);
        Assert.Equal(HelperResult.Denied, wrong.Result); Assert.Equal("helper_identity_mismatch", wrong.Code);
        deadline.Cancel(); await serving;
        var missing = await new NamedPipeHelperClient("HostAgent-absent-" + Guid.NewGuid().ToString("N"), sid, TimeSpan.FromMilliseconds(100))
            .ExecuteAsync(Task3Fixture.Request(), default);
        Assert.Equal(HelperResult.TimedOut, missing.Result); Assert.Equal(0, fixture.Executor.Calls);
    }
    [Fact]
    public void Authenticated_server_and_client_are_required_instead_of_caller_identity_fields()
    {
        var assembly = typeof(SqliteHostAgentStore).Assembly;
        Assert.NotNull(assembly.GetType("HostAgent.Infrastructure.Security.PipeAuthorization"));
        Assert.NotNull(assembly.GetType("HostAgent.Infrastructure.Pipes.NamedPipeHelperServer"));
        Assert.NotNull(assembly.GetType("HostAgent.Infrastructure.Pipes.NamedPipeHelperClient"));
    }
}
