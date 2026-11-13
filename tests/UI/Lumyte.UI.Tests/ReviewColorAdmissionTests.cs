using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Mathematics.Colors;
using Lumyte.Signals;
using Xunit;

namespace Lumyte.UI.Tests;

/// <summary>Independent regressions for unit-color animation admission.</summary>
public sealed class ReviewColorAdmissionTests
{
    /// <summary>All finite non-unit components must fail before creating a display projection.</summary>
    /// <param name="component">The RGBA component index.</param>
    /// <param name="invalid">The finite component outside [0,1].</param>
    [Theory]
    [InlineData(0, -0.1f)]
    [InlineData(0, 1.1f)]
    [InlineData(1, -0.1f)]
    [InlineData(1, 1.1f)]
    [InlineData(2, -0.1f)]
    [InlineData(2, 1.1f)]
    [InlineData(3, -0.1f)]
    [InlineData(3, 1.1f)]
    public void ColorProducerRejectsInitialTargetBeforePublication(int component, float invalid)
    {
        var target = Signal.Create(Invalid(component, invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => Compose.Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Color));
    }

    /// <summary>A detached projection must not return an invalid updated model value.</summary>
    [Fact]
    public void ColorProducerRejectsDetachedTargetUpdate()
    {
        var target = Signal.Create(new Rgba<float>(0, 0, 0, 1));
        UiAnimated<Rgba<float>> value = Compose.Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Color);
        target.Value = new(2, 0, 0, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => value.Peek());
    }

    /// <summary>The UI color interpolator must validate both endpoints at tween registration, not only interior sampling.</summary>
    /// <param name="invalidLast">Whether the second endpoint is invalid.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColorTweenRejectsEndpointRegistration(bool invalidLast)
    {
        Rgba<float> valid = new(0, 0, 0, 1);
        Rgba<float> invalid = new(2, 0, 0, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tween<Rgba<float>>(invalidLast ? valid : invalid, invalidLast ? invalid : valid, Duration.FromSeconds(1), UiAnimationInterpolators.Color, AnimationEasing.Linear));
    }

    /// <summary>A failed UI timeline initial-value registration must leave the channel available for a valid registration.</summary>
    [Fact]
    public void ClipRejectsInvalidInitialValueBeforeChannelRegistration()
    {
        var channel = AnimationChannel<Rgba<float>>.Create();
        UiAnimationClip clip = Compose.Animate(new AnimationTimelineBuilder().Build());
        Assert.Throws<ArgumentOutOfRangeException>(() => clip.Value(channel, new Rgba<float>(2, 0, 0, 1)));
        Rgba<float> valid = new(0, 0, 0, 1);
        Assert.Equal(valid, clip.Value(channel, valid).Peek());
    }

    /// <summary>Held and exact keyframe endpoints also require unit validation at admission.</summary>
    [Fact]
    public void ColorCurveRejectsKeyframeRegistration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationCurve<Rgba<float>>(
            Duration.FromSeconds(1),
            [new(Duration.Zero, new(2, 0, 0, 1))],
            UiAnimationInterpolators.Color));
    }

    /// <summary>Bezier wrappers must retain the borrowed color operation's endpoint contract.</summary>
    [Fact]
    public void BezierColorWrapperRejectsStoredEndpoints()
    {
        Rgba<float> valid = new(0, 0, 0, 1);
        Rgba<float> invalid = new(2, 0, 0, 1);
        IAnimationInterpolator<Rgba<float>> wrapped = AnimationInterpolators.CubicBezier(valid, valid, UiAnimationInterpolators.Color);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tween<Rgba<float>>(invalid, valid, Duration.FromSeconds(1), wrapped, AnimationEasing.Linear));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tween<Rgba<float>>(valid, invalid, Duration.FromSeconds(1), wrapped, AnimationEasing.Linear));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationCurve<Rgba<float>>(Duration.FromSeconds(1), [new(Duration.Zero, invalid)], wrapped));
    }

    /// <summary>Both Bezier controls require operation-specific validation before wrapper publication.</summary>
    [Fact]
    public void BezierColorWrapperRejectsInvalidControls()
    {
        Rgba<float> valid = new(0, 0, 0, 1);
        Rgba<float> invalid = new(2, 0, 0, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => AnimationInterpolators.CubicBezier(invalid, valid, UiAnimationInterpolators.Color));
        Assert.Throws<ArgumentOutOfRangeException>(() => AnimationInterpolators.CubicBezier(valid, invalid, UiAnimationInterpolators.Color));
    }

    /// <summary>Generic object-valued UI data is not implicitly interpreted as a normalized color projection.</summary>
    [Fact]
    public void BoxedRawHdrDataDoesNotAcquireColorPropertySemantics()
    {
        object raw = new Rgba<float>(-1, 2, 4, 3);
        var target = Signal.Create(raw);
        UiAnimated<object> projection = Compose.Animate(target, Duration.FromSeconds(1), AnimationInterpolators.Step<object>());
        Assert.Same(raw, projection.Peek());
    }

    private static Rgba<float> Invalid(int component, float invalid)
    {
        float[] channels = [0.25f, 0.5f, 0.75f, 1];
        channels[component] = invalid;
        return new(channels[0], channels[1], channels[2], channels[3]);
    }
}
