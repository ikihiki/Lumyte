using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.DotNetCli;
using Lumyte.Graphics.RenderGraph.Benchmarks;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;

if (args.Contains("--smoke", StringComparer.Ordinal))
{
    GraphChecks.Run();
    return;
}

IConfig config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.Default.WithId("G04-ProductDefaults")
        .WithToolchain(new Toolchain("ExplicitProject", new ExplicitProjectGenerator(), new DotNetCliBuilder("net10.0"), new DotNetCliExecutor(null!)))
        .WithLaunchCount(5).WithWarmupCount(5).WithIterationCount(15)
        .WithIterationTime(TimeInterval.FromMilliseconds(250)).WithOutlierMode(OutlierMode.DontRemove))
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddColumn(StatisticColumn.Median, StatisticColumn.P95)
    .AddExporter(JsonExporter.Full);
BenchmarkSwitcher.FromAssembly(typeof(GraphBenchmarks).Assembly).Run(args, config);
