using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.BenchmarkWorkloads;

namespace Lumyte.Graphics.Cpu.Benchmarks;

/// <summary>Managed-only recording and public Scene editing; excludes GPU preparation.</summary>
public class RectangleBenchmarks
{
    private RectangleFixture _fixture = null!;
    private int _frame;

    /// <summary>Gets or sets content commands per node.</summary>
    [Params(1, 16, 64, 256, 4096)]
    public int Granularity { get; set; }

    /// <summary>Gets or sets the actual changed logical element count profile.</summary>
    [Params("static", "one", "1%", "10%", "all")]
    public string Update { get; set; } = "static";

    /// <summary>Gets or sets the locality.</summary>
    [Params("contiguous", "dispersed")]
    public string Locality { get; set; } = "contiguous";

    /// <summary>Gets or sets a value indicating whether to use public Scene retention.</summary>
    [Params(false, true)]
    public bool Retained { get; set; }

    /// <summary>Creates inputs and initial immutable recordings outside measurements.</summary>
    [GlobalSetup]
    public void Setup() => _fixture = new(new(Update, Locality), Granularity, Retained);

    /// <summary>Measures reused static recording or full Canvas / affected-node Canvas plus ReplaceContent.</summary>
    /// <returns>The last operation's actual recording command count.</returns>
    [Benchmark]
    public int RecordAndPublish()
    {
        _frame ^= 1;
        _fixture.Apply(_frame);
        return _fixture.RerecordedCommands;
    }

    /// <summary>Releases the scene outside measurements.</summary>
    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();
}
