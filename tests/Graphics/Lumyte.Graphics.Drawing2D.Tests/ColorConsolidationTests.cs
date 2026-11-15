using System.Numerics;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks that the real paint compiler preserves its original single-precision calculation.</summary>
public sealed class ColorConsolidationTests
{
    /// <summary>Threshold neighbors and ordinary channels match the fixed baseline expression.</summary>
    [Fact]
    public void PaintCompilerMatchesOriginalFloatExpression()
    {
        float[] values = [0, float.Epsilon, MathF.BitDecrement(0.04045f), 0.04045f, MathF.BitIncrement(0.04045f), 0.1f, 0.5f, 1];
        foreach (float value in values)
        {
            var color = new SRgba<float>(value, value, value, 0.9f);
            float expected = (value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f)) * color.A;
            Assert.Equal(new Vector4(expected, expected, expected, color.A), DrawingProgram2D.Linear(color));
        }
    }

    /// <summary>Drawing signatures admit encoded values, not linear storage with identical components.</summary>
    [Fact]
    public void DrawingBoundariesUseEncodedSpaceAndLinearStorageKeepsHdr()
    {
        Assert.Equal(typeof(SRgba<float>), typeof(ColorStop2D).GetProperty(nameof(ColorStop2D.Color))!.PropertyType);
        Assert.Equal(typeof(SRgba<float>), typeof(SolidPaintNode2D).GetProperty(nameof(SolidPaintNode2D.Color))!.PropertyType);
        Assert.Equal(typeof(SRgba<float>), typeof(RenderTarget2D).GetProperty(nameof(RenderTarget2D.ClearColor))!.PropertyType);
        Assert.Equal(typeof(SRgba<float>), typeof(DrawingTargetInfo2D).GetProperty(nameof(DrawingTargetInfo2D.ClearColor))!.PropertyType);
        Assert.Null(typeof(PaintGraph2D).GetMethod(nameof(PaintGraph2D.Solid), [typeof(LRgba<float>)]));

        var encodedHdr = new SRgba<float>(-1, 2, 4, 0.5f);
        Assert.Equal(4, encodedHdr.B);
        Assert.Throws<ArgumentOutOfRangeException>(() => PaintGraph2D.Solid(encodedHdr));
        var linearHdr = new LRgba<float>(-1, 2, 4, 0.5f);
        Assert.Equal(new LRgba<float>(-0.5f, 1, 2, 0.5f), linearHdr.PremultiplyLinear<float>());
    }
}
