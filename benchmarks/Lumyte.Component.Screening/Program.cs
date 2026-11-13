using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Lumyte.Signals.Benchmarks;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;

if (args.Contains("--smoke", StringComparer.Ordinal))
{
    var scalar = new ScalarBenchmarks();
    if (scalar.ReadValue() != 42 || scalar.ReadPeek() != 42)
    {
        throw new InvalidOperationException("Scalar smoke failed.");
    }

    scalar.WriteSame();
    scalar.WriteUnobserved();
    var graph = new GraphBenchmarks();
    graph.Setup();
    try
    {
        if (graph.DiamondUpdate() != 4 || graph.SwitchBranch() != 1 || !graph.CheckReadSet())
        {
            throw new InvalidOperationException("Graph smoke failed.");
        }

        _ = graph.CaptureEightReads();
    }
    finally
    {
        graph.Cleanup();
    }

    var asynchronous = new AsyncBenchmarks();
    asynchronous.Setup();
    try
    {
        if (asynchronous.CreateCompleted() != 42 || asynchronous.ReloadCompleted() != 42
            || asynchronous.ReloadCompletedObserved() != 42 || asynchronous.SupersedeAndCancelPending() != 2)
        {
            throw new InvalidOperationException("Async smoke failed.");
        }
    }
    finally
    {
        asynchronous.Cleanup();
    }

    Console.WriteLine("screening-smoke-ok:v1:12");
    return;
}

IConfig config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.Default
        .WithId("ComponentScreening-v1")
        .WithLaunchCount(1)
        .WithWarmupCount(3)
        .WithIterationCount(8)
        .WithIterationTime(TimeInterval.FromMilliseconds(150))
        .WithOutlierMode(OutlierMode.DontRemove))
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddExporter(JsonExporter.Full);

BenchmarkSwitcher.FromAssembly(typeof(ScalarBenchmarks).Assembly).Run(args, config);
