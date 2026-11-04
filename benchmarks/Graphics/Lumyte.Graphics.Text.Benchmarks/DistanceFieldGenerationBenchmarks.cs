using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text.Benchmarks;

/// <summary>CPU-only real per-outline distance generation, distinct from GPU atlas creation/upload.</summary>
public class DistanceFieldGenerationBenchmarks
{
    private TextFixture _fixture = null!;
    private TextLayout2D[] _layouts = null!;
    private int _state;

    /// <summary>Gets bounded outline cases, excluding COLR-only paint graphs.</summary>
    public static IEnumerable<string> Cases => TextCase.All.Where(input => input.Available && input.Id != "colr").Select(input => input.Id);

    /// <summary>Gets or sets the shaped outline profile.</summary>
    [ParamsSource(nameof(Cases))]
    public string Case { get; set; } = "fixed";

    /// <summary>Gets or sets the actual distance representation.</summary>
    [Params(DistanceFieldMode2D.Msdf, DistanceFieldMode2D.Mtsdf)]
    public DistanceFieldMode2D Mode { get; set; }

    /// <summary>Shapes immutable input outside the distance-generation timing boundary.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var input = TextCase.Find(Case);
        TextChecks.Verify(input);
        _fixture = new TextFixture(input);
        try
        {
            _layouts = [_fixture.Layout(0), _fixture.Layout(1)];
            TextChecks.Require(_layouts.All(layout => layout.Geometry.Count > 0), "No real distance-field outlines.");
        }
        catch
        {
            _fixture.Dispose();
            throw;
        }
    }

    /// <summary>Generates all selected state's outline fields with the public managed generator.</summary>
    /// <returns>The actual generated RGBA byte count, consumed by BenchmarkDotNet.</returns>
    [Benchmark]
    public long GenerateOutlineFields()
    {
        _state ^= 1;
        long bytes = 0;
        foreach (TextGeometry2D geometry in _layouts[_state].Geometry)
        {
            DistanceFieldImage2D image = DistanceFieldGenerator2D.Generate(geometry.Path, pixelsPerUnit: 1, distanceRange: 8, mode: Mode);
            bytes += image.Pixels.Length;
        }

        return bytes;
    }

    /// <summary>Disposes the borrowed native shaping engine after all CPU operations.</summary>
    [GlobalCleanup]
    public void Cleanup() => _fixture?.Dispose();
}
