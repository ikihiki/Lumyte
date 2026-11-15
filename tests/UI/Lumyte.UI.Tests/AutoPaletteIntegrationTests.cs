using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;
using Lumyte.Signals;
using Xunit;

namespace Lumyte.UI.Tests;

/// <summary>Exercises semantic palette conversion through real generated UI and animation consumers.</summary>
public sealed class AutoPaletteIntegrationTests
{
    /// <summary>Typed receiver properties enter generated public factories with one UiArgument conversion.</summary>
    [Fact]
    public void PropertiesAndGeneratedFactoriesAcceptNormalizedPalette()
    {
        SRgba<float> red = SRgba<float>.Red;
        var direct = new Compose.Definitions.Rectangle { Fill = SRgba<float>.Red };
        Assert.Equal(red, direct.Fill);
        Assert.Equal(red, Compose.Rectangle(fill: red).Fill);
        Assert.Equal(red, Compose.Rectangle(SRgba<float>.Red).Fill);
        var target = Signal.Create<SRgba<float>>(SRgba<float>.Red);
        Assert.Equal(red, Compose.Rectangle(fill: target).Fill);
        Assert.Equal(red, Compose.Rectangle(with: [Compose.Border.Stroke(SRgba<float>.Red, 1)]).BoxModifiers[0].Color);
        SolidPaintNode2D solid = Assert.IsType<SolidPaintNode2D>(PaintGraph2D.Solid(SRgba<float>.CornflowerBlue).Root);
        Assert.Equal(SRgba<float>.CornflowerBlue, solid.Color);
        Assert.Equal(new SRgba<float>(1, 128 / 255f, 0, 1), (SRgba<float>)new SRgba8(255, 128, 0));
    }

    /// <summary>Explicit generic types give endpoints, nullable keys and signals their intended normalized color context.</summary>
    [Fact]
    public void AnimationEndpointsAndNullableKeysUseTypedPalette()
    {
        var duration = Duration.FromSeconds(1);
        var tween = new Tween<SRgba<float>>(SRgba<float>.Red, SRgba<float>.Blue, duration, UiAnimationInterpolators.Color, AnimationEasing.Linear);
        Assert.Equal(SRgba<float>.Red, tween.Sample(Duration.Zero));
        Assert.Equal(SRgba<float>.Blue, tween.Sample(duration));
        var target = Signal.Create<SRgba<float>>(SRgba<float>.CornflowerBlue);
        UiAnimated<SRgba<float>> animated = Compose.Animate(target, duration, UiAnimationInterpolators.Color);
        Assert.Equal(SRgba<float>.CornflowerBlue, Compose.Rectangle(fill: animated).Fill);
        IAnimationInterpolator<SRgba<float>?> nullable = UiAnimationInterpolators.Nullable<SRgba<float>>(UiAnimationInterpolators.Color);
        var curve = new AnimationCurve<SRgba<float>?>(
            duration,
            [new AnimationKey<SRgba<float>?>(Duration.Zero, SRgba<float>.Red), new AnimationKey<SRgba<float>?>(duration, SRgba<float>.Blue)],
            nullable);
        Assert.Equal((SRgba<float>?)SRgba<float>.Red, curve.Sample(Duration.Zero));
        Assert.Equal((SRgba<float>?)SRgba<float>.Blue, curve.Sample(duration));
        var nullableTarget = Signal.Create<SRgba<float>?>(SRgba<float>.Red);
        Assert.Equal((SRgba<float>?)SRgba<float>.Red, Compose.Animate(nullableTarget, duration, nullable).Peek());
    }

    /// <summary>Typed palette admission does not relax raw negative/HDR rejection or failed-setter atomicity.</summary>
    /// <param name="invalid">The finite out-of-unit component.</param>
    [Theory]
    [InlineData(-1f)]
    [InlineData(2f)]
    public void RawColorsStillFailBeforeStateChanges(float invalid)
    {
        var rectangle = new Compose.Definitions.Rectangle { Fill = SRgba<float>.Red };
        var raw = new SRgba<float>(invalid, 0, 0, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => rectangle.Fill = raw);
        Assert.Equal(SRgba<float>.Red, rectangle.Fill);
        Assert.Throws<ArgumentOutOfRangeException>(() => Compose.Rectangle(fill: raw));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tween<SRgba<float>>(SRgba<float>.Red, raw, Duration.FromSeconds(1), UiAnimationInterpolators.Color, AnimationEasing.Linear));
    }
}
