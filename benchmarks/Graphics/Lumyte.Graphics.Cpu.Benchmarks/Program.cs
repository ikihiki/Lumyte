using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Lumyte.Graphics.Cpu.Benchmarks;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;

IConfig config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.Default.WithId("ProductDefaults").WithLaunchCount(5).WithWarmupCount(5)
        .WithIterationCount(15).WithIterationTime(TimeInterval.FromMilliseconds(250))
        .WithOutlierMode(OutlierMode.DontRemove))
    .AddDiagnoser(MemoryDiagnoser.Default).AddExporter(JsonExporter.Full);
BenchmarkSwitcher.FromAssembly(typeof(RectangleBenchmarks).Assembly).Run(args, config);
