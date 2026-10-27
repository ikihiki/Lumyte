using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Verifies COLR color lines and geometries reach the standard GPU evaluator without approximation.</summary>
public sealed class GradientSemanticsTests
{
    /// <summary>Spec-defined transparent and hard-edge degeneracies compile instead of requiring a fallback renderer.</summary>
    [Fact]
    public void DegenerateGeometryAndColorLinesRemainRepresentable()
    {
        ColorStop2D[] stops = [new(0.5f, new Color2D(1, 0, 0, 1)), new(0.5f, new Color2D(0, 0, 1, 1))];
        PaintGraph2D[] paints =
        [
            PaintGraph2D.LinearGradient(Vector2.Zero, Vector2.Zero, Vector2.Zero, stops, ExtendMode2D.Pad),
            PaintGraph2D.LinearGradient(Vector2.Zero, Vector2.One, Vector2.One * 2, stops, ExtendMode2D.Pad),
            PaintGraph2D.LinearGradient(Vector2.Zero, Vector2.One, [], ExtendMode2D.Pad),
            PaintGraph2D.LinearGradient(Vector2.Zero, Vector2.One, stops, ExtendMode2D.Repeat),
            PaintGraph2D.LinearGradient(Vector2.Zero, Vector2.One, stops, ExtendMode2D.Reflect),
            PaintGraph2D.RadialGradient(Vector2.One, 5, Vector2.One, 5, stops, ExtendMode2D.Pad),
            PaintGraph2D.RadialGradient(Vector2.Zero, 0, Vector2.One, 0, stops, ExtendMode2D.Pad),
            PaintGraph2D.SweepGradient(Vector2.One, MathF.PI, MathF.PI, stops, ExtendMode2D.Pad),
            PaintGraph2D.SweepGradient(Vector2.One, MathF.PI, MathF.PI, stops, ExtendMode2D.Repeat),
        ];
        foreach (PaintGraph2D paint in paints)
        {
            DrawingProgram2D program = Compile(paint);
            DrawingInstruction gradient = Assert.Single(program.Instructions, instruction => instruction.Kind is 6 or 7 or 8);
            int expectedCount = paint.Root switch
            {
                LinearPaintNode2D linear => linear.Stops.Count,
                RadialPaintNode2D radial => radial.Stops.Count,
                SweepPaintNode2D sweep => sweep.Stops.Count,
                _ => throw new InvalidOperationException(),
            };
            Assert.Equal((uint)expectedCount, gradient.Count);
        }
    }

    /// <summary>Variable radial circles can be signed; their numeric values are not clamped or converted into a different gradient.</summary>
    [Fact]
    public void SignedRadialRadiiReachTheShaderUnchanged()
    {
        ColorStop2D[] stops = [new(0, new Color2D(1, 0, 0, 1)), new(1, new Color2D(0, 0, 1, 1))];
        var paint = PaintGraph2D.RadialGradient(new Vector2(2, 3), -10, new Vector2(5, 7), 20, stops, ExtendMode2D.Reflect);
        RadialPaintNode2D node = Assert.IsType<RadialPaintNode2D>(paint.Root);
        Assert.Equal(-10, node.R0);
        DrawingInstruction radial = Assert.Single(Compile(paint).Instructions, instruction => instruction.Kind == 7);
        Assert.Equal(new Vector4(2, 3, -10, 0), radial.A);
        Assert.Equal(new Vector4(5, 7, 20, 0), radial.B);
        Assert.Equal((uint)ExtendMode2D.Reflect, radial.Mode);
        Assert.Throws<ArgumentOutOfRangeException>(() => PaintGraph2D.RadialGradient(Vector2.Zero, float.NaN, Vector2.One, 2, stops, ExtendMode2D.Pad));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaintGraph2D.RadialGradient(Vector2.Zero, -2, stops));
    }

    /// <summary>Stops stay ordered at coincident offsets and are decoded to linear-light premultiplied values once.</summary>
    [Fact]
    public void StopDataPreservesHardEdgesAndLinearLightAlpha()
    {
        ColorStop2D[] stops =
        [
            new(-0.5f, new Color2D(0.5f, 0.5f, 0.5f, 0.5f)),
            new(0.75f, new Color2D(1, 0, 0, 0.5f)),
            new(0.75f, new Color2D(0, 0, 1, 1)),
        ];
        var paint = PaintGraph2D.LinearGradient(Vector2.Zero, Vector2.One, stops);
        DrawingProgram2D program = Compile(paint);
        DrawingInstruction gradient = Assert.Single(program.Instructions, instruction => instruction.Kind == 6);
        int first = (int)gradient.First;
        Assert.Equal(-0.5f, program.Data[first].X);
        Assert.Equal(0.75f, program.Data[first + 2].X);
        Assert.Equal(0.75f, program.Data[first + 4].X);
        Assert.Equal(0.10702057f, program.Data[first + 1].X, 6);
        Assert.Equal(0.5f, program.Data[first + 1].W);
        Assert.Equal(new Vector4(0.5f, 0, 0, 0.5f), program.Data[first + 3]);
        Assert.Equal(new Vector4(0, 0, 1, 1), program.Data[first + 5]);
    }

    private static DrawingProgram2D Compile(PaintGraph2D paint)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new Rect2D(0, 0, 100, 100), paint);
        var target = new DrawingTargetInfo2D
        {
            Description = new TextureDesc { Width = 100, Height = 100, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment },
        };
        var program = new DrawingProgram2D(null!, null!, target);
        program.Compile(Assert.Single(canvas.End().Commands));
        program.ValidateFiniteData();
        return program;
    }
}
