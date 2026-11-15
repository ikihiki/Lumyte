using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.DotNetCli;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;
namespace BackendNativeWriters;
internal static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Contains("--verify"))
        {
            foreach (int count in new[] { 1, 64 })
            {
                using var fixture = await Fixture.Create(count);
                fixture.SetRecordCpu();
                await fixture.FullCompletion();
                await fixture.AssertOutput();
                await fixture.Map();
                fixture.CopyFromCpu();
                fixture.AssertMetadata();
                Console.WriteLine($"PASS Count={count}: generated root two refs; real completed dispatch/readback; discarded recording; mapped CopyFrom metadata");
            }
            return;
        }
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(Job.Default.WithId("Bounded-Native-Writers").WithCustomBuildConfiguration("Release")
                .WithToolchain(new Toolchain("ExplicitProject", new ExplicitProjectGenerator(), new DotNetCliBuilder("net10.0"), new DotNetCliExecutor(null!)))
                .WithLaunchCount(1).WithWarmupCount(2).WithIterationCount(5)
                .WithIterationTime(TimeInterval.FromMilliseconds(100)).WithOutlierMode(OutlierMode.DontRemove))
            .AddDiagnoser(MemoryDiagnoser.Default).AddColumn(StatisticColumn.Median, StatisticColumn.P95)
            .AddExporter(JsonExporter.Full);
        BenchmarkSwitcher.FromTypes([typeof(SetBenchmarks), typeof(CopyBenchmarks)]).Run(args, config);
    }
}
internal sealed class ExplicitProjectGenerator() : CsProjGenerator("net10.0", null!, null!, null!)
{
    protected override FileInfo GetProjectFilePath(Type target, ILogger logger)
        => new(Environment.GetEnvironmentVariable("BACKEND_WRITERS_PROJECT") ?? throw new InvalidOperationException("Set BACKEND_WRITERS_PROJECT to the frozen external csproj."));
}
public class SetBenchmarks
{
    private Fixture _fixture = null!;
    [Params(1, 64)] public int Count { get; set; }
    [GlobalSetup] public async Task Setup() { _fixture = await Fixture.Create(Count); await _fixture.FullCompletion(); await _fixture.AssertOutput(); }
    [Benchmark] public void SetRecordCpu() => _fixture.SetRecordCpu();
    [Benchmark] public Task FullCompletion() => _fixture.FullCompletion();
    [GlobalCleanup] public void Cleanup() => _fixture.Dispose();
}
public class CopyBenchmarks
{
    private Fixture _fixture = null!;
    [GlobalSetup] public async Task Setup() { _fixture = await Fixture.Create(64); await _fixture.FullCompletion(); await _fixture.AssertOutput(); await _fixture.Map(); }
    [Benchmark] public void CopyFromCpu() => _fixture.CopyFromCpu();
    [GlobalCleanup] public void Cleanup() => _fixture.Dispose();
}
