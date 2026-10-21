using HostAgent.Core.Contracts;
using HostAgent.Core.Orchestration;
using HostAgent.Core.Models.Diagnosis;
using HostAgent.Infrastructure.Security;
using HostAgent.Infrastructure.Storage;
using HostAgent.Service;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using HostAgent.Core.Diagnostics;
using HostAgent.Core.Models.Incidents;
namespace HostAgent.Infrastructure.Tests;
public sealed class ServiceLifecycleTests
{
    [Fact]
    public async Task Successful_collection_cycle_duration_is_included_in_sampling_interval()
    {
        var clock = new SamplingClock();
        using var worker = new ResilientWorker(_ =>
        {
            clock.Advance(TimeSpan.FromSeconds(20));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(2), clock, _ => { });
        await worker.StartAsync(default);
        try
        {
            var delay = await clock.ScheduledDelay.Task.WaitAsync(TimeSpan.FromSeconds(3));
            // CPU evidence requires five samples within five minutes. Waiting a
            // full minute AFTER a slow collection silently makes that impossible.
            Assert.Equal(TimeSpan.FromSeconds(40), delay);
        }
        finally { await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(2)); }
    }

    private sealed class SamplingClock : TimeProvider
    {
        private long ticks;
        public TaskCompletionSource<TimeSpan> ScheduledDelay { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref ticks, elapsed.Ticks);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime > TimeSpan.Zero && dueTime <= TimeSpan.FromSeconds(60))
                ScheduledDelay.TrySetResult(dueTime);
            return new DormantTimer();
        }
        private sealed class DormantTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Reopened_incident_gets_fresh_notice_but_ordinary_samples_do_not_spam()
    {
        await using var fixture=await OperatingModeTests.Fixture.Create();
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        try
        {
            var notices=new SqliteNotificationStore(path);
            var publisher=new IncidentPublisher(fixture.Store,notices,new());
            var incident=Assert.Single(await fixture.Store.ReadIncidentHeadsAsync(default));
            var now=DateTimeOffset.UtcNow;
            await publisher.PublishAsync([incident],now,default);
            var engine=new IncidentEngine();
            engine.Restore([incident]);
            incident=await engine.TransitionAsync(incident.IncidentId,IncidentLifecycleState.Resolved,
                incident.Snapshots[^1] with{SnapshotId=Guid.NewGuid().ToString("N"),CapturedAtUtc=now});
            await publisher.PublishAsync([incident],now,default);
            incident=await engine.ApplyAsync(incident.LatestDiagnosis with{ObservedAtUtc=now.AddSeconds(1)},incident.Snapshots[^1] with{SnapshotId=Guid.NewGuid().ToString("N"),CapturedAtUtc=now.AddSeconds(1)});
            await fixture.Store.SaveIncidentBundleAsync(incident,default);
            // Reopen both incident and publisher persistence, not just an in-memory dedupe map.
            incident=Assert.Single(await fixture.Store.ReadIncidentHeadsAsync(default));
            await new IncidentPublisher(fixture.Store,new SqliteNotificationStore(path),new()).PublishAsync([incident],now.AddSeconds(1),default);
            Assert.Equal(3,(await notices.ReadDueAsync(now.AddSeconds(1),default)).Count);
            incident=await engine.ApplyAsync(incident.LatestDiagnosis with{ObservedAtUtc=now.AddSeconds(2)},incident.Snapshots[^1] with{SnapshotId=Guid.NewGuid().ToString("N"),CapturedAtUtc=now.AddSeconds(2)});
            await publisher.PublishAsync([incident],now.AddSeconds(2),default);
            incident=await engine.ApplyAsync(incident.LatestDiagnosis with{ObservedAtUtc=now.AddSeconds(3)},incident.Snapshots[^1] with{SnapshotId=Guid.NewGuid().ToString("N"),CapturedAtUtc=now.AddSeconds(3)});
            await publisher.PublishAsync([incident],now.AddSeconds(3),default);
            Assert.Equal(4,(await notices.ReadDueAsync(now.AddSeconds(3),default)).Count);
        }
        finally{File.Delete(path);}
    }
    [Fact]
    public async Task Cooperative_cycle_cleanup_finishes_before_shutdown_disposes_dependencies()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned=false;
        using var worker=new ResilientWorker(async token=>
        {
            entered.TrySetResult();
            try{await Task.Delay(Timeout.InfiniteTimeSpan,token);}
            finally{await Task.Delay(600);cleaned=true;}
        },TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(3),TimeProvider.System,_=>{});
        await worker.StartAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(cleaned);
    }
    [Fact]
    public async Task Slow_notification_does_not_delay_heartbeat_worker_or_service_shutdown()
    {
        await using var fixture=await OperatingModeTests.Fixture.Create();
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        try
        {
            var notificationStore=new SqliteNotificationStore(path);
            await notificationStore.EnqueueAsync(new("n","i",DiagnosisSeverity.High,"Synthetic"),DateTimeOffset.UtcNow,default);
            var blocked=new BlockingNotification();
            var heartbeat=new SignallingHeartbeat();
            using var host=new HostBuilder().ConfigureServices(services=>
            {
                services.AddSingleton<IHostAgentStore>(fixture.Store);
                services.AddSingleton<INotificationStore>(notificationStore);
                services.AddSingleton<INotificationSender>(blocked);
                services.AddSingleton<IHeartbeatSender>(heartbeat);
                services.AddSingleton<IHelperClient>(fixture.Helper);
                services.AddSingleton<DiagnosticCycleProcessor>();
                ServiceRuntime.AddWorkers(services,new());
            }).Build();
            await host.StartAsync();
            Assert.Same(blocked.Entered.Task,await Task.WhenAny(blocked.Entered.Task,Task.Delay(2000)));
            Assert.Same(heartbeat.Called.Task,await Task.WhenAny(heartbeat.Called.Task,Task.Delay(1000)));
            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(heartbeat.Last!.Stopping);
        }
        finally{File.Delete(path);}
    }
    private sealed class BlockingNotification:INotificationSender
    {
        public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<DeliveryResult> SendAsync(Notification notification,CancellationToken cancellationToken)
        {Entered.TrySetResult();await Task.Delay(Timeout.InfiniteTimeSpan,cancellationToken);return new(DeliveryState.Sent,"synthetic");}
        public ValueTask<DeliveryResult> PollAsync(string receipt,CancellationToken cancellationToken)=>throw new NotSupportedException();
    }
    private sealed class SignallingHeartbeat:IHeartbeatSender
    {
        public TaskCompletionSource Called{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AgentHealth? Last{get;private set;}
        public ValueTask<DeliveryResult> SendAsync(AgentHealth health,CancellationToken cancellationToken)
        {Last=health;Called.TrySetResult();return ValueTask.FromResult(new DeliveryResult(DeliveryState.Sent,"synthetic"));}
    }
    [Fact]
    public void Production_worker_registration_exposes_collection_and_independent_outbound_worker()
    {
        var type=typeof(ResilientWorker).Assembly.GetType("HostAgent.Service.ServiceRuntime");
        Assert.NotNull(type);
        var method=type.GetMethod("AddWorkers");
        Assert.NotNull(method);
        var services=new ServiceCollection();
        method.Invoke(null,[services,new ServiceConfiguration()]);
        Assert.Equal(2,services.Count(descriptor=>descriptor.ServiceType==typeof(IHostedService)));
        Assert.Contains(services,descriptor=>descriptor.ServiceType==typeof(RemediationCoordinator));
    }
    [Fact]
    public async Task Publication_recovers_if_process_stopped_between_audit_and_outbox_commit()
    {
        await using var fixture=await OperatingModeTests.Fixture.Create();
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        try
        {
            var store=new SqliteNotificationStore(path);
            var flaky=new FailEnqueueOnce(store);
            var incidents=await fixture.Store.ReadIncidentHeadsAsync(default);
            await Assert.ThrowsAsync<IOException>(async()=>await new IncidentPublisher(fixture.Store,flaky,new()).PublishAsync(incidents,DateTimeOffset.UtcNow,default));
            await new IncidentPublisher(fixture.Store,store,new()).PublishAsync(incidents,DateTimeOffset.UtcNow,default);
            Assert.Single(await store.ReadDueAsync(DateTimeOffset.UtcNow,default));
        }
        finally {File.Delete(path);}
    }
    // Worker failures must retry with backoff, never overlap, and stop despite a non-cooperative cycle.
    [Fact]
    public async Task Worker_retries_failure_and_stops_promptly_through_generic_host()
    {
        var calls=0;
        var recovered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reports=new List<string>();
        using var host=new HostBuilder().ConfigureServices(services=>
            services.AddHostedService(_=>new ResilientWorker(_=>
            {
                if (Interlocked.Increment(ref calls)==1) throw new IOException("sensitive synthetic exception");
                recovered.TrySetResult();
                return Task.CompletedTask;
            },TimeSpan.FromMilliseconds(30),TimeSpan.FromSeconds(1),TimeProvider.System,code=>reports.Add(code)))).Build();
        await host.StartAsync();
        var complete=await Task.WhenAny(recovered.Task,Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(recovered.Task,complete);
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Contains("cycle_failed",reports);
        Assert.Contains("cycle_recovered",reports);
        Assert.DoesNotContain(reports,code=>code.Contains("sensitive"));
    }
    [Fact]
    public async Task Timeout_does_not_start_overlapping_cycle_and_stop_is_bounded()
    {
        var calls=0;
        var blocked=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timedOut=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker=new ResilientWorker(_=>{Interlocked.Increment(ref calls);return blocked.Task;},
            TimeSpan.FromMilliseconds(20),TimeSpan.FromMilliseconds(60),TimeProvider.System,
            code=>{if(code=="cycle_timeout")timedOut.TrySetResult();});
        await worker.StartAsync(default);
        try
        {
            Assert.Same(timedOut.Task,await Task.WhenAny(timedOut.Task,Task.Delay(TimeSpan.FromSeconds(3))));
            await Task.Delay(100);
            Assert.Equal(1,calls);
            var observed=System.Diagnostics.Stopwatch.StartNew();
            var stopping=worker.StopAsync(default);
            try {await stopping.WaitAsync(TimeSpan.FromSeconds(3));}
            catch(TimeoutException)
            {
                Assert.Fail($"stop_wait_timeout; elapsedMs={observed.ElapsedMilliseconds}; " +
                    $"stopTask={stopping.Status}; cycleTask={blocked.Task.Status}; calls={Volatile.Read(ref calls)}");
            }
        }
        finally {blocked.TrySetResult();}
    }
    [Fact]
    public async Task Incident_publication_is_local_durable_deduplicated_and_shadow_audited()
    {
        await using var fixture=await OperatingModeTests.Fixture.Create();
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        try
        {
            var notifications=new SqliteNotificationStore(path);
            var publisher=new IncidentPublisher(fixture.Store,notifications,new());
            var incidents=await fixture.Store.ReadIncidentHeadsAsync(default);
            await publisher.PublishAsync(incidents,DateTimeOffset.UtcNow,default);
            await publisher.PublishAsync(incidents,DateTimeOffset.UtcNow,default);
            var notice=Assert.Single(await notifications.ReadDueAsync(DateTimeOffset.UtcNow,default));
            Assert.Equal(DiagnosisSeverity.High,notice.Notification.Severity);
            Assert.Equal(incidents[0].IncidentId,notice.Notification.IncidentId);
            Assert.DoesNotContain("S-1-",notice.Notification.Message);
            await using var db=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=fixture.Path,Pooling=false}.ToString());
            await db.OpenAsync();
            using var command=db.CreateCommand();
            command.CommandText="SELECT COUNT(*) FROM audit_entries WHERE result='shadow_recommendation';";
            Assert.Equal(1L,await command.ExecuteScalarAsync());
        }
        finally {File.Delete(path);}
    }
    private sealed class FailEnqueueOnce(INotificationStore inner):INotificationStore
    {
        public ValueTask<bool> HasFailuresAsync(CancellationToken cancellationToken)=>inner.HasFailuresAsync(cancellationToken);
        public ValueTask EnqueueAsync(Notification notification,DateTimeOffset now,CancellationToken cancellationToken)=>throw new IOException("synthetic interruption");
        public ValueTask<NotificationRecord?> ReadAsync(string id,CancellationToken cancellationToken)=>inner.ReadAsync(id,cancellationToken);
        public ValueTask<IReadOnlyList<NotificationRecord>> ReadDueAsync(DateTimeOffset now,CancellationToken cancellationToken)=>inner.ReadDueAsync(now,cancellationToken);
        public ValueTask<bool> ClaimAsync(string id,CancellationToken cancellationToken)=>inner.ClaimAsync(id,cancellationToken);
        public ValueTask CompleteAsync(string id,DeliveryResult result,DateTimeOffset now,CancellationToken cancellationToken)=>inner.CompleteAsync(id,result,now,cancellationToken);
    }
}
