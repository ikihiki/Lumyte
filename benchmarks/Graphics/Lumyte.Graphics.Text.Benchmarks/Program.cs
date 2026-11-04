using System.Text.Json;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.DotNetCli;
using Lumyte.Graphics.Text.Benchmarks;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;

try
{
    string mode = CliOptions.Validate(args);
    if (mode == "--cpu")
    {
        TextChecks.Require(File.Exists(TextCase.FontPath("DejaVuSans.ttf")), "Required DejaVu Latin/Arabic asset is missing.");
        foreach (TextCase input in TextCase.All.Where(input => !input.Available))
        {
            Console.WriteLine(JsonSerializer.Serialize(new { input.Id, status = "asset-gap", font = input.AdditionalFont, reason = "Bundled font absent; excluded from CPU parameter source, not unsupported API." }));
        }

        IConfig config = ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(Job.Default.WithId("D07-ProductDefaults")
                .WithToolchain(new Toolchain("ExplicitProject", new ExplicitProjectGenerator(), new DotNetCliBuilder("net10.0"), new DotNetCliExecutor(null!)))
                .WithLaunchCount(5).WithWarmupCount(5).WithIterationCount(15)
                .WithIterationTime(TimeInterval.FromMilliseconds(250)).WithOutlierMode(OutlierMode.DontRemove))
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddColumn(StatisticColumn.Median, StatisticColumn.P95)
            .AddExporter(JsonExporter.Full);
        BenchmarkSwitcher.FromAssembly(typeof(TextBenchmarks).Assembly).Run(args.Where(argument => argument != "--cpu").ToArray(), config);
        return;
    }

    if (mode is "--list" or "--check-layouts")
    {
        TextChecks.Require(File.Exists(TextCase.FontPath("DejaVuSans.ttf")), "Required DejaVu Latin/Arabic asset is missing.");
        foreach (TextCase input in TextCase.All)
        {
            if (!input.Available)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { input.Id, status = "asset-gap", font = input.AdditionalFont, reason = "Bundled asset absent; not unsupported API." }));
            }
            else if (mode == "--check-layouts")
            {
                Console.WriteLine(JsonSerializer.Serialize(TextChecks.Verify(input), TextChecks.Json));
            }
            else
            {
                Console.WriteLine(JsonSerializer.Serialize(new { input, status = "available", methods = new[] { "vector", "msdf", "mtsdf" } }));
            }
        }

        return;
    }

    if (mode is "--smoke" or "--run")
    {
        await TextGpuRunner.RunAsync(args);
        return;
    }

    throw new ArgumentException("Choose --list, --check-layouts, --cpu [BDN options], --smoke, or --run --case ID --method vector|msdf|mtsdf --source COMMIT [--depth 1|3 --frames N --warmup N --output PATH].");
}
catch (Exception error)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "failed", error = error.ToString() }));
    Environment.ExitCode = 1;
}
