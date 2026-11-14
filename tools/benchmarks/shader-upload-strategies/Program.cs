using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.DotNetCli;
using Lumyte.Graphics.UploadStrategies.Benchmarks;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;

if (args.Contains("--verify", StringComparer.Ordinal))
{
    Console.WriteLine(await Correctness.RunAsync());
    return;
}

// Reuse the repository's BDN 0.15.8 configuration/explicit-project pattern.
// Six cases x one independent launch; the external campaign is frozen ABBA.
IConfig config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.Default.WithId("UploadStrategies-SteadyState")
        .WithToolchain(new Toolchain("ExplicitUploadStrategiesProject", new ExplicitProjectGenerator(), new DotNetCliBuilder("net10.0"), new DotNetCliExecutor(null!)))
        .WithLaunchCount(1).WithMinWarmupCount(5).WithMaxWarmupCount(10)
        .WithMinIterationCount(10).WithMaxIterationCount(15)
        .WithIterationTime(TimeInterval.FromMilliseconds(200))
        .WithOutlierMode(OutlierMode.DontRemove))
    .AddColumn(StatisticColumn.Median, StatisticColumn.P95)
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddExporter(JsonExporter.Full);
BenchmarkSwitcher.FromTypes([typeof(CpuBenchmarks), typeof(GpuBenchmarks)]).Run(args, config);
