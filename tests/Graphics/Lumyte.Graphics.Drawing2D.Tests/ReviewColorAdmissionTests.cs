using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Mathematics.Colors;
using Lumyte.UI;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Independent adversarial probes for raw storage and pre-publication admission.</summary>
public sealed class ReviewColorAdmissionTests
{
    /// <summary>Rejects the nearest out-of-unit values without disturbing prior commands or state.</summary>
    [Fact]
    public void EveryColorOverloadRejectsBeforeRecordingAndPreservesState()
    {
        var valid = new Rgba<float>(1, 0.5f, 0.25f, 0);
        Path2D path = new PathBuilder2D().Build();
        var style = new StrokeStyle2D();
        Action<Canvas2D, Rgba<float>>[] calls =
        [
            (c, color) => c.FillRect(new(0, 0, 1, 1), color),
            (c, color) => c.FillRoundedRect(new(0, 0, 1, 1), 1, color),
            (c, color) => c.FillCircle(Vector2.Zero, 1, color),
            (c, color) => c.DrawLine(Vector2.Zero, Vector2.One, color, style),
            (c, color) => c.FillPath(path, color),
            (c, color) => c.StrokePath(path, color, style),
        ];
        foreach (Action<Canvas2D, Rgba<float>> call in calls)
        {
            var canvas = new Canvas2D();
            canvas.Begin();
            canvas.Translate(new(3, 7));
            canvas.Save();
            canvas.FillRect(new(0, 0, 1, 1), valid);
            for (int channel = 0; channel < 4; channel++)
            {
                foreach (float bad in new[] { -float.Epsilon, MathF.BitIncrement(1f), float.MinValue, float.MaxValue })
                {
                    float[] v = [0, 0, 0, 0];
                    v[channel] = bad;
                    var raw = new Rgba<float>(v[0], v[1], v[2], v[3]);
                    Assert.Throws<ArgumentOutOfRangeException>(() => call(canvas, raw));
                }
            }

            call(canvas, valid);
            canvas.Restore();
            DrawList2D list = canvas.End();
            Assert.Equal(2, list.Commands.Count);
            Assert.All(list.Commands, command => Assert.Equal(Matrix3x2.CreateTranslation(3, 7), command.State.Transform));
        }
    }

    /// <summary>Checks init and failed with expressions across target inheritance and paint values.</summary>
    [Fact]
    public void FailedInitAndWithLeavePublishedValuesIntact()
    {
        var color = new Rgba<float>(0.25f, 0.5f, 0.75f, 0);
        var stop = new ColorStop2D(-2, color);
        var node = new SolidPaintNode2D(color);
        var target = new RenderTarget2D { View = null!, ClearColor = color, LoadOp = AttachmentLoadOp.Load };
        var info = new DrawingTargetInfo2D { Description = null!, ClearColor = color };
        var binding = new RenderTargetBinding2D { Texture = null!, Description = null!, ClearColor = color };
        for (int channel = 0; channel < 4; channel++)
        {
            foreach (float bad in new[] { -float.Epsilon, MathF.BitIncrement(1f) })
            {
                float[] v = [0, 0, 0, 0];
                v[channel] = bad;
                var raw = new Rgba<float>(v[0], v[1], v[2], v[3]);
                Assert.Throws<ArgumentOutOfRangeException>(() => new ColorStop2D(0, raw));
                Assert.Throws<ArgumentOutOfRangeException>(() => new ColorStop2D { Color = raw });
                Assert.Throws<ArgumentOutOfRangeException>(() => stop with { Color = raw });
                Assert.Throws<ArgumentOutOfRangeException>(() => new SolidPaintNode2D(raw));
                Assert.Throws<ArgumentOutOfRangeException>(() => new SolidPaintNode2D(default) { Color = raw });
                Assert.Throws<ArgumentOutOfRangeException>(() => node with { Color = raw });
                Assert.Throws<ArgumentOutOfRangeException>(() => PaintGraph2D.Solid(raw));
                Assert.Throws<ArgumentOutOfRangeException>(() => new RenderTarget2D { View = null!, ClearColor = raw });
                Assert.Throws<ArgumentOutOfRangeException>(() => target with { ClearColor = raw });
                Assert.Throws<ArgumentOutOfRangeException>(() => new DrawingTargetInfo2D { Description = null!, ClearColor = raw });
                Assert.Throws<ArgumentOutOfRangeException>(() => info with { ClearColor = raw });
                Assert.Throws<ArgumentOutOfRangeException>(() => new RenderTargetBinding2D { Description = null!, Texture = null!, ClearColor = raw });
                Assert.Throws<ArgumentOutOfRangeException>(() => binding with { ClearColor = raw });
            }
        }

        Assert.Equal(color, stop.Color);
        Assert.Equal(color, node.Color);
        Assert.Equal(color, target.ClearColor);
        Assert.Equal(color, info.ClearColor);
        Assert.Equal(color, binding.ClearColor);
    }

    /// <summary>Factories snapshot duplicate and extrapolating stops without rewriting hidden RGB.</summary>
    [Fact]
    public void GradientFactoriesOwnStopsAndPreservePermittedOffsets()
    {
        var hidden = new Rgba<float>(1, 0.5f, 0.25f, 0);
        Func<ColorStop2D[], PaintGraph2D>[] factories =
        [
            stops => PaintGraph2D.LinearGradient(Vector2.Zero, Vector2.One, stops),
            stops => PaintGraph2D.RadialGradient(Vector2.Zero, 1, stops),
            stops => PaintGraph2D.SweepGradient(Vector2.Zero, 0, 1, stops, ExtendMode2D.Reflect),
        ];
        foreach (Func<ColorStop2D[], PaintGraph2D> factory in factories)
        {
            ColorStop2D[] stops = [new(-2, hidden), new(-2, default), new(3, hidden)];
            PaintGraph2D graph = factory(stops);
            IReadOnlyList<ColorStop2D> snapshot = graph.Root switch
            {
                LinearPaintNode2D linear => linear.Stops,
                RadialPaintNode2D radial => radial.Stops,
                SweepPaintNode2D sweep => sweep.Stops,
                _ => throw new InvalidOperationException(),
            };
            stops[0] = new(0, default);
            Assert.Equal(new ColorStop2D(-2, hidden), snapshot[0]);
            Assert.Equal(new ColorStop2D(-2, default), snapshot[1]);
            Assert.Equal(new ColorStop2D(3, hidden), snapshot[2]);
            Assert.Throws<ArgumentException>(() => factory([new(3, default), new(-2, default)]));
            Assert.NotNull(factory([]));
        }

        var stop = new ColorStop2D(-2, hidden);
        foreach (float bad in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ColorStop2D(bad, hidden));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ColorStop2D { Offset = bad });
            Assert.Throws<ArgumentOutOfRangeException>(() => stop with { Offset = bad });
        }

        Assert.Equal(-2, stop.Offset);
    }

    /// <summary>Raw HDR storage and backend clear remain distinct from unit drawing admission.</summary>
    [Fact]
    public void RawHdrAndDefaultRemainValidWithoutColorWrappers()
    {
        var raw = new Rgba<float>(-1, 2, -0.5f, 3);
        Assert.Equal(3, raw.A);
        var clear = new ClearColor(-1, 2, -0.5, 3);
        Assert.Equal(-1, clear.Red);
        Assert.Equal(3, clear.Alpha);
        Assert.Equal(default, default(ColorStop2D).Color);
        Assert.Equal(default, Assert.IsType<SolidPaintNode2D>(PaintGraph2D.Solid(default).Root).Color);
        Assert.Null(typeof(Canvas2D).Assembly.GetType("Lumyte.Graphics.Drawing2D.Color2D"));
        for (int channel = 0; channel < 4; channel++)
        {
            foreach (float bad in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity })
            {
                float[] v = [0, 0, 0, 0];
                v[channel] = bad;
                Assert.Throws<ArgumentOutOfRangeException>(() => new Rgba<float>(v[0], v[1], v[2], v[3]));
            }
        }
    }

    /// <summary>Fixed baseline expressions retain float paint, float-source double clear, and raw UI rounding.</summary>
    [Fact]
    public void PrecisionBoundariesMatchFixedBaselineOracles()
    {
        foreach (float value in new[] { 0f, float.Epsilon, MathF.BitDecrement(0.04045f), 0.04045f, MathF.BitIncrement(0.04045f), 0.1f, 0.5f, 1f })
        {
            var color = new Rgba<float>(value, value, value, 0.9f);
            float paint = (value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f)) * color.A;
            Assert.Equal(new Vector4(paint, paint, paint, color.A), DrawingProgram2D.Linear(color));
            double clear = (value <= 0.04045f ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4)) * color.A;
            Assert.Equal(clear, color.DecodeSrgb<double>().PremultiplyLinear<double>().R);
        }

        Rgba<float> middle = UiAnimationInterpolators.Color.Interpolate(new(0, 0, 0, 0.1f), new(0, 0, 0, 0.9f), 0.5f);
        Assert.Equal(0.49999997f, middle.A);
        Rgba<float> extrapolated = UiAnimationInterpolators.Color.Interpolate(new(0, 0, 0, 0.9f), new(0, 0, 0, 1), MathF.BitIncrement(1f));
        Assert.Equal(1f, extrapolated.A);
        var hidden = new Rgba<float>(1, 0.5f, 0.25f, 0);
        Assert.Equal(hidden, UiAnimationInterpolators.Color.Interpolate(hidden, default, 0));
    }
}
