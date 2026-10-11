using System.Reflection;
using HostAgent.Core.Models.Telemetry;
using Xunit;

namespace HostAgent.Core.Tests;

public sealed class BaselineEngineTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Full_baseline_reports_hand_calculated_median_mad_percentile_and_anomaly()
    {
        var engineType = typeof(CollectorResult<>).Assembly.GetType("HostAgent.Core.Diagnostics.BaselineEngine");
        Assert.NotNull(engineType);

        var engine = Activator.CreateInstance(engineType!);
        Assert.NotNull(engine);
        var observe = engineType!.GetMethod("ObserveAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(observe);

        object? assessment = null;
        var history = new (int Day, double Value)[]
        {
            (0, 20),
            (1, 19),
            (2, 20),
            (3, 20),
            (4, 21),
            (5, 22)
        };

        foreach (var (day, value) in history)
        {
            assessment = await ObserveAsync(observe!, engine!, "cpu.usage.percent", "host", Start.AddDays(day), value);
        }

        assessment = await ObserveAsync(observe!, engine!, "cpu.usage.percent", "host", Start.AddDays(31), 50);

        Assert.Equal(20d, ReadDouble(assessment!, "Median"), 8);
        Assert.Equal(1d, ReadDouble(assessment!, "MedianAbsoluteDeviation"), 8);
        Assert.Equal(21.8d, ReadDouble(assessment!, "NinetyFifthPercentile"), 8);
        Assert.Equal("Full", ReadProperty(assessment!, "LearningMode")?.ToString());
        Assert.Equal("Anomaly", ReadProperty(assessment!, "AdaptiveSignal")?.ToString());
        Assert.Equal(5, ReadInt32(assessment!, "SampleCount"));
    }

    [Fact]
    public async Task Adaptive_learning_signal_moves_through_informational_reduced_and_full_modes()
    {
        var engineType = typeof(CollectorResult<>).Assembly.GetType("HostAgent.Core.Diagnostics.BaselineEngine");
        Assert.NotNull(engineType);
        var engine = Activator.CreateInstance(engineType!);
        Assert.NotNull(engine);
        var observe = engineType!.GetMethod("ObserveAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(observe);

        for (var day = 0; day <= 5; day++)
        {
            await ObserveAsync(observe!, engine!, "memory.used.percent", "host", Start.AddDays(day), 10);
        }

        var daySeven = await ObserveAsync(
            observe!, engine!, "memory.used.percent", "host", Start.AddDays(7), 80);
        var dayEight = await ObserveAsync(
            observe!, engine!, "memory.used.percent", "host", Start.AddDays(8), 80);
        var dayThirty = await ObserveAsync(
            observe!, engine!, "memory.used.percent", "host", Start.AddDays(30), 80);

        Assert.Equal("Informational", ReadProperty(daySeven!, "AdaptiveSignal")?.ToString());
        Assert.Equal("ReducedConfidence", ReadProperty(dayEight!, "LearningMode")?.ToString());
        Assert.Equal("ReducedConfidence", ReadProperty(dayThirty!, "LearningMode")?.ToString());

        var fullEngine = Activator.CreateInstance(engineType!);
        Assert.NotNull(fullEngine);
        for (var day = 0; day <= 30; day++)
        {
            await ObserveAsync(observe!, fullEngine!, "disk.used.percent", "C:\\", Start.AddDays(day), 10);
        }

        var dayThirtyOne = await ObserveAsync(
            observe!, fullEngine!, "disk.used.percent", "C:\\", Start.AddDays(31), 80);
        Assert.Equal("Full", ReadProperty(dayThirtyOne!, "LearningMode")?.ToString());
        Assert.True(ReadInt32(dayEight!, "Confidence") < ReadInt32(dayThirtyOne!, "Confidence"));
        Assert.Equal(30, ReadInt32(dayThirtyOne!, "SampleCount"));
    }

    private static async Task<object?> ObserveAsync(
        MethodInfo method,
        object engine,
        string metric,
        string scope,
        DateTimeOffset sampledAtUtc,
        double value)
    {
        var boxedValueTask = method.Invoke(engine, [metric, scope, sampledAtUtc, value, CancellationToken.None]);
        Assert.NotNull(boxedValueTask);
        var asTask = boxedValueTask!.GetType().GetMethod("AsTask", Type.EmptyTypes);
        Assert.NotNull(asTask);
        var task = Assert.IsAssignableFrom<Task>(asTask!.Invoke(boxedValueTask, null));
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    private static object? ReadProperty(object instance, string name) =>
        instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance);

    private static double ReadDouble(object instance, string name) =>
        Assert.IsType<double>(ReadProperty(instance, name));

    private static int ReadInt32(object instance, string name) =>
        Assert.IsType<int>(ReadProperty(instance, name));
}
