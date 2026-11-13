using System.Numerics;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Separates raw finite storage from drawing admission.</summary>
public sealed class DrawingColorAdmissionTests
{
    /// <summary>Provides out-of-unit finite values for every channel.</summary>
    /// <returns>The channel and rejected value.</returns>
    public static IEnumerable<object[]> InvalidComponents()
    {
        for (int component = 0; component < 4; component++)
        {
            foreach (float invalid in new[] { -0.01f, 1.01f })
            {
                yield return [component, invalid];
            }
        }
    }

    /// <summary>Raw negative/HDR components are valid storage but rejected before publication.</summary>
    /// <param name="component">The channel.</param>
    /// <param name="invalid">The finite out-of-unit value.</param>
    [Theory]
    [MemberData(nameof(InvalidComponents))]
    public void RawStorageDoesNotBypassDrawingAdmission(int component, float invalid)
    {
        float[] values = [0.25f, 0.5f, 0.75f, 1];
        values[component] = invalid;
        var raw = new SrgbRgba<float>(values[0], values[1], values[2], values[3]);
        Assert.Equal(values, new[] { raw.R, raw.G, raw.B, raw.A });
        var valid = new SrgbRgba<float>(0.25f, 0.5f, 0.75f, 1);
        var stop = new ColorStop2D(0, valid);
        var node = new SolidPaintNode2D(valid);
        Assert.Throws<ArgumentOutOfRangeException>(() => PaintGraph2D.Solid(raw));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColorStop2D(0, raw));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColorStop2D { Color = raw });
        Assert.Throws<ArgumentOutOfRangeException>(() => stop with { Color = raw });
        Assert.Throws<ArgumentOutOfRangeException>(() => new SolidPaintNode2D(raw));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SolidPaintNode2D(valid) { Color = raw });
        Assert.Throws<ArgumentOutOfRangeException>(() => node with { Color = raw });
        var target = new RenderTarget2D { View = null!, ClearColor = valid };
        var info = new DrawingTargetInfo2D { Description = null!, ClearColor = valid };
        var binding = new RenderTargetBinding2D { Description = null!, Texture = null!, ClearColor = valid };
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderTarget2D { View = null!, ClearColor = raw });
        Assert.Throws<ArgumentOutOfRangeException>(() => target with { ClearColor = raw });
        Assert.Throws<ArgumentOutOfRangeException>(() => info with { ClearColor = raw });
        Assert.Throws<ArgumentOutOfRangeException>(() => binding with { ClearColor = raw });
        Assert.Equal(valid, target.ClearColor);
        Assert.Equal(valid, info.ClearColor);
        Assert.Equal(valid, binding.ClearColor);
        Assert.Equal(valid, node.Color);
        Assert.Equal(valid, stop.Color);
        var canvas = new Canvas2D();
        canvas.Begin();
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.FillRect(new Rect2D(0, 0, 1, 1), raw));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.FillRoundedRect(new Rect2D(0, 0, 1, 1), 1, raw));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.FillCircle(Vector2.Zero, 1, raw));
        Path2D path = new PathBuilder2D().Build();
        var style = new StrokeStyle2D();
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.DrawLine(Vector2.Zero, Vector2.One, raw, style));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.FillPath(path, raw));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.StrokePath(path, raw, style));
        Assert.Empty(canvas.End().Commands);
    }

    /// <summary>Nonfinite raw values remain rejected by the mathematical constructor.</summary>
    [Fact]
    public void NonfiniteStorageIsRejectedInEveryChannel()
    {
        for (int component = 0; component < 4; component++)
        {
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                float[] values = [0, 0, 0, 1];
                values[component] = invalid;
                Assert.Throws<ArgumentOutOfRangeException>(() => new SrgbRgba<float>(values[0], values[1], values[2], values[3]));
            }
        }
    }

    /// <summary>Finite stop offsets allow duplicates and extrapolation, but factories reject descending order.</summary>
    [Fact]
    public void StopOffsetsValidateBeforePublication()
    {
        var stop = new ColorStop2D(-2, default);
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ColorStop2D(invalid, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ColorStop2D { Offset = invalid });
            Assert.Throws<ArgumentOutOfRangeException>(() => stop with { Offset = invalid });
        }

        Assert.Equal(-2, stop.Offset);
        ColorStop2D[] stops = [stop, stop, new(2, default)];
        Assert.NotNull(PaintGraph2D.LinearGradient(Vector2.Zero, Vector2.One, stops));
        Assert.NotNull(PaintGraph2D.RadialGradient(Vector2.Zero, 1, stops));
        Assert.NotNull(PaintGraph2D.SweepGradient(Vector2.Zero, 0, 1, stops, ExtendMode2D.Pad));
        Assert.Throws<ArgumentException>(() => PaintGraph2D.LinearGradient(Vector2.Zero, Vector2.One, new ColorStop2D[] { new(2, default), stop }));
    }

    /// <summary>Default and transparent nonblack colors retain their exact components.</summary>
    [Fact]
    public void TransparentStorageIsNotNormalizedAway()
    {
        Assert.Equal(new SrgbRgba<float>(0, 0, 0, 0), default);
        var hidden = new SrgbRgba<float>(1, 0.5f, 0.25f, 0);
        Assert.Equal(hidden, new SolidPaintNode2D(hidden).Color);
        Assert.Equal(hidden, new ColorStop2D(0, hidden).Color);
        Assert.Equal(hidden, new RenderTarget2D { View = null!, ClearColor = hidden }.ClearColor);
    }
}
