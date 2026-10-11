using HostAgent.Core.Contracts;
using HostAgent.Core.Models.Diagnosis;
using HostAgent.Infrastructure.Storage;
using HostAgent.Infrastructure.Notifications;
using HostAgent.Infrastructure.Heartbeat;
using HostAgent.Service;
using Xunit;
namespace HostAgent.Infrastructure.Tests;
public sealed class NotificationOutboxTests
{
    [Fact]
    public async Task First_failed_send_degrades_concurrent_heartbeat_before_later_slow_send_finishes()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        var sender=new FailThenBlock();
        using var stop=new CancellationTokenSource();
        try
        {
            var now=DateTimeOffset.UtcNow;
            var store=new SqliteNotificationStore(path);
            await store.EnqueueAsync(new("a","i",DiagnosisSeverity.High,"Synthetic"),now,default);
            await store.EnqueueAsync(new("b","i",DiagnosisSeverity.High,"Synthetic"),now,default);
            var heartbeat=new FakeHeartbeat();
            var outbound=new OutboundCoordinator(store,sender,heartbeat);
            var sending=outbound.RunNotificationsAsync(now,stop.Token).AsTask();
            try
            {
                await sender.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await outbound.RunHeartbeatAsync(false,default);
                Assert.True(heartbeat.Last!.Degraded);
            }
            finally
            {
                stop.Cancel();
                try{await sending;}catch(OperationCanceledException){}
            }
        }
        finally{File.Delete(path);}
    }
    private sealed class FailThenBlock:INotificationSender
    {
        private int _calls;
        public TaskCompletionSource Blocked{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<DeliveryResult> SendAsync(Notification notification,CancellationToken cancellationToken)
        {
            if(++_calls==1)return new(DeliveryState.Failed,"push_rejected");
            Blocked.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan,cancellationToken);
            return new(DeliveryState.Sent,"synthetic");
        }
        public ValueTask<DeliveryResult> PollAsync(string receipt,CancellationToken cancellationToken)=>throw new NotSupportedException();
    }
    [Fact]
    public async Task Heartbeat_failure_is_persisted_as_redacted_local_health()
    {
        await using var fixture=await OperatingModeTests.Fixture.Create();
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        try
        {
            var outbound=new OutboundCoordinator(new SqliteNotificationStore(path),new LocalNotificationSender(),new RecoveringHeartbeat(),fixture.Store);
            await outbound.RunCycleAsync(DateTimeOffset.UtcNow,false,default);
            await using var db=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=fixture.Path,Pooling=false}.ToString());
            await db.OpenAsync();
            using var command=db.CreateCommand();
            command.CommandText="SELECT COUNT(*) FROM service_health WHERE component_id='outbound-heartbeat' AND status='Failed';";
            Assert.Equal(1L,await command.ExecuteScalarAsync());
        }
        finally{File.Delete(path);}
    }
    [Fact]
    public async Task Future_notification_schema_is_rejected_without_writing_old_version()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        try
        {
            await using(var connection=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString()))
            {
                await connection.OpenAsync();
                using var command=connection.CreateCommand();
                command.CommandText="CREATE TABLE notification_schema(version INTEGER PRIMARY KEY);INSERT INTO notification_schema VALUES(2);";
                await command.ExecuteNonQueryAsync();
            }
            await Assert.ThrowsAsync<NotSupportedException>(async()=>await new SqliteNotificationStore(path).EnqueueAsync(new("n","i",DiagnosisSeverity.High,"Synthetic"),DateTimeOffset.UtcNow,default));
        }
        finally{File.Delete(path);}
    }
    [Fact]
    public async Task Uncertain_history_still_degrades_after_process_restart()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        try
        {
            var store=new SqliteNotificationStore(path);
            await store.EnqueueAsync(new("n","i",DiagnosisSeverity.High,"Synthetic"),DateTimeOffset.UtcNow,default);
            Assert.True(await store.ClaimAsync("n",default));
            var heartbeat=new FakeHeartbeat();
            using var http=new HttpClient(new OutboundTransportTests.FakeHttp());
            var outbound=new OutboundCoordinator(new SqliteNotificationStore(path),
                new PushoverNotifier(http,new OutboundTransportTests.TestSecrets(),TimeSpan.FromSeconds(1)),heartbeat);
            await outbound.RunCycleAsync(DateTimeOffset.UtcNow.AddMinutes(1),false,default);
            Assert.True(heartbeat.Last!.Degraded);
        }
        finally{File.Delete(path);}
    }
    [Fact]
    public async Task Recovered_heartbeat_can_report_success_instead_of_permanent_fail_loop()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        try
        {
            var heartbeat=new RecoveringHeartbeat();
            var outbound=new OutboundCoordinator(new SqliteNotificationStore(path),new LocalNotificationSender(),heartbeat);
            await outbound.RunCycleAsync(DateTimeOffset.UtcNow,false,default);
            Assert.True(outbound.Degraded);
            await outbound.RunCycleAsync(DateTimeOffset.UtcNow.AddMinutes(1),false,default);
            Assert.False(heartbeat.Last!.Degraded);
            Assert.False(outbound.Degraded);
        }
        finally{File.Delete(path);}
    }
    private sealed class RecoveringHeartbeat:IHeartbeatSender
    {
        private int _calls;
        public AgentHealth? Last{get;private set;}
        public ValueTask<DeliveryResult> SendAsync(AgentHealth health,CancellationToken cancellationToken)
        {Last=health;return ValueTask.FromResult(new DeliveryResult(++_calls==1?DeliveryState.Failed:DeliveryState.Sent,"synthetic"));}
    }
    // A restart must not lose an emergency receipt or resend an uncertain prior attempt.
    [Fact]
    public async Task History_deduplicates_and_reopen_polls_durable_critical_receipt_then_stops_after_ack()
    {
        var path = Path.Combine(Path.GetTempPath(), "notification-" + Guid.NewGuid().ToString("N") + ".db");
        var now = DateTimeOffset.UtcNow;
        try
        {
            var store = new SqliteNotificationStore(path);
            var critical = new Notification("critical-1", "incident-1", DiagnosisSeverity.Critical, "Synthetic critical");
            await store.EnqueueAsync(critical, now, default);
            await store.EnqueueAsync(critical, now, default);
            await store.EnqueueAsync(new("info", "i", DiagnosisSeverity.Information, "Local"), now, default);
            var httpHandler = new OutboundTransportTests.FakeHttp();
            using var http = new HttpClient(httpHandler);
            var heartbeat = new FakeHeartbeat();
            var outbound = new OutboundCoordinator(store, new PushoverNotifier(http, new OutboundTransportTests.TestSecrets(), TimeSpan.FromSeconds(1)), heartbeat);
            await outbound.RunCycleAsync(now, false, default);
            Assert.Equal(1, httpHandler.Calls);
            Assert.Equal(DeliveryState.Local, (await store.ReadAsync("info", default))!.Result.State);
            var record = await new SqliteNotificationStore(path).ReadAsync("critical-1", default);
            Assert.NotNull(record);
            Assert.Equal(DeliveryState.PendingAcknowledgement, record.Result.State);
            Assert.Equal("syntheticreceipt", record.Result.Receipt);
            Assert.Equal(1, record.Attempts);
            httpHandler.Json = """{"status":1,"acknowledged":1,"expired":0}""";
            outbound = new OutboundCoordinator(new SqliteNotificationStore(path),
                new PushoverNotifier(http, new OutboundTransportTests.TestSecrets(), TimeSpan.FromSeconds(1)), heartbeat);
            await outbound.RunCycleAsync(now.AddMinutes(1), false, default);
            Assert.Equal(DeliveryState.Acknowledged, (await store.ReadAsync("critical-1", default))!.Result.State);
            await outbound.RunCycleAsync(now.AddMinutes(2), false, default);
            Assert.Equal(2, httpHandler.Calls);
            Assert.Contains("/receipts/", httpHandler.Url);
            Assert.False(outbound.Degraded);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task Claim_is_durable_exclusive_and_never_retried_after_crash()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            var store = new SqliteNotificationStore(path);
            await store.EnqueueAsync(new("n","i",DiagnosisSeverity.High,"Synthetic"), DateTimeOffset.UtcNow, default);
            Assert.True(await store.ClaimAsync("n", default));
            Assert.False(await new SqliteNotificationStore(path).ClaimAsync("n", default));
            Assert.Empty(await new SqliteNotificationStore(path).ReadDueAsync(DateTimeOffset.UtcNow.AddDays(1), default));
            Assert.Equal(DeliveryState.Uncertain, (await store.ReadAsync("n", default))!.Result.State);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task Rejected_push_is_bounded_to_three_attempts_and_degrades_heartbeat_without_throwing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            var now = DateTimeOffset.UtcNow;
            var store = new SqliteNotificationStore(path);
            await store.EnqueueAsync(new("n","i",DiagnosisSeverity.High,"Synthetic"), now, default);
            var handler = new OutboundTransportTests.FakeHttp { Status = System.Net.HttpStatusCode.ServiceUnavailable };
            using var http = new HttpClient(handler);
            var heartbeat = new FakeHeartbeat();
            var outbound = new OutboundCoordinator(store, new PushoverNotifier(http,new OutboundTransportTests.TestSecrets(),TimeSpan.FromSeconds(1)),heartbeat);
            for (var i = 0; i < 5; i++) await outbound.RunCycleAsync(now.AddHours(i), false, default);
            Assert.Equal(3, handler.Calls);
            Assert.Equal(3, (await store.ReadAsync("n", default))!.Attempts);
            Assert.True(outbound.Degraded);
            Assert.True(heartbeat.Last!.Degraded);
        }
        finally { File.Delete(path); }
    }
    internal sealed class FakeHeartbeat : IHeartbeatSender
    {
        public AgentHealth? Last { get; private set; }
        public ValueTask<DeliveryResult> SendAsync(AgentHealth health, CancellationToken cancellationToken)
        { Last = health; return ValueTask.FromResult(new DeliveryResult(DeliveryState.Sent,"heartbeat_sent")); }
    }
}
