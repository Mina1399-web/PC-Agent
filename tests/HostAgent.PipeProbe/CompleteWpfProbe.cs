using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Desktop;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Storage;
using HostAgent.Service;
using Microsoft.Data.Sqlite;
using WpfButton=System.Windows.Controls.Button;
using WpfText=System.Windows.Controls.TextBlock;

public sealed record CompleteWpfProof(bool PartialCleared, bool FreshMetrics, bool StaleCleared,
    int OutageRecoveryCycles, double MinimumOutageSeconds, bool ActionsAlwaysDisabled, bool WindowClosed,
    bool QuickCheckHealthy);

// DEV-only, real MainWindow -> ViewPipeClient -> authenticated private Windows
// pipe -> DashboardDispatcher -> SqliteHostAgentStore. No production endpoint,
// helper, service configuration, outbound sender or user database is consulted.
public static class CompleteWpfProbe
{
    public static bool Accept(CompleteWpfProof proof) => proof.PartialCleared && proof.FreshMetrics
        && proof.StaleCleared && proof.OutageRecoveryCycles>=3
        && double.IsFinite(proof.MinimumOutageSeconds) && proof.MinimumOutageSeconds>=10
        && proof.ActionsAlwaysDisabled && proof.WindowClosed && proof.QuickCheckHealthy;

    public static async Task<int> RunAsync(string[] args)
    {
        if(args.Length!=2 || args[0]!="isolated-complete-wpf"
            || !Guid.TryParseExact(args[1],"N",out var id) || id==Guid.Empty)return 2;
        CompleteProbeGuard.ValidateVm();
        var directory=CompleteProbeGuard.NewDirectory(id,"complete-wpf");
        var state=new ProbeState();
        using var lifetime=new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var guard=Task.Run(async()=>
        {
            try
            {
                while(!lifetime.IsCancellationRequested)
                {
                    CompleteProbeGuard.CheckLimits();
                    state.MinimumFreeMiB=Math.Min(state.MinimumFreeMiB,CompleteProbeGuard.FreeMiB());
                    await Task.Delay(250,lifetime.Token);
                }
            }
            catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
            catch {state.ResourceGuardFailed=true;lifetime.Cancel();}
        });
        var completion=new TaskCompletionSource<CompleteWpfProof>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiThread=new Thread(()=>
        {
            var dispatcher=Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async()=>
            {
                try {completion.TrySetResult(await RunWindowAsync(id,directory,state,lifetime.Token));}
                catch(Exception error){completion.TrySetException(error);}
                finally {dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}
            }));
            Dispatcher.Run();
        }){IsBackground=true,Name="HostAgent isolated WPF integration"};
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        try
        {
            var proof=await completion.Task.WaitAsync(TimeSpan.FromSeconds(185));
            if(!uiThread.Join(TimeSpan.FromSeconds(5)))throw new TimeoutException("wpf_dispatcher_shutdown");
            var ok=Accept(proof)&&!state.ResourceGuardFailed;
            var result=new {scenario="isolated-complete-wpf",runId=id.ToString("N"),ok,
                fixtureDirectory=directory,proof,readOnlyOperationsOnly=true,
                productionTransport=true,productionDispatcher=true,disposableSqlite=true,
                clientRequests=state.ClientRequests,clientDeadlineCount=state.ClientDeadlineCount,
                completedStage=state.Stage,minimumFreeMiB=state.MinimumFreeMiB,
                resourceGuardFailed=state.ResourceGuardFailed};
            var json=JsonSerializer.Serialize(result);
            await File.WriteAllTextAsync(Path.Combine(directory,"result.json"),json);
            Console.WriteLine(json);
            return ok?0:1;
        }
        catch(Exception error)
        {
            lifetime.Cancel();
            // Never disclose exception messages, request bodies or database contents.
            var json=JsonSerializer.Serialize(new {scenario="isolated-complete-wpf",runId=id.ToString("N"),
                ok=false,stage=state.Stage,failureKind=error.GetType().Name,
                resourceGuardFailed=state.ResourceGuardFailed,fixtureDirectory=directory,
                clientRequests=state.ClientRequests,clientDeadlineCount=state.ClientDeadlineCount});
            await File.WriteAllTextAsync(Path.Combine(directory,"result.json"),json);
            Console.WriteLine(json);
            return 1;
        }
        finally
        {
            lifetime.Cancel();
            await guard.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private static async Task<CompleteWpfProof> RunWindowAsync(Guid id,string directory,ProbeState state,CancellationToken token)
    {
        var database=Path.Combine(directory,"host-agent.db");
        await using var store=new SqliteHostAgentStore(database);
        await store.InitializeAsync(token);
        await SeedAsync(store,false,token);
        var clock=new PrivateClock();
        var dispatcher=new DashboardDispatcher(store,new(),new(),clock);
        using var identity=WindowsIdentity.GetCurrent();
        var sid=identity.User??throw new InvalidOperationException("current_sid_required");
        var pipeName="HostAgent.Dev.Complete.Wpf."+id.ToString("N");
        var server=new AdminPipeServer(pipeName,sid,sid,async(payload,cancellation)=>
        {
            var command=ViewProtocol.Parse(payload).Command;
            if(command is not ("overview" or "history"))throw new InvalidDataException("read_only_probe_scope");
            return await dispatcher.DispatchAsync(payload,cancellation);
        },TimeSpan.FromSeconds(3));
        var client=new ViewPipeClient(pipeName,sid,TimeSpan.FromSeconds(1),diagnostic=>
        {
            if(diagnostic.Kind=="client_deadline")Interlocked.Increment(ref state.ClientDeadlineCount);
        });
        ValueTask<byte[]> Send(byte[] payload,CancellationToken cancellation)
        {
            // Input is never replaced with a fabricated reply. Every accepted
            // read goes through the real Windows transport and owner check.
            if(ViewProtocol.Parse(payload).Command is not ("overview" or "history"))
                throw new InvalidDataException("read_only_probe_scope");
            Interlocked.Increment(ref state.ClientRequests);
            return client.SendAsync(payload,cancellation);
        }
        var controller=new DashboardController(Send);
        DashboardWorker? worker=null;
        MainWindow? window=null;
        var actionsAlwaysDisabled=true;
        var windowClosed=false;
        var completedCycles=0;
        var minimumOutageSeconds=double.PositiveInfinity;
        var partial=false;var fresh=false;var stale=false;
        try
        {
            worker=await StartAsync(server,token);
            state.Stage="partial";
            window=new MainWindow(controller)
            {
                Title="Host Agent DEV — isolated synthetic-data resilience test",
                IsHitTestVisible=false
            };
            // This visible production window must never launch maintenance,
            // CLI, helper or settings actions during the operator campaign.
            window.PreviewKeyDown+=(_,e)=>e.Handled=true;
            window.PreviewTextInput+=(_,e)=>e.Handled=true;
            window.Closed+=(_,_)=>windowClosed=true;
            window.Show();
            await AwaitRefreshAsync(window,()=>controller.State=="connected",token);
            StopRefreshTimer(window);
            partial=controller.Snapshot?.Cpu?.Value==42 && controller.Snapshot.Memory is null
                && Text(window,"CpuText")=="42 %" && Text(window,"MemoryText")=="—";
            Require(partial,"partial_metrics");
            actionsAlwaysDisabled&=ActionDisabled(window);
            Report("partial",true);

            state.Stage="fresh";
            await SeedAsync(store,true,token);
            await AwaitRefreshAsync(window,()=>controller.Snapshot?.Memory?.Value==64,token);
            fresh=Text(window,"CpuText")=="42 %" && Text(window,"MemoryText")=="64 %"
                && Text(window,"UptimeText")=="2 h 00 m" && controller.Snapshot?.ReadOnly==true;
            Require(fresh,"fresh_metrics");
            actionsAlwaysDisabled&=ActionDisabled(window);
            Report("fresh",true);

            state.Stage="stale";
            clock.Offset=TimeSpan.FromMinutes(-10);
            await AwaitRefreshAsync(window,()=>controller.State=="stale",token);
            stale=controller.Snapshot is null && Cleared(window)
                && Text(window,"StatusText").Contains("zastaralá",StringComparison.Ordinal);
            Require(stale,"stale_clear");
            actionsAlwaysDisabled&=ActionDisabled(window);
            Report("stale",true);
            clock.Offset=TimeSpan.Zero;
            await AwaitRefreshAsync(window,()=>controller.Snapshot?.Memory?.Value==64,token);

            for(var cycle=1;cycle<=3;cycle++)
            {
                state.Stage="outage_"+cycle;
                await StopAsync(worker);worker=null;
                var outage=Stopwatch.StartNew();
                var failedRequestsBefore=state.ClientDeadlineCount;
                do
                {
                    token.ThrowIfCancellationRequested();
                    CompleteProbeGuard.CheckLimits();
                    await RefreshAsync(window).WaitAsync(TimeSpan.FromSeconds(5),token);
                    Require(controller.Snapshot is null && controller.State=="disconnected" && Cleared(window),"outage_clear");
                    actionsAlwaysDisabled&=ActionDisabled(window);
                    await Task.Delay(250,token);
                }while(outage.Elapsed<TimeSpan.FromSeconds(10));
                Require(state.ClientDeadlineCount>failedRequestsBefore,"real_transport_timeout_missing");
                minimumOutageSeconds=Math.Min(minimumOutageSeconds,outage.Elapsed.TotalSeconds);
                Report("outage_"+cycle,true);

                state.Stage="recovery_"+cycle;
                await SeedAsync(store,true,token);
                worker=await StartAsync(server,token);
                await AwaitRefreshAsync(window,()=>controller.Snapshot?.Cpu?.Value==42 && controller.Snapshot.Memory?.Value==64,token);
                Require(Text(window,"CpuText")=="42 %" && Text(window,"MemoryText")=="64 %","recovery_metrics");
                actionsAlwaysDisabled&=ActionDisabled(window);
                completedCycles++;
                Report("recovery_"+cycle,true);
            }
            state.Stage="shutdown";
        }
        finally
        {
            if(worker is not null)await StopAsync(worker);
            if(window is not null)
            {
                StopRefreshTimer(window);
                window.Close();
            }
        }
        await store.DisposeAsync();
        state.Stage="readonly_quick_check";
        var healthy=ReadOnlyQuickCheck(database);
        Require(healthy,"quick_check");
        var files=Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories).Select(x=>new FileInfo(x)).ToArray();
        Require(files.Length<=16 && files.Sum(x=>x.Length)<=32L*1024*1024,"fixture_size_limit");
        state.Stage="completed";
        return new(partial,fresh,stale,completedCycles,minimumOutageSeconds,actionsAlwaysDisabled,windowClosed,healthy);
    }

    internal static bool ReadOnlyQuickCheck(string database)
    {
        CompleteProbeGuard.AssertPlainPath(database);
        foreach(var suffix in new[]{"-wal","-journal"})
        {
            var sidecar=new FileInfo(database+suffix);
            if(sidecar.Exists && sidecar.Length!=0)throw new InvalidOperationException("nonempty_journal_readonly_check_rejected");
        }
        using var readLock=File.Open(database,FileMode.Open,FileAccess.Read,FileShare.Read);
        using var connection=new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource=new Uri(Path.GetFullPath(database)).AbsoluteUri+"?mode=ro&immutable=1",
            Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=2
        }.ToString());
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText="PRAGMA query_only=ON; PRAGMA temp_store=MEMORY;";command.ExecuteNonQuery();
        command.CommandText="PRAGMA quick_check;";
        using var reader=command.ExecuteReader();
        return reader.Read() && reader.GetString(0)=="ok" && !reader.Read();
    }

    private static async Task SeedAsync(SqliteHostAgentStore store,bool withMemory,CancellationToken token)
    {
        var at=DateTimeOffset.UtcNow.AddSeconds(-1);
        foreach(var (name,value) in new[]{("cpu.usage.percent",42d),("system.uptime.seconds",7200d)})
            await store.SaveMetricSampleAsync(new(Guid.NewGuid(),name,"host",at,value),token);
        if(withMemory)await store.SaveMetricSampleAsync(new(Guid.NewGuid(),"memory.used.percent","host",at,64),token);
    }
    private static async Task<DashboardWorker> StartAsync(AdminPipeServer server,CancellationToken token)
    {
        var worker=new DashboardWorker(server.ServeOnceAsync,retryDelay:TimeSpan.FromMilliseconds(100));
        await Task.Run(()=>worker.StartAsync(token),token);
        return worker;
    }
    private static async Task StopAsync(DashboardWorker worker)
    {
        try {await Task.Run(()=>worker.StopAsync(default)).WaitAsync(TimeSpan.FromSeconds(5));}
        finally {worker.Dispose();}
    }
    private static void StopRefreshTimer(MainWindow window)
        =>((DispatcherTimer)typeof(MainWindow).GetField("_timer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!).Stop();
    private static Task RefreshAsync(MainWindow window)
        =>(Task)typeof(MainWindow).GetMethod("Refresh",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)!;
    private static async Task AwaitRefreshAsync(MainWindow window,Func<bool> complete,CancellationToken token)
    {
        var deadline=Stopwatch.StartNew();
        do
        {
            token.ThrowIfCancellationRequested();
            await RefreshAsync(window).WaitAsync(TimeSpan.FromSeconds(5),token);
            // Loaded refresh may already hold the production window's gate.
            // Do not accept a controller state before that render/history
            // continuation has completed on this same STA dispatcher.
            var gate=(SemaphoreSlim)typeof(MainWindow).GetField("_reading",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            if(gate.CurrentCount==1 && complete())return;
            await Task.Delay(100,token);
        }while(deadline.Elapsed<TimeSpan.FromSeconds(10));
        throw new TimeoutException("wpf_refresh_assertion");
    }
    private static string Text(MainWindow window,string name)=>((WpfText)window.FindName(name)).Text;
    private static bool ActionDisabled(MainWindow window)=>!((WpfButton)window.FindName("OperationButton")).IsEnabled;
    private static bool Cleared(MainWindow window)=>Text(window,"CpuText")=="—" && Text(window,"MemoryText")=="—"
        && Text(window,"UptimeText")=="—" && Text(window,"IncidentCountText")=="—"
        && ((System.Windows.Controls.DataGrid)window.FindName("ProcessGrid")).Items.Count==0
        && ((System.Windows.Controls.DataGrid)window.FindName("ServicesGrid")).Items.Count==0;
    private static void Require(bool condition,string code)
    {if(!condition)throw new InvalidOperationException(code);}
    private static void Report(string stage,bool ok)=>Console.WriteLine(JsonSerializer.Serialize(new {scenario="isolated-complete-wpf",stage,ok}));
    private sealed class PrivateClock:TimeProvider
    {
        public TimeSpan Offset {get;set;}
        public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UtcNow+Offset;
    }
    private sealed class ProbeState
    {
        public volatile string Stage="initialization";
        public volatile bool ResourceGuardFailed;
        public double MinimumFreeMiB=double.PositiveInfinity;
        public int ClientRequests,ClientDeadlineCount;
    }
}
