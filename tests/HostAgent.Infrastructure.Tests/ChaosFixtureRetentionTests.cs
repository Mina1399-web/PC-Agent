using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class ChaosFixtureRetentionTests
{
    [Fact]
    public void Operator_run_retains_only_its_own_fixture_and_normal_tests_still_clean_up()
    {
        var root=Path.Combine(Path.GetTempPath(),"retain-"+Guid.NewGuid().ToString("N"));
        var sibling=root+"-sibling.db"; var file=Path.Combine(root,"fixture.db");
        Directory.CreateDirectory(root);File.WriteAllText(file,"synthetic");File.WriteAllText(sibling,"synthetic");
        var before=Environment.GetEnvironmentVariable("HOSTAGENT_DEV_FIXTURE_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("HOSTAGENT_DEV_FIXTURE_ROOT",root);
            ChaosFixtureRetention.DeleteFile(file);
            Assert.True(File.Exists(file));
            ChaosFixtureRetention.DeleteDirectory(root);
            Assert.True(Directory.Exists(root));
            ChaosFixtureRetention.DeleteFile(sibling);
            Assert.False(File.Exists(sibling));
            Environment.SetEnvironmentVariable("HOSTAGENT_DEV_FIXTURE_ROOT",null);
            ChaosFixtureRetention.DeleteFile(file);
            Assert.False(File.Exists(file));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOSTAGENT_DEV_FIXTURE_ROOT",before);
            if(Directory.Exists(root))Directory.Delete(root,true);
            if(File.Exists(sibling))File.Delete(sibling);
        }
    }
}
