using System.Security.Principal;
using System.Security.AccessControl;
using HostAgent.Core.Contracts;
using HostAgent.Infrastructure.Security;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class ProtectedSecretStoreTests
{
    [Fact]
    public void Protected_but_invalid_configuration_falls_back_to_shadow_without_crashing_startup()
    {
        using var identity=WindowsIdentity.GetCurrent();
        var root=Root();
        try
        {
            HostAgentDataDirectory.EnsureSecure(root,identity.User!);
            var path=Path.Combine(root,"service.json");
            File.WriteAllText(path,"{}");
            var config=ServiceConfiguration.LoadSafe(path,identity.User!);
            Assert.Equal(OperatingMode.Shadow,config.Mode);
            Assert.True(config.ReadOnly);
            Assert.False(config.ConfigurationAvailable);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    // Detect plaintext persistence, unprotected input acceptance, and permissive secret-file ACLs.
    [Fact]
    public async Task Synthetic_secret_round_trips_dpapi_and_only_ciphertext_reaches_disk()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var root = Root();
        try
        {
            var store = new ProtectedSecretStore(root, identity.User!);
            await store.WriteAsync(SecretName.PushoverToken, "synthetic-dpapi-fixture", default);
            Assert.Equal("synthetic-dpapi-fixture", await new ProtectedSecretStore(root, identity.User!).ReadAsync(SecretName.PushoverToken, default));
            var file = Assert.Single(Directory.GetFiles(root));
            Assert.DoesNotContain("synthetic-dpapi-fixture", System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file)));
            await File.WriteAllTextAsync(file, "unprotected synthetic credential");
            Assert.Null(await store.ReadAsync(SecretName.PushoverToken, default));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Missing_invalid_enum_and_foreign_acl_fail_closed()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var root = Root();
        try
        {
            var store = new ProtectedSecretStore(root, identity.User!);
            Assert.Null(await store.ReadAsync(SecretName.PushoverUser, default));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await store.WriteAsync((SecretName)99, "synthetic", default));
            await store.WriteAsync(SecretName.HeartbeatUrl, "https://example.invalid/synthetic", default);
            var file = new FileInfo(Assert.Single(Directory.GetFiles(root)));
            var acl = file.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
            file.SetAccessControl(acl);
            Assert.Null(await store.ReadAsync(SecretName.HeartbeatUrl, default));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static string Root() => Path.Combine(Path.GetTempPath(), "HostAgentSecretTests-" + Guid.NewGuid().ToString("N"));
}
