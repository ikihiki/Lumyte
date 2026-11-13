using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.UI.Tests;

/// <summary>Checks legacy UI validation boundaries after delegation to common color mathematics.</summary>
public sealed class ColorConsolidationTests
{
    /// <summary>Float alpha subtraction and float-valid overshoot remain observable through the public adapter.</summary>
    [Fact]
    public void AdapterRetainsMixedPrecisionAndFinalValidation()
    {
        Rgba<float> midpoint = UiAnimationInterpolators.Color.Interpolate(new(0, 0, 0, 0.1f), new(0, 0, 0, 0.9f), 0.5f);
        Assert.Equal(0.49999997f, midpoint.A);
        Rgba<float> overshoot = UiAnimationInterpolators.Color.Interpolate(new(0, 0, 0, 0.9f), new(0, 0, 0, 1), MathF.BitIncrement(1));
        Assert.Equal(1f, overshoot.A);
        Assert.Throws<ArgumentOutOfRangeException>(() => UiAnimationInterpolators.Color.Interpolate(default, new(1, 1, 1, 1), float.NaN));
    }

    /// <summary>Transparent colored endpoints are preserved, while interior zero alpha becomes black.</summary>
    [Fact]
    public void AdapterPreservesHiddenEndpointRgb()
    {
        Rgba<float> from = new(1, 0.5f, 0.25f, 0);
        Rgba<float> to = new(0.25f, 0.5f, 1, 0);
        Assert.Equal(from, UiAnimationInterpolators.Color.Interpolate(from, to, 0));
        Assert.Equal(to, UiAnimationInterpolators.Color.Interpolate(from, to, 1));
        Assert.Equal(default, UiAnimationInterpolators.Color.Interpolate(from, to, 0.5f));
    }

    /// <summary>Each raw finite out-of-unit component must be rejected before widget state changes.</summary>
    /// <param name="invalid">The finite component outside the unit interval.</param>
    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void WidgetsRejectInvalidComponentsAtomically(float invalid)
    {
        for (int component = 0; component < 4; component++)
        {
            float[] values = [0.25f, 0.5f, 0.75f, 0f];
            values[component] = invalid;
            var bad = new Rgba<float>(values[0], values[1], values[2], values[3]);
            Rgba<float> valid = new(0.1f, 0.2f, 0.3f, 0.4f);
            var rectangle = new Compose.Definitions.Rectangle { Fill = valid };
            var separator = new Compose.Definitions.Separator { Color = valid };
            Assert.Throws<ArgumentOutOfRangeException>(() => rectangle.Fill = bad);
            Assert.Throws<ArgumentOutOfRangeException>(() => separator.Color = bad);
            Assert.Equal(valid, rectangle.Fill);
            Assert.Equal(valid, separator.Color);
            Assert.Throws<ArgumentOutOfRangeException>(() => Compose.Rectangle(fill: bad));
            Assert.Throws<ArgumentOutOfRangeException>(() => Compose.Separator(color: bad));
            Assert.Throws<ArgumentOutOfRangeException>(() => Compose.Rectangle(with: [Compose.Border.Stroke(bad, 1)]));
            Assert.Throws<ArgumentOutOfRangeException>(() => UiAnimationInterpolators.Color.Interpolate(bad, valid, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => UiAnimationInterpolators.Color.Interpolate(valid, bad, 0));
        }
    }
}
