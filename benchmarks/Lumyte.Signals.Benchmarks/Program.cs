using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Lumyte.Signals.Benchmarks;
using Perfolizer.Horology;

if (args.Contains("--smoke", StringComparer.Ordinal))
{
    Smoke.Verify();
    Console.WriteLine("All benchmark workload checks passed.");
    return;
}

if (args.Contains("--retention", StringComparer.Ordinal))
{
    RetentionProbe.Run();
    return;
}

IConfig config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.Default
        .WithId("Signals")
        .WithLaunchCount(1)
        .WithWarmupCount(3)
        .WithIterationCount(8)
        .WithIterationTime(TimeInterval.FromMilliseconds(150)))
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddExporter(JsonExporter.Full, MarkdownExporter.GitHub);

BenchmarkSwitcher.FromAssembly(typeof(ScalarBenchmarks).Assembly).Run(args, config);
