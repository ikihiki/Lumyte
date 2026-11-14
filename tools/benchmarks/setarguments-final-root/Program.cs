using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.DotNetCli;
using Lumyte.Graphics.FinalRoot.Benchmarks;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;

if (args.Contains("--verify", StringComparer.Ordinal))
{
    foreach (int count in new[] { 1, 64 })
    {
        var benchmark = new FinalRootBenchmarks { Count = count };
        await benchmark.Setup();
        try
        {
            // Call the actual benchmark entry points, then read back in separate submissions.
            for (int repeat = 0; repeat < 2; repeat++)
            {
                await benchmark.InterfaceSetDispatchComplete();
                await benchmark.VerifyInterfaceAsync();
                await benchmark.ExplicitSnapshotSetDispatchComplete();
                await benchmark.VerifySnapshotsAsync();
            }
            Console.WriteLine($"PASS Count={count}: interface and explicit snapshot, repeated completed submissions; wire=20, native capacity=32.");
        }
        finally { benchmark.Cleanup(); }
    }
    return;
}

IConfig config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.Default.WithId("FinalRoot-GpuCompleted")
        .WithCustomBuildConfiguration("Release")
        .WithToolchain(new Toolchain("ExplicitFinalRootProject", new ExplicitProjectGenerator(), new DotNetCliBuilder("net10.0"), new DotNetCliExecutor(null!)))
        .WithLaunchCount(1).WithMinWarmupCount(5).WithMaxWarmupCount(10)
        .WithMinIterationCount(10).WithMaxIterationCount(15)
        .WithIterationTime(TimeInterval.FromMilliseconds(200))
        .WithOutlierMode(OutlierMode.DontRemove))
    .AddColumn(StatisticColumn.Median, StatisticColumn.P95)
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddExporter(JsonExporter.Full);
BenchmarkSwitcher.FromTypes([typeof(FinalRootBenchmarks)]).Run(args, config);
