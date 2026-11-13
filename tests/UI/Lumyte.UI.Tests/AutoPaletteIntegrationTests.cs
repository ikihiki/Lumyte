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
    /// <summary>Properties accept one conversion; generated arguments use a typed value or explicit constant boundary.</summary>
    [Fact]
    public void PropertiesAndGeneratedFactoriesAcceptNormalizedPalette()
    {
        SrgbRgba<float> red = CssColors.Red;
        var direct = new Compose.Definitions.Rectangle { Fill = CssColors.Red };
        Assert.Equal(red, direct.Fill);
        Assert.Equal(red, Compose.Rectangle(fill: red).Fill);
        Assert.Equal(red, Compose.Rectangle(fill: UiArgument<SrgbRgba<float>>.Constant(CssColors.Red)).Fill);
        var target = Signal.Create<SrgbRgba<float>>(CssColors.Red);
        Assert.Equal(red, Compose.Rectangle(fill: target).Fill);
        Assert.Equal(red, Compose.Rectangle(with: [Compose.Border.Stroke(CssColors.Red, 1)]).BoxModifiers[0].Color);
        SolidPaintNode2D solid = Assert.IsType<SolidPaintNode2D>(PaintGraph2D.Solid(CssColors.CornflowerBlue).Root);
        Assert.Equal((SrgbRgba<float>)CssColors.CornflowerBlue, solid.Color);
        Assert.Equal(new SrgbRgba<float>(1, 128 / 255f, 0, 1), (SrgbRgba<float>)new SrgbRgba8(255, 128, 0));
    }

    /// <summary>Explicit generic types give endpoints, nullable keys and signals their intended normalized color context.</summary>
    [Fact]
    public void AnimationEndpointsAndNullableKeysUseAutomaticNormalization()
    {
        var duration = Duration.FromSeconds(1);
        var tween = new Tween<SrgbRgba<float>>(CssColors.Red, CssColors.Blue, duration, UiAnimationInterpolators.Color, AnimationEasing.Linear);
        Assert.Equal((SrgbRgba<float>)CssColors.Red, tween.Sample(Duration.Zero));
        Assert.Equal((SrgbRgba<float>)CssColors.Blue, tween.Sample(duration));
        var target = Signal.Create<SrgbRgba<float>>(CssColors.CornflowerBlue);
        UiAnimated<SrgbRgba<float>> animated = Compose.Animate(target, duration, UiAnimationInterpolators.Color);
        Assert.Equal((SrgbRgba<float>)CssColors.CornflowerBlue, Compose.Rectangle(fill: animated).Fill);
        IAnimationInterpolator<SrgbRgba<float>?> nullable = UiAnimationInterpolators.Nullable<SrgbRgba<float>>(UiAnimationInterpolators.Color);
        var curve = new AnimationCurve<SrgbRgba<float>?>(
            duration,
            [new AnimationKey<SrgbRgba<float>?>(Duration.Zero, CssColors.Red), new AnimationKey<SrgbRgba<float>?>(duration, CssColors.Blue)],
            nullable);
        Assert.Equal((SrgbRgba<float>?)CssColors.Red, curve.Sample(Duration.Zero));
        Assert.Equal((SrgbRgba<float>?)CssColors.Blue, curve.Sample(duration));
        var nullableTarget = Signal.Create<SrgbRgba<float>?>(CssColors.Red);
        Assert.Equal((SrgbRgba<float>?)CssColors.Red, Compose.Animate(nullableTarget, duration, nullable).Peek());
    }

    /// <summary>Automatic palette admission does not relax raw negative/HDR rejection or failed-setter atomicity.</summary>
    /// <param name="invalid">The finite out-of-unit component.</param>
    [Theory]
    [InlineData(-1f)]
    [InlineData(2f)]
    public void RawColorsStillFailBeforeStateChanges(float invalid)
    {
        var rectangle = new Compose.Definitions.Rectangle { Fill = CssColors.Red };
        var raw = new SrgbRgba<float>(invalid, 0, 0, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => rectangle.Fill = raw);
        Assert.Equal((SrgbRgba<float>)CssColors.Red, rectangle.Fill);
        Assert.Throws<ArgumentOutOfRangeException>(() => Compose.Rectangle(fill: raw));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tween<SrgbRgba<float>>(CssColors.Red, raw, Duration.FromSeconds(1), UiAnimationInterpolators.Color, AnimationEasing.Linear));
    }
}
