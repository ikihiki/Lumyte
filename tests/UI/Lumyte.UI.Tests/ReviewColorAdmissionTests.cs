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
        var target = Signal.Create(new SRgba<float>(0, 0, 0, 1));
        UiAnimated<SRgba<float>> value = Compose.Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Color);
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
        SRgba<float> valid = new(0, 0, 0, 1);
        SRgba<float> invalid = new(2, 0, 0, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tween<SRgba<float>>(invalidLast ? valid : invalid, invalidLast ? invalid : valid, Duration.FromSeconds(1), UiAnimationInterpolators.Color, AnimationEasing.Linear));
    }

    /// <summary>A failed UI timeline initial-value registration must leave the channel available for a valid registration.</summary>
    [Fact]
    public void ClipRejectsInvalidInitialValueBeforeChannelRegistration()
    {
        var channel = AnimationChannel<SRgba<float>>.Create();
        UiAnimationClip clip = Compose.Animate(new AnimationTimelineBuilder().Build());
        Assert.Throws<ArgumentOutOfRangeException>(() => clip.Value(channel, new SRgba<float>(2, 0, 0, 1)));
        SRgba<float> valid = new(0, 0, 0, 1);
        Assert.Equal(valid, clip.Value(channel, valid).Peek());
    }

    /// <summary>Held and exact keyframe endpoints also require unit validation at admission.</summary>
    [Fact]
    public void ColorCurveRejectsKeyframeRegistration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationCurve<SRgba<float>>(
            Duration.FromSeconds(1),
            [new(Duration.Zero, new(2, 0, 0, 1))],
            UiAnimationInterpolators.Color));
    }

    /// <summary>Bezier wrappers must retain the borrowed color operation's endpoint contract.</summary>
    [Fact]
    public void BezierColorWrapperRejectsStoredEndpoints()
    {
        SRgba<float> valid = new(0, 0, 0, 1);
        SRgba<float> invalid = new(2, 0, 0, 1);
        IAnimationInterpolator<SRgba<float>> wrapped = AnimationInterpolators.CubicBezier(valid, valid, UiAnimationInterpolators.Color);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tween<SRgba<float>>(invalid, valid, Duration.FromSeconds(1), wrapped, AnimationEasing.Linear));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tween<SRgba<float>>(valid, invalid, Duration.FromSeconds(1), wrapped, AnimationEasing.Linear));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnimationCurve<SRgba<float>>(Duration.FromSeconds(1), [new(Duration.Zero, invalid)], wrapped));
    }

    /// <summary>Both Bezier controls require operation-specific validation before wrapper publication.</summary>
    [Fact]
    public void BezierColorWrapperRejectsInvalidControls()
    {
        SRgba<float> valid = new(0, 0, 0, 1);
        SRgba<float> invalid = new(2, 0, 0, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => AnimationInterpolators.CubicBezier(invalid, valid, UiAnimationInterpolators.Color));
        Assert.Throws<ArgumentOutOfRangeException>(() => AnimationInterpolators.CubicBezier(valid, invalid, UiAnimationInterpolators.Color));
    }

    /// <summary>Generic object-valued UI data is not implicitly interpreted as a normalized color projection.</summary>
    [Fact]
    public void BoxedRawHdrDataDoesNotAcquireColorPropertySemantics()
    {
        object raw = new LRgba<float>(-1, 2, 4, 3);
        var target = Signal.Create(raw);
        UiAnimated<object> projection = Compose.Animate(target, Duration.FromSeconds(1), AnimationInterpolators.Step<object>());
        Assert.Same(raw, projection.Peek());
    }

    /// <summary>Linear HDR data remains generic even when declared as a concrete color value.</summary>
    [Fact]
    public void LinearHdrDataDoesNotAcquireEncodedColorAdmission()
    {
        LRgba<float> raw = new(-1, 2, 4, 3);
        var target = Signal.Create(raw);
        UiAnimated<LRgba<float>> projection = Compose.Animate(target, Duration.FromSeconds(1), AnimationInterpolators.Step<LRgba<float>>());
        Assert.Equal(raw, projection.Peek());
        var channel = AnimationChannel<LRgba<float>>.Create();
        UiAnimationClip clip = Compose.Animate(new AnimationTimelineBuilder().Build());
        Assert.Equal(raw, clip.Value(channel, raw).Peek());
    }

    /// <summary>Boxed encoded storage is also data, not a declared encoded-color projection.</summary>
    [Fact]
    public void BoxedEncodedRawDataDoesNotAcquireColorAdmission()
    {
        object raw = new SRgba<float>(-1, 2, 4, 3);
        var target = Signal.Create(raw);
        UiAnimated<object> projection = Compose.Animate(target, Duration.FromSeconds(1), AnimationInterpolators.Step<object>());
        Assert.Same(raw, projection.Peek());
    }

    private static SRgba<float> Invalid(int component, float invalid)
    {
        float[] channels = [0.25f, 0.5f, 0.75f, 1];
        channels[component] = invalid;
        return new(channels[0], channels[1], channels[2], channels[3]);
    }
}
