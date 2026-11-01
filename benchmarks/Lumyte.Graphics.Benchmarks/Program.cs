using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.DotNetCli;
using Lumyte.Graphics.Benchmarks;
using Perfolizer.Horology;

if (args.Contains("--extended", StringComparer.Ordinal))
{
    await ExtendedNativeStages.RunAsync();
    return;
}

if (args.Contains("--gpu", StringComparer.Ordinal))
{
    await NativeStages.RunAsync();
    return;
}

IConfig config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.Default.WithToolchain(new Toolchain("ExplicitProject", new ExplicitProjectGenerator(), new DotNetCliBuilder("net10.0"), new DotNetCliExecutor(null!))).WithLaunchCount(1).WithWarmupCount(3).WithIterationCount(12)
        .WithIterationTime(TimeInterval.FromMilliseconds(150)))
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddColumn(StatisticColumn.Median, StatisticColumn.P95)
    .AddExporter(JsonExporter.Full);
BenchmarkSwitcher.FromAssembly(typeof(RecordingBenchmarks).Assembly).Run(args, config);
