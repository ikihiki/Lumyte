using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Drawing2D;

// Same completed-frame fixture and assertion route; only explicit observation count varies.
public class OwnerAccountingFrameBenchmarks : RecordFrameBenchmarks
{
    [Params(0, 2)]
    public int Reads { get => StatisticsReads; set => StatisticsReads = value; }
}

// Diagnostic steady-state reads, not a completed frame or application FPS measurement.
public class OwnerAccountingStatisticsBenchmarks
{
    RecordFrameBenchmarks fixture = null!;

    [Params("rect1000-dynamic", "rect1000-cached")]
    public string Case { get; set; } = "rect1000-dynamic";

    [GlobalSetup]
    public async Task Setup()
    {
        fixture = new() { Case = Case };
        await fixture.Setup();
        for (int i = 0; i < 64; i++) await fixture.CompletedFrameAsync();
        Console.WriteLine($"idle assets={fixture.ObserveStatistics().CachedAssets}; estimated bytes={fixture.ObserveStatistics().CachedBytes}");
    }

    [Benchmark]
    public DrawingRetentionStatistics2D Read() => fixture.ObserveStatistics();

    [GlobalCleanup]
    public void Cleanup() => fixture.Cleanup();
}
