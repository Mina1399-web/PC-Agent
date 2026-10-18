using System.Security.Principal;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Storage;
using HostAgent.Service;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class CompleteWpfTransportTests
{
    [Fact]
    public async Task Real_private_pipe_projects_sqlite_partial_data_clears_outage_and_reconnects()
    {
        var id=Guid.NewGuid().ToString("N");
        var root=Path.Combine(Path.GetTempPath(),"HostAgent-complete-view-"+id);
        Directory.CreateDirectory(root);
        try
        {
            await using var store=new SqliteHostAgentStore(Path.Combine(root,"fixture.db"));
            await store.InitializeAsync(default);
            await store.SaveMetricSampleAsync(new(Guid.NewGuid(),"cpu.usage.percent","host",DateTimeOffset.UtcNow.AddSeconds(-1),42),default);
            using var identity=WindowsIdentity.GetCurrent();
            var sid=identity.User!;
            var dispatcher=new DashboardDispatcher(store,new(),new(),TimeProvider.System);
            var server=new AdminPipeServer("HostAgent.Dev.Complete.Test."+id,sid,sid,dispatcher.DispatchAsync,TimeSpan.FromSeconds(2));
            var client=new ViewPipeClient("HostAgent.Dev.Complete.Test."+id,sid,TimeSpan.FromSeconds(1));
            var controller=new DashboardController(client.SendAsync);
            using(var worker=new DashboardWorker(server.ServeOnceAsync))
            {
                await worker.StartAsync(default);
                try
                {
                    await controller.RefreshAsync(default);
                    Assert.Equal("connected",controller.State);
                    Assert.Equal(42,controller.Snapshot!.Cpu!.Value);
                    Assert.Null(controller.Snapshot.Memory);
                    Assert.True(controller.Snapshot.ReadOnly);
                    Assert.Equal("Shadow",controller.Snapshot.Mode);
                }
                finally {await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));}
            }
            await controller.RefreshAsync(default);
            Assert.Null(controller.Snapshot);
            Assert.Equal("disconnected",controller.State);
            await store.SaveMetricSampleAsync(new(Guid.NewGuid(),"memory.used.percent","host",DateTimeOffset.UtcNow.AddSeconds(-1),64),default);
            using(var restarted=new DashboardWorker(server.ServeOnceAsync))
            {
                await restarted.StartAsync(default);
                try
                {
                    await controller.RefreshAsync(default);
                    Assert.Equal("connected",controller.State);
                    Assert.Equal(42,controller.Snapshot!.Cpu!.Value);
                    Assert.Equal(64,controller.Snapshot.Memory!.Value);
                }
                finally {await restarted.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));}
            }
        }
        finally {Directory.Delete(root,true);}
    }
}
