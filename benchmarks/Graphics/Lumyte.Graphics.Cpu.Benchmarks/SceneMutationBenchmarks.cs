using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.BenchmarkWorkloads;

namespace Lumyte.Graphics.Cpu.Benchmarks;

/// <summary>Public placement and bounded structural roundtrip CPU editing, without GPU or reference generation.</summary>
public class SceneMutationBenchmarks
{
    private SceneMutationFixture _fixture = null!;
    private int _frame;

    /// <summary>Gets or sets the selected scenario.</summary>
    [Params("D03", "D11")]
    public string Scenario { get; set; } = "D03";

    /// <summary>Gets or sets the placement update rule; only relevant to D03.</summary>
    [Params("parent", "individual")]
    public string Placement { get; set; } = "parent";

    /// <summary>Gets or sets a value indicating whether D03 content is shared.</summary>
    [Params(true, false)]
    public bool Shared { get; set; }

    /// <summary>Creates the public scene outside the timed operation.</summary>
    [GlobalSetup]
    public void Setup() => _fixture = new(Scenario, Placement, Shared);

    /// <summary>Measures a successful public edit step of the deterministic roundtrip trace.</summary>
    /// <returns>The number of observed successful edit calls.</returns>
    [Benchmark]
    public int Edit()
    {
        _fixture.Apply(++_frame);
        return _fixture.PropertyEdits + _fixture.StructuralEdits;
    }

    /// <summary>Releases the scene outside timing.</summary>
    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();
}
