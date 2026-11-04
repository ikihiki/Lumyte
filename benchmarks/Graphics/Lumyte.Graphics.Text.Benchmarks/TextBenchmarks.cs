using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.Text.Benchmarks;

/// <summary>Real native Parley shaping/paragraph layout and managed snapshot creation.</summary>
public class TextBenchmarks
{
    private TextFixture _fixture = null!;
    private int _state;

    /// <summary>Gets cases whose explicit font assets are present.</summary>
    public static IEnumerable<string> Cases => TextCase.All.Where(input => input.Available).Select(input => input.Id);

    /// <summary>Gets or sets the bounded logical text trace.</summary>
    [ParamsSource(nameof(Cases))]
    public string Case { get; set; } = "fixed";

    /// <summary>Registers fonts and verifies glyph/paragraph semantics outside timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var input = TextCase.Find(Case);
        TextChecks.Verify(input);
        _fixture = new TextFixture(input);
    }

    /// <summary>Shapes and lays out a real state; width trace calls the actual finite-width paragraph provider.</summary>
    /// <returns>The independent immutable vector snapshot.</returns>
    [Benchmark]
    public TextLayout2D ShapeLayoutAndSnapshot()
    {
        _state ^= 1;
        return _fixture.Layout(_state);
    }

    /// <summary>Releases native engine/font state outside timing.</summary>
    [GlobalCleanup]
    public void Cleanup() => _fixture?.Dispose();
}
