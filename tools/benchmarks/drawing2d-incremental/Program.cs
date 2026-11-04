using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.DotNetCli;
using Lumyte.Graphics.Incremental.Benchmarks;
using Perfolizer.Horology;

if (args.Contains("--public-retained-api", StringComparer.Ordinal) || args.Contains("--public-retained-ui-cpu", StringComparer.Ordinal))
{
    await PublicRetainedApiProbe.RunAsync(args.Contains("--public-retained-ui-cpu", StringComparer.Ordinal));
    return;
}

if (args.Contains("--production-retained-checks", StringComparer.Ordinal) || args.Contains("--production-retained-hardware", StringComparer.Ordinal))
{
    await ProductionRetainedProbe.RunAsync(args.Contains("--production-retained-checks", StringComparer.Ordinal));
    return;
}

if (args.Any(arg => arg.StartsWith("--workload-", StringComparison.Ordinal)))
{
    try
    {
        await RenderingWorkloadRunner.RunAsync(args);
    }
    catch (ArgumentException error)
    {
        Console.Error.WriteLine(error.Message);
        Environment.ExitCode = 2;
    }
    catch (FormatException error)
    {
        Console.Error.WriteLine(error.Message);
        Environment.ExitCode = 2;
    }

    return;
}

if (args.Contains("--direct-checks", StringComparer.Ordinal))
{
    DirectRectangleChecks.Run();
    return;
}

if (args.Contains("--direct-hardware", StringComparer.Ordinal))
{
    await DirectHardwareProbe.RunAsync();
    return;
}

if (args.Contains("--smoke", StringComparer.Ordinal))
{
    PrototypeChecks.RunChecks();
    ComparisonChecks.Run();
    return;
}

if (args.Contains("--retention", StringComparer.Ordinal))
{
    RetentionProbe.Run();
    return;
}

if (args.Contains("--software-integration", StringComparer.Ordinal))
{
    await SoftwareIntegrationProbe.RunAsync();
    return;
}

if (args.Contains("--hardware-integration", StringComparer.Ordinal))
{
    await HardwareIntegrationProbe.RunAsync();
    return;
}

IConfig config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.Default.WithId(Environment.GetEnvironmentVariable("DRAWING_BASELINE") ?? "comparison")
        .WithToolchain(new Toolchain("ExplicitProject", new ExplicitProjectGenerator(), new DotNetCliBuilder("net10.0"), new DotNetCliExecutor(null!)))
        .WithLaunchCount(1).WithWarmupCount(3).WithIterationCount(8)
        .WithOutlierMode(Perfolizer.Mathematics.OutlierDetection.OutlierMode.DontRemove)
        .WithIterationTime(TimeInterval.FromMilliseconds(150)))
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddColumn(StatisticColumn.Median, StatisticColumn.P95)
    .AddExporter(JsonExporter.Full);
BenchmarkSwitcher.FromAssembly(typeof(UpdateBenchmarks).Assembly).Run(args, config);
