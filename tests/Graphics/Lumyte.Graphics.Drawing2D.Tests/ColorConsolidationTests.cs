using System.Numerics;
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
            var color = new Color2D(value, value, value, 0.9f);
            float expected = (value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f)) * color.A;
            Assert.Equal(new Vector4(expected, expected, expected, color.A), DrawingProgram2D.Linear(color));
        }
    }
}
