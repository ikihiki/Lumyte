using Lumyte.Graphics.Drawing2D;
using Xunit;

namespace Lumyte.UI.Tests;

/// <summary>Checks legacy UI validation boundaries after delegation to common color mathematics.</summary>
public sealed class ColorConsolidationTests
{
    /// <summary>Float alpha subtraction and float-valid overshoot remain observable through the public adapter.</summary>
    [Fact]
    public void AdapterRetainsMixedPrecisionAndFinalValidation()
    {
        Color2D midpoint = UiAnimationInterpolators.Color.Interpolate(new(0, 0, 0, 0.1f), new(0, 0, 0, 0.9f), 0.5f);
        Assert.Equal(0.49999997f, midpoint.A);
        Color2D overshoot = UiAnimationInterpolators.Color.Interpolate(new(0, 0, 0, 0.9f), new(0, 0, 0, 1), MathF.BitIncrement(1));
        Assert.Equal(1f, overshoot.A);
        Assert.Throws<ArgumentOutOfRangeException>(() => UiAnimationInterpolators.Color.Interpolate(default, new(1, 1, 1, 1), float.NaN));
    }

    /// <summary>Transparent colored endpoints are preserved, while interior zero alpha becomes black.</summary>
    [Fact]
    public void AdapterPreservesHiddenEndpointRgb()
    {
        Color2D from = new(1, 0.5f, 0.25f, 0);
        Color2D to = new(0.25f, 0.5f, 1, 0);
        Assert.Equal(from, UiAnimationInterpolators.Color.Interpolate(from, to, 0));
        Assert.Equal(to, UiAnimationInterpolators.Color.Interpolate(from, to, 1));
        Assert.Equal(default, UiAnimationInterpolators.Color.Interpolate(from, to, 0.5f));
    }
}
