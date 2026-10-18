using Xunit;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace HostAgent.Infrastructure.Tests;

public sealed class CompleteWpfProofTests
{
    private static CompleteWpfProof Complete => new(true,true,true,3,10,true,true,true);

    [Fact]
    public void Accepts_only_complete_read_only_live_wpf_proof()
    {
        Assert.True(CompleteWpfProbe.Accept(Complete));
        Assert.False(CompleteWpfProbe.Accept(Complete with { PartialCleared=false }));
        Assert.False(CompleteWpfProbe.Accept(Complete with { FreshMetrics=false }));
        Assert.False(CompleteWpfProbe.Accept(Complete with { StaleCleared=false }));
        Assert.False(CompleteWpfProbe.Accept(Complete with { OutageRecoveryCycles=2 }));
        Assert.False(CompleteWpfProbe.Accept(Complete with { MinimumOutageSeconds=9.9 }));
        Assert.False(CompleteWpfProbe.Accept(Complete with { MinimumOutageSeconds=double.NaN }));
        Assert.False(CompleteWpfProbe.Accept(Complete with { ActionsAlwaysDisabled=false }));
        Assert.False(CompleteWpfProbe.Accept(Complete with { WindowClosed=false }));
        Assert.False(CompleteWpfProbe.Accept(Complete with { QuickCheckHealthy=false }));
    }

    [Fact]
    public void Immutable_quick_check_preserves_fixture_bytes_and_inventory()
    {
        var root=Path.Combine(Path.GetTempPath(),"HostAgent-wpf-readonly-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file=Path.Combine(root,"fixture.db");
        try
        {
            using(var db=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=file,Pooling=false}.ToString()))
            {
                db.Open();using var command=db.CreateCommand();
                command.CommandText="CREATE TABLE synthetic_proof(id INTEGER PRIMARY KEY); INSERT INTO synthetic_proof VALUES(1);";
                command.ExecuteNonQuery();
            }
            var before=SHA256.HashData(File.ReadAllBytes(file));
            var names=Directory.GetFiles(root).Select(Path.GetFileName).OrderBy(x=>x).ToArray();
            Assert.True(CompleteWpfProbe.ReadOnlyQuickCheck(file));
            Assert.Equal(before,SHA256.HashData(File.ReadAllBytes(file)));
            Assert.Equal(names,Directory.GetFiles(root).Select(Path.GetFileName).OrderBy(x=>x).ToArray());
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public void Immutable_quick_check_refuses_uncheckpointed_sidecar_without_modifying_it()
    {
        var root=Path.Combine(Path.GetTempPath(),"HostAgent-wpf-journal-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file=Path.Combine(root,"fixture.db");
        try
        {
            File.WriteAllBytes(file,[]);
            File.WriteAllBytes(file+"-wal",[1,2,3]);
            Assert.Throws<InvalidOperationException>(()=>CompleteWpfProbe.ReadOnlyQuickCheck(file));
            Assert.Equal(new byte[]{1,2,3},File.ReadAllBytes(file+"-wal"));
            Assert.Equal(0,new FileInfo(file).Length);
        }
        finally {Directory.Delete(root,true);}
    }
}
