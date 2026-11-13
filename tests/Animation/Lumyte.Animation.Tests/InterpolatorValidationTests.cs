using Lumyte.Core.Time;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Animation.Tests;

/// <summary>Checks optional operation-specific validation independently of stored numeric representations.</summary>
public sealed class InterpolatorValidationTests
{
    /// <summary>Both endpoints are validated before a tween can publish its held values.</summary>
    [Fact]
    public void TweenValidatesBothEndpointsBeforeStorage()
    {
        var interpolator = new NonnegativeInterpolator();
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tween<int>(-1, 1, Duration.FromSeconds(1), interpolator, AnimationEasing.Linear));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tween<int>(1, -1, Duration.FromSeconds(1), interpolator, AnimationEasing.Linear));
    }

    /// <summary>Default curve validation includes single, held and exact final keys.</summary>
    [Fact]
    public void CurveValidatesEveryStoredKey()
    {
        var interpolator = new NonnegativeInterpolator();
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationCurve<int>(Duration.FromSeconds(1), [new(Duration.Zero, -1)], interpolator));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationCurve<int>(Duration.FromSeconds(1), [new(Duration.Zero, 0) { Hold = true }, new(Duration.FromSeconds(1), -1)], interpolator));
    }

    /// <summary>Only an executing segment override validates its operands; outgoing flags on the final key remain ignored.</summary>
    [Fact]
    public void SegmentOverridesValidateBothOperandsWithoutActivatingIgnoredOverrides()
    {
        var strict = new NonnegativeInterpolator();
        var fallback = new LegacyIntInterpolator();
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationCurve<int>(Duration.FromSeconds(1), [new(Duration.Zero, 0) { Interpolator = strict }, new(Duration.FromSeconds(1), -1)], fallback));
        var curve = new AnimationCurve<int>(Duration.FromSeconds(1), [new(Duration.Zero, -1) { Hold = true, Interpolator = strict }, new(Duration.FromSeconds(1), -2) { Interpolator = strict }], fallback);
        Assert.Equal(-1, curve.Sample(Duration.Zero));
        Assert.Equal(-2, curve.Sample(Duration.FromSeconds(1)));
    }

    /// <summary>Existing interpolators need not implement validation, and common raw HDR values remain valid outside UI color operations.</summary>
    [Fact]
    public void DefaultHookPreservesLegacyImplementationsAndRawHdrStorage()
    {
        var raw = new Rgba<float>(-4, 8, 2, 3);
        var interpolator = new LegacyRawInterpolator();
        var tween = new Tween<Rgba<float>>(raw, raw, Duration.FromSeconds(1), interpolator, AnimationEasing.Linear);
        Assert.Equal(raw, tween.Sample(Duration.Zero));
        Assert.Equal(raw, tween.Sample(Duration.FromSeconds(0.5)));
        var curve = new AnimationCurve<Rgba<float>>(Duration.FromSeconds(1), [new(Duration.Zero, raw)], interpolator);
        Assert.Equal(raw, curve.Sample(Duration.FromSeconds(1)));
    }

    private sealed class NonnegativeInterpolator : IAnimationInterpolator<int>
    {
        public void Validate(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        public int Interpolate(int from, int to, float amount) => from;
    }

    private sealed class LegacyIntInterpolator : IAnimationInterpolator<int>
    {
        public int Interpolate(int from, int to, float amount) => from;
    }

    private sealed class LegacyRawInterpolator : IAnimationInterpolator<Rgba<float>>
    {
        public Rgba<float> Interpolate(Rgba<float> from, Rgba<float> to, float amount) => from;
    }
}
