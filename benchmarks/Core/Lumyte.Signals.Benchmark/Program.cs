using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Lumyte.Signals.Benchmark;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;

if (args.Contains("--retention", StringComparer.Ordinal))
{
    RetentionProbe.Run();
    return;
}

if (args.Contains("--screening-smoke", StringComparer.Ordinal))
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

    Console.WriteLine("screening-smoke-ok:v2:12");
    return;
}

bool screening = args.Contains("--screening", StringComparer.Ordinal);
args = args.Where(argument => argument != "--screening").ToArray();
Job job = Job.Default
    .WithId(screening ? "SignalsScreening-v2" : "Signals")
    .WithLaunchCount(1)
    .WithWarmupCount(3)
    .WithIterationCount(8)
    .WithIterationTime(TimeInterval.FromMilliseconds(150));
if (screening)
{
    job = job.WithOutlierMode(OutlierMode.DontRemove);
}

IConfig config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(job)
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddExporter(JsonExporter.Full, MarkdownExporter.GitHub);

BenchmarkSwitcher.FromAssembly(typeof(ScalarBenchmarks).Assembly).Run(args, config);
