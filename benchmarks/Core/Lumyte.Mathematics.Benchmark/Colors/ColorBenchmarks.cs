using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Mathematics.Benchmark;

/// <summary>Color encoding, arithmetic, pixels and catalog lookup.</summary>
[BenchmarkCategory("Standalone", "Core", "Mathematics", "Colors", "Screening", "Comparison", "Diagnostic")]
public class ColorBenchmarks
{
    private readonly SRgba<float> _color = new(0.2f, 0.5f, 0.8f, 0.5f);
    private readonly LRgba<float> _premultiplied = new(0.1f, 0.25f, 0.4f, 0.5f);
    private readonly LRgba<float> _destination = new(0.3f, 0.1f, 0.2f, 0.5f);
    private readonly byte[] _bytes = [32, 128, 255, 128];

    /// <summary>Executes the Normalize color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public SRgba<float> Normalize() => new SRgba<byte>(32, 128, 255, 128).NormalizeUnorm<float>(255);

    /// <summary>Executes the Quantize color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public SRgba<byte> Quantize() => _color.QuantizeUnorm<byte>(255);

    /// <summary>Executes the Decode color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public LRgba<float> Decode() => _color.ToLinear<float>();

    /// <summary>Executes the Encode color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public SRgba<float> Encode() => _premultiplied.ToSrgb<float>();

    /// <summary>Executes the Grayscale color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public LRgba<float> Grayscale() => _premultiplied.ToLinearGrayscale<float>();

    /// <summary>Executes the Premultiply color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public LRgba<float> Premultiply() => _premultiplied.PremultiplyLinear<float>();

    /// <summary>Executes the Unpremultiply color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public LRgba<float> Unpremultiply() => _premultiplied.UnpremultiplyLinear<float>();

    /// <summary>Executes the SourceOver color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public LRgba<float> SourceOver() => _premultiplied.SourceOverLinearPremultiplied(_destination);

    /// <summary>Executes the Interpolate color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public SRgba<float> Interpolate() => _color.InterpolateSrgbLinearPremultiplied<float>(new SRgba<float>(0.3f, 0.1f, 0.2f, 0.5f), 0.375f);

    /// <summary>Executes the Matrix color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public LRgba<float> Matrix() => _premultiplied.ApplyMatrixLinearPremultiplied(new Matrix4x4(1.1f, 0, 0, 0, 0, 0.9f, 0, 0, 0, 0, 0.8f, 0, 0, 0, 0, 1), new Vector4(0.01f));

    /// <summary>Executes the PixelDecode color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public SRgba<float> PixelDecode() => SRgba<float>.DecodePixel(_bytes, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);

    /// <summary>Executes the PixelWrite color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public int PixelWrite() => _color.WritePixel(_bytes, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);

    /// <summary>Executes the Pack color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public RawRgb565 Pack() => RawRgb565.FromSrgb(new SRgba<float>(0.2f, 0.5f, 0.8f, 1));

    /// <summary>Executes the Unpack color operation.</summary>
    /// <returns>The consumed operation result.</returns>
    [Benchmark]
    [BenchmarkCategory("SteadyState")]
    public SRgba<float> Unpack() => new RawRgb565(0x7bef).InterpretSrgb<float>();
}
