using System.Text;
using System.Text.Json;
using System.Security.Principal;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Infrastructure.Storage;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class AdministrationTests
{
    // Impersonation identifies the client of the last pipe message read. A
    // connected client can still be checking the server before sending it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authentication_waits_for_first_bounded_message_before_impersonating(bool group)
    {
        using var identity=WindowsIdentity.GetCurrent();var sid=identity.User!;
        var role=group?new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null):sid;
        Assert.True(new WindowsPrincipal(identity).IsInRole(role));
        var name="HostAgent-admin-delayed-"+Guid.NewGuid().ToString("N");var calls=0;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serving=new AdminPipeServer(name,sid,role,(bytes,token)=>
        {calls++;return ValueTask.FromResult(AdminProtocol.Reply(true,"ready"));},TimeSpan.FromSeconds(3)).ServeOnceAsync(deadline.Token);
        await using var client=new System.IO.Pipes.NamedPipeClientStream(".",name,
            System.IO.Pipes.PipeAccessRights.ReadWrite|System.IO.Pipes.PipeAccessRights.Synchronize,
            System.IO.Pipes.PipeOptions.Asynchronous,TokenImpersonationLevel.Identification,System.IO.HandleInheritability.None);
        await client.ConnectAsync(deadline.Token);
        await Task.Delay(200,deadline.Token);
        Assert.False(serving.IsCompleted,"The server must wait for a message before authenticating, rather than close an unread connection.");
        await HelperProtocol.WriteFrameAsync(client,AdminProtocol.Request("status",new{}),deadline.Token);
        var reply=await HelperProtocol.ReadFrameAsync(client,deadline.Token);
        await serving;
        using var json=JsonDocument.Parse(reply);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());Assert.Equal(1,calls);
    }
    [Fact]
    public void Frame_size_and_nesting_are_bounded()
    {
        Assert.Throws<InvalidDataException>(()=>AdminProtocol.Parse(new byte[16385]));
        var nested="{\"version\":1,\"command\":\"status\",\"body\":"+new string('[',13)+"0"+new string(']',13)+"}";
        Assert.Throws<InvalidDataException>(()=>AdminProtocol.Parse(Encoding.UTF8.GetBytes(nested)));
    }
    [Fact]
    public async Task Unprivileged_authenticated_owner_cannot_dispatch_administrator_command()
    {
        using var identity=WindowsIdentity.GetCurrent();var sid=identity.User!;
        var admin=new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null);
        // In the approved non-elevated DEV account this exercises the actual production administrator role.
        var role=new WindowsPrincipal(identity).IsInRole(admin)?new SecurityIdentifier("S-1-5-21-1-2-3-9999"):admin;
        var name="HostAgent-admin-denied-"+Guid.NewGuid().ToString("N");var calls=0;string? rejection=null;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var serving=new AdminPipeServer(name,sid,role,(bytes,token)=>{calls++;return ValueTask.FromResult(AdminProtocol.Reply(true,"unexpected"));},TimeSpan.FromSeconds(1),code=>rejection=code).ServeOnceAsync(deadline.Token);
        var result=await new AdminPipeClient(name,sid,TimeSpan.FromSeconds(1)).SendAsync(AdminProtocol.Request("status",new{}),deadline.Token);
        await serving;using var json=JsonDocument.Parse(result);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());Assert.Equal(0,calls);
        Assert.Equal("control_unauthorized",rejection);
    }
    // Defect: administrative envelope accepts unknown commands/fields or duplicate keys.
    [Theory]
    [InlineData("{\"version\":2,\"command\":\"status\",\"body\":{}}")]
    [InlineData("{\"version\":1,\"command\":\"shell\",\"body\":{}}")]
    [InlineData("{\"version\":1,\"command\":\"status\",\"body\":{},\"sid\":\"S-1-5-18\"}")]
    [InlineData("{\"version\":1,\"command\":\"status\",\"command\":\"incidents\",\"body\":{}}")]
    [InlineData("{\"version\":1,\"command\":\"secret-set\",\"body\":{\"name\":\"Arbitrary\",\"value\":\"synthetic\"}}")]
    public void Invalid_envelopes_are_rejected(string json)
        => Assert.Throws<InvalidDataException>(()=>AdminProtocol.Parse(Encoding.UTF8.GetBytes(json)));
    [Fact]
    public void Status_and_closed_secret_names_are_parsed_without_reflection()
    {
        Assert.Equal("status",AdminProtocol.Parse(Encoding.UTF8.GetBytes("{\"version\":1,\"command\":\"status\",\"body\":{}}")).Command);
        var secret=AdminProtocol.Parse(Encoding.UTF8.GetBytes("{\"version\":1,\"command\":\"secret-set\",\"body\":{\"name\":\"PushoverToken\",\"value\":\"synthetic-token\"}}"));
        Assert.Equal("synthetic-token",secret.Body.GetProperty("value").GetString());
    }
    // Defect: real authenticated local IPC cannot dispatch or cancellation leaves an accept hung.
    [Fact]
    public async Task Actual_pipe_dispatches_one_request_under_authenticated_local_identity()
    {
        var sid=WindowsIdentity.GetCurrent().User!;
        var name="HostAgent-admin-test-"+Guid.NewGuid().ToString("N");
        var calls=0;
        var server=new AdminPipeServer(name,sid,sid,(bytes,token)=>
        { Assert.Equal("status",AdminProtocol.Parse(bytes).Command);calls++;return ValueTask.FromResult(AdminProtocol.Reply(true,"ready")); },TimeSpan.FromSeconds(2));
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var serving=server.ServeOnceAsync(deadline.Token);
        var result=await new AdminPipeClient(name,sid,TimeSpan.FromSeconds(2)).SendAsync(AdminProtocol.Request("status",new{}),deadline.Token);
        await serving;
        using var json=JsonDocument.Parse(result);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());Assert.Equal(1,calls);
    }
    [Fact]
    public async Task Wrong_server_owner_is_rejected_before_secret_payload_is_sent()
    {
        var sid=WindowsIdentity.GetCurrent().User!;var calls=0;
        var name="HostAgent-admin-test-"+Guid.NewGuid().ToString("N");
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var serving=new AdminPipeServer(name,sid,sid,(bytes,token)=>{calls++;return ValueTask.FromResult(AdminProtocol.Reply(true,"unexpected"));},TimeSpan.FromMilliseconds(500)).ServeOnceAsync(deadline.Token);
        var result=await new AdminPipeClient(name,new(WellKnownSidType.LocalSystemSid,null),TimeSpan.FromSeconds(1))
            .SendAsync(AdminProtocol.Request("secret-set",new{name="PushoverToken",value="synthetic-never-sent"}),default);
        await serving;
        using var json=JsonDocument.Parse(result);
        Assert.Equal("control_identity_mismatch",json.RootElement.GetProperty("code").GetString());Assert.Equal(0,calls);
    }
    // Defect: approval does not persist exact parameters/digest or does not bind the trusted approver.
    [Fact]
    public async Task Issued_approval_is_durable_exact_and_expires_within_five_minutes()
    {
        var directory=Path.Combine(Path.GetTempPath(),"HostAgent-admin-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var sid=WindowsIdentity.GetCurrent().User!.Value;
            await using var journal=new SqliteHelperJournal(Path.Combine(directory,"journal.db"),[sid]);
            var request=Task3Fixture.Request();
            var before=DateTimeOffset.UtcNow;
            var approval=await new ApprovalIssuer(journal,sid,TimeProvider.System).CreateAsync(request,new("FixtureService",null,null),new("FixtureService",null,null),default);
            Assert.Equal(new HostAgent.Core.Models.Remediation.ApprovedTarget("FixtureService",null,null),approval.Target);Assert.Equal(request.RequestId,approval.RequestId);Assert.Equal(sid,approval.ApproverSid);
            Assert.Equal(HelperProtocol.Digest(request),approval.RequestDigest);
            Assert.InRange(approval.ApprovedAtUtc,before,DateTimeOffset.UtcNow);
            Assert.Equal(TimeSpan.FromMinutes(5),approval.ExpiresAtUtc-approval.ApprovedAtUtc);
            Assert.Equal(approval,await journal.ReadApprovalAsync(approval.ApprovalId,default));
            Assert.NotEqual(approval.RequestDigest,HelperProtocol.Digest(request with{Parameters=new HostAgent.Core.Models.Remediation.ServiceParameters("different")}));
        }
        finally{Directory.Delete(directory,true);}
    }
}
