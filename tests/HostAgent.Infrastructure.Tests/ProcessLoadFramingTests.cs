using System.Text.Json;
using HostAgent.Core.Dashboard;
using HostAgent.Core.Models.Diagnosis;
using HostAgent.Core.Models.Incidents;
using HostAgent.Core.Models.Telemetry;
using HostAgent.Core.Orchestration;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Service;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class ProcessLoadFramingTests
{
    // Reproduce the independent QA fixture: disjoint CPU and RAM leaders and
    // every bounded overview section filled with long, valid values.
    [Theory]
    [InlineData(96)]
    [InlineData(128)]
    public async Task Full_overview_stays_connected_and_process_load_is_independent(int incidentWidth)
    {
        const int nameWidth=64;
        var clock=TimeProvider.System;
        var sample=clock.GetUtcNow().AddSeconds(-1);
        var started=sample.AddHours(-2);
        await using var fixture=await OperatingModeTests.Fixture.Create();
        var store=fixture.Store;
        var activity = Enumerable.Range(1, 20).Select(i => new
        {
            ProcessId = 5000 + i,
            ProcessName = Name("Process", i, nameWidth),
            ExecutablePath = (string?)null,
            CpuUsagePercent = i <= 10 ? i / 3.14159 : 80 + (i - 10) / 3.14159,
            MemoryBytes = i <= 10 ? 8_400_000_000 + i : 24_000_000 + i,
            StartedAtUtc = started.AddSeconds(-i),
            CpuIdentityVerified = true
        }).ToArray();
        var services = Enumerable.Range(1, 30)
            .Select(i => new ServiceTelemetry(Name("Service", i, nameWidth), "Fixture service",
                HostServiceStatus.Running, HostServiceStartMode.Automatic, false)).ToArray();
        await Seed("process-services", new { Processes = activity, Services = services, ServiceChanges = Array.Empty<ServiceStateChange>() });
        await Seed("cpu-memory", new CpuMemoryTelemetry(32.163434, 16_000_000_000, 9_200_000_000, 24_000_000_000,
            12_000_000_000, [], []));
        var disks = Enumerable.Range(0, 8)
            .Select(i => new DiskVolumeTelemetry($"{(char)('C' + i)}:\\", "Volume", "NTFS",
                2_000_000_000_000, 700_000_000_000, 1_300_000_000_000, 35)).ToArray();
        await Seed("disk", new DiskTelemetry(disks, null, []));
        for (var i = 0; i < 7; ++i)
        {
            foreach (var metric in new[] { "cpu.usage.percent", "memory.used.percent" })
                await store.SaveMetricSampleAsync(new(Guid.NewGuid(), metric, "host", sample.AddMinutes(-i),
                    3.14159265 + i / 3.14159265), default);
        }
        for (var i = 1; i <= 20; ++i)
        {
            var id = i.ToString("x32");
            var at = sample.AddMinutes(-i);
            var key = $"qa-service-{i:D2}";
            var diagnosis = new Diagnosis(id, key, at, DiagnosisSeverity.Warning, 90, [],
                new("service_stopped", "Fixture service stopped", Name("Service", i, incidentWidth)), [],
                new("inspect", "Fixture"), IncidentLifecycleState.Active);
            await store.SaveIncidentBundleAsync(new(id, key, IncidentLifecycleState.Active, at, at, 1, diagnosis, []), default);
        }

        var health = new AgentRuntimeHealth();
        health.Set("collection_worker", false);
        health.Set("diagnostics", false);
        health.Set("publication", false);
        health.Set("administration", false);
        health.SetCollectors([
            new("system", CollectorCycleStatus.Succeeded, null, 0, null),
            new("cpu-memory", CollectorCycleStatus.Succeeded, null, 0, null),
            new("disk", CollectorCycleStatus.Partial, null, 0, null) { WarningCodes = ["disk_io_counters_unavailable", "smart_health_unavailable"] },
            new("process-services", CollectorCycleStatus.Partial, null, 0, null)
                { WarningCodes = ["some_executable_paths_unavailable", "some_service_triggers_unavailable"] },
            new("network", CollectorCycleStatus.Succeeded, null, 0, null),
            new("event-log", CollectorCycleStatus.Succeeded, null, 0, null)
        ]);
        var dispatcher = new DashboardDispatcher(store, new(), health, clock);

        var overviewBytes=0;
        var loadBytes=0;
        var loadRequests=0;
        var failLoad=false;
        TaskCompletionSource<byte[]>? pauseNext=null;
        var framedOldLoad=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller=new DashboardController(Send);
        Assert.Throws<InvalidDataException>(()=>ViewProtocol.Request("process-load",new{limit=20}));
        await controller.RefreshAsync(default);
        Assert.Equal("connected",controller.State);
        var overview=Assert.IsType<DashboardSnapshot>(controller.Snapshot);
        Assert.InRange(overviewBytes,14_000,HelperProtocol.MaximumFrameBytes-1);
        Assert.Equal(10,overview.Processes.Count);
        Assert.Equal(30,overview.Services.Count);
        Assert.Equal(20,overview.Incidents.Count);
        Assert.Equal(8,overview.Disks.Count);
        Assert.InRange(overview.RecentCpu.Count,6,7);
        Assert.InRange(overview.RecentMemory.Count,6,7);
        Assert.Equal(0,loadRequests);
        Assert.Null(controller.ProcessLoad);

        Assert.True(await controller.RefreshProcessLoadAsync(default));
        var load=Assert.IsType<ViewProcessLoad>(controller.ProcessLoad);
        Assert.InRange(loadBytes,1,HelperProtocol.MaximumFrameBytes-1);
        Assert.Equal(10,load.TopCpu.Count);
        Assert.Equal(10,load.TopRam.Count);
        Assert.Equal(20,load.Sampled);
        Assert.Empty(load.TopCpu.Select(row=>row.Pid).Intersect(load.TopRam.Select(row=>row.Pid)));

        pauseNext=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate=pauseNext;
        var delayed=controller.RefreshProcessLoadAsync(default).AsTask();
        var delayedResponse=await framedOldLoad.Task.WaitAsync(TimeSpan.FromSeconds(5));
        failLoad=true;
        Assert.True(await controller.RefreshProcessLoadAsync(default));
        Assert.Same(overview,controller.Snapshot);
        Assert.Equal("connected",controller.State);
        Assert.Null(controller.ProcessLoad);

        gate.SetResult(delayedResponse);
        Assert.False(await delayed);
        Assert.Null(controller.ProcessLoad);
        Assert.Same(overview,controller.Snapshot);

        failLoad=false;
        pauseNext=new(TaskCreationOptions.RunContinuationsAsynchronously);
        gate=pauseNext;
        framedOldLoad=new(TaskCreationOptions.RunContinuationsAsynchronously);
        delayed=controller.RefreshProcessLoadAsync(default).AsTask();
        delayedResponse=await framedOldLoad.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await controller.RefreshAsync(default);
        Assert.NotSame(overview,controller.Snapshot);
        gate.SetResult(delayedResponse);
        Assert.False(await delayed);
        Assert.Equal("connected",controller.State);
        Assert.Null(controller.ProcessLoad);

        async ValueTask<byte[]> Send(byte[] request,CancellationToken token)
        {
            var command=ViewProtocol.Parse(request).Command;
            var pending=command=="process-load"?pauseNext:null;
            if(pending is not null)pauseNext=null;
            var response=command=="process-load"&&failLoad
                ? AdminProtocol.Reply(false,"view_request_rejected")
                : await dispatcher.DispatchAsync(request,token);
            using var stream=new MemoryStream();
            await HelperProtocol.WriteFrameAsync(stream,response,token);
            stream.Position=0;
            var framed=await HelperProtocol.ReadFrameAsync(stream,token);
            if(command=="overview")
            {
                overviewBytes=framed.Length;
                using var json=JsonDocument.Parse(framed);
                if(json.RootElement.GetProperty("ok").GetBoolean())
                    Assert.False(json.RootElement.GetProperty("data").TryGetProperty("ProcessLoad",out _));
            }
            else if(command=="process-load"){loadRequests++;loadBytes=framed.Length;}
            if(pending is null)return framed;
            framedOldLoad.SetResult(framed);
            return await pending.Task.WaitAsync(token);
        }

        async Task Seed(string collectorId,object value)
        {
            var payload=JsonSerializer.Serialize(new{CollectorId=collectorId,Status=0,Value=value,Warnings=Array.Empty<string>()});
            await store.SaveTelemetrySnapshotAsync(new(Guid.NewGuid(),collectorId,1,sample,payload),default);
        }
    }

    private static string Name(string prefix,int index,int length)=>$"{prefix}{index:D2}_".PadRight(length,'a');
}
