using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.DotNetCli;
using Lumyte.Graphics.CursorMetadata.Benchmarks;
using Lumyte.Graphics.FinalRoot.Benchmarks;
using Lumyte.Graphics.UploadStrategies.Benchmarks;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;

if (args.Contains("--verify", StringComparer.Ordinal))
{
    foreach (int count in new[] { 1, 64 })
    {
        var root = new FinalRootBenchmarks { Count = count };
        await root.Setup();
        try
        {
            for (int repeat = 0; repeat < 2; repeat++)
            {
                await root.InterfaceSetDispatchComplete();
                await root.VerifyInterfaceAsync();
                await root.ExplicitSnapshotSetDispatchComplete();
                await root.VerifySnapshotsAsync();
            }
        }
        finally { root.Cleanup(); }

        var cpu = new CpuBenchmarks { Count = count };
        await cpu.Setup();
        try
        {
            for (int repeat = 0; repeat < 2; repeat++)
            {
                if (cpu.MappedReferenceWrite() != count)
                    throw new InvalidOperationException("CPU source count differs.");
                await cpu.VerifyLastWriteAsync();
            }
        }
        finally { cpu.Cleanup(); }

        var gpu = new GpuBenchmarks { Count = count };
        await gpu.Setup();
        try
        {
            for (int repeat = 0; repeat < 2; repeat++)
            {
                await gpu.ReferenceWriteCopyComplete();
                await gpu.VerifyLastWriteAsync();
            }
        }
        finally { gpu.Cleanup(); }
        Console.WriteLine($"PASS Count={count}: all four actual entry points twice; root wire=20; all numeric/matrix/vector fields and reference identities/readbacks verified.");
    }
    Console.WriteLine("PASS: 8 cases, 16 actual benchmark calls with per-call untimed GPU readback verification.");
    return;
}

IConfig config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.Default.WithId("CursorMetadata-SteadyState")
        .WithCustomBuildConfiguration("Release")
        .WithToolchain(new Toolchain("ExplicitCursorMetadataProject", new ExplicitProjectGenerator(), new DotNetCliBuilder("net10.0"), new DotNetCliExecutor(null!)))
        .WithLaunchCount(1).WithMinWarmupCount(5).WithMaxWarmupCount(10)
        .WithMinIterationCount(10).WithMaxIterationCount(15)
        .WithIterationTime(TimeInterval.FromMilliseconds(200))
        .WithOutlierMode(OutlierMode.DontRemove))
    .AddColumn(StatisticColumn.Median, StatisticColumn.P95)
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddExporter(JsonExporter.Full);
BenchmarkSwitcher.FromTypes([typeof(FinalRootBenchmarks), typeof(CpuBenchmarks), typeof(GpuBenchmarks)]).Run(args, config);
