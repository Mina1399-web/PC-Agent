using HostAgent.Infrastructure.Collectors;
using Xunit;

namespace HostAgent.Infrastructure.Tests;

public sealed class ProcessCpuIdentityTests
{
    [Fact]
    public void Recycled_pid_needs_new_baseline_and_zero_is_a_valid_rate()
    {
        var tracker=new ProcessCpuTracker();
        var time=new DateTimeOffset(2026,11,21,12,0,0,TimeSpan.Zero);
        var first=new ProcessTimeSample(time.AddDays(-1),TimeSpan.FromSeconds(10),false);
        Assert.Null(tracker.Record(42,first,time,4));
        Assert.Equal(0d,tracker.Record(42,first,time.AddSeconds(1),4));

        var replacement=new ProcessTimeSample(time.AddSeconds(1),TimeSpan.FromSeconds(30),false);
        Assert.Null(tracker.Record(42,replacement,time.AddSeconds(2),4));
        Assert.Equal(50d,tracker.Record(42,replacement with{CpuTime=TimeSpan.FromSeconds(32)},time.AddSeconds(3),4));
    }

    [Fact]
    public void Unknown_or_exited_process_forgets_its_previous_baseline()
    {
        var tracker=new ProcessCpuTracker();
        var time=new DateTimeOffset(2026,11,21,12,0,0,TimeSpan.Zero);
        var sample=new ProcessTimeSample(time.AddMinutes(-1),TimeSpan.FromSeconds(10),false);
        Assert.Null(tracker.Record(7,sample,time,2));
        Assert.Null(tracker.Record(7,new(null,sample.CpuTime,false),time.AddSeconds(1),2));
        Assert.Null(tracker.Record(7,sample,time.AddSeconds(2),2));
        Assert.Null(tracker.Record(7,sample with{Exited=true},time.AddSeconds(3),2));
        Assert.Null(tracker.Record(7,sample,time.AddSeconds(4),2));
        tracker.Retain([]);
        Assert.Null(tracker.Record(7,sample,time.AddSeconds(5),2));
    }

    [Fact]
    public void Native_times_reject_exited_and_unidentifiable_samples()
    {
        var start=new DateTimeOffset(2026,11,21,12,0,0,TimeSpan.Zero);
        var creation=start.ToFileTime();
        Assert.True(WindowsProcessCpuSample.FromTimes(creation,creation+1,5,7).Exited);
        Assert.Null(WindowsProcessCpuSample.FromTimes(0,0,5,7).StartedAt);
        Assert.Null(WindowsProcessCpuSample.FromTimes(creation,0,long.MaxValue,7).CpuTime);
        var observation=WindowsProcessCpuSample.FromTimes(creation,0,5,7);
        Assert.Equal(start,observation.StartedAt);
        Assert.Equal(TimeSpan.FromTicks(12),observation.CpuTime);
    }
}
