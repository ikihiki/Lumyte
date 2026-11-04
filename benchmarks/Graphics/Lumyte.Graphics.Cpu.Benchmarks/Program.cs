using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Lumyte.Graphics.BenchmarkWorkloads;
using Lumyte.Graphics.Cpu.Benchmarks;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;

// A separate process path: no benchmark frames or forced GC in timed operations.
if (RetentionMemoryOptions.IsRequested(args))
{
    RetentionMemoryOptions memory;
    try
    {
        memory = RetentionMemoryOptions.Parse(args);
    }
    catch (ArgumentException error)
    {
        Console.Error.WriteLine(error.Message);
        Environment.ExitCode = 2;
        return;
    }

    bool[] paths = memory.Path == "all" ? [false, true] : [memory.Path == "scene"];
    int[] histories = memory.SnapshotHistory is int selectedHistory ? [selectedHistory] : [0, 1, 3];
    foreach (bool retained in paths)
    {
        foreach (int history in histories)
        {
            RetentionMemorySample[] observations = ManagedRetentionProbe.MeasureDetailed(retained, 64, history, memory.Cycles);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                schema = "graphics-managed-retention-v2",
                scenarioVersion = RectangleTrace.Version,
                retained,
                path = retained ? "scene" : "canvas",
                granularity = 64,
                snapshotHistory = history,
                cycles = memory.Cycles,
                observations,
                scope = "dedicated full-GC checkpoints: whole managed live heap and refreshed process RSS, not allocation/op or frame timing; RSS is not native allocation or VRAM",
                releaseContract = "fixture disposed inside NoInlining helper; helper returns before after-dispose full GC; GC.KeepAlive covers all owned observations",
                repetitionContract = "select one path/history per fresh process for independent repeats; default all-profile sweep shares runtime/JIT state",
                unavailableCounters = new { nativeAllocationBytes = (long?)null, nativeVramBytes = (long?)null, gpuAllocationBytes = (long?)null },
            }));
        }
    }

    return;
}

IConfig config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.Default.WithId("ProductDefaults").WithLaunchCount(5).WithWarmupCount(5)
        .WithIterationCount(15).WithIterationTime(TimeInterval.FromMilliseconds(250))
        .WithOutlierMode(OutlierMode.DontRemove))
    .AddDiagnoser(MemoryDiagnoser.Default).AddExporter(JsonExporter.Full);
BenchmarkSwitcher.FromAssembly(typeof(RectangleBenchmarks).Assembly).Run(args, config);
