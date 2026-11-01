using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Benchmarks;

/// <summary>Isolates immutable typed tape creation from command allocation and validation.</summary>
public class SnapshotBenchmarks
{
    private DrawingCommand2D[] _commands = null!;

    /// <summary>Gets or sets the snapshot size.</summary>
    [Params(100, 1000, 1024, 1025, 10000)]
    public int Count { get; set; }

    /// <summary>Creates immutable input outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var workload = new RecordingBenchmarks { Count = Count };
        _commands = workload.Rectangles().Commands.ToArray();
    }

    /// <summary>Copies and indexes the painter-order tape.</summary>
    /// <returns>The immutable snapshot.</returns>
    [Benchmark]
    public DrawList2D Snapshot() => new(_commands);
}
