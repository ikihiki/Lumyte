using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Restricts rectangle specialization to exactly representable canonical contours.</summary>
public sealed class RectanglePathTests
{
    /// <summary>Fractional rectangles retain all four original edge coordinates.</summary>
    [Fact]
    public void CanonicalRectanglePreservesEdges()
    {
        Path2D path = Rectangle(1.125f, -2.25f, 19.5f, 12.75f);
        Assert.True(DrawingProgram2D.TryRectangle(path, out Rect2D rectangle));
        Assert.Equal(new Rect2D(1.125f, -2.25f, 18.375f, 15), rectangle);
    }

    /// <summary>Empty, backwards, overflowing and lossy extents keep winding evaluation.</summary>
    /// <param name="left">The left edge.</param>
    /// <param name="top">The top edge.</param>
    /// <param name="right">The right edge.</param>
    /// <param name="bottom">The bottom edge.</param>
    [Theory]
    [InlineData(0f, 0f, 0f, 10f)]
    [InlineData(0f, 0f, 10f, 0f)]
    [InlineData(10f, 0f, 0f, 10f)]
    [InlineData(-3.4028235E+38f, 0f, 3.4028235E+38f, 10f)]
    [InlineData(-1E+20f, 0f, 1f, 10f)]
    public void NonCanonicalExtentsFallBack(float left, float top, float right, float bottom)
        => Assert.False(DrawingProgram2D.TryRectangle(Rectangle(left, top, right, bottom), out _));

    /// <summary>Open, curved, skewed and multi-contour paths cannot use rectangle coverage.</summary>
    [Fact]
    public void OtherContoursFallBack()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.LineTo(new(10, 0));
        builder.LineTo(new(10, 10));
        builder.LineTo(new(0, 10));
        Assert.False(DrawingProgram2D.TryRectangle(builder.Build(), out _));
        builder.Close();
        builder.MoveTo(new(2, 2));
        Assert.False(DrawingProgram2D.TryRectangle(builder.Build(), out _));

        builder = new();
        builder.MoveTo(Vector2.Zero);
        builder.LineTo(new(10, 1));
        builder.LineTo(new(10, 10));
        builder.LineTo(new(0, 10));
        builder.Close();
        Assert.False(DrawingProgram2D.TryRectangle(builder.Build(), out _));

        builder = new();
        builder.MoveTo(Vector2.Zero);
        builder.QuadraticTo(new(5, 0), new(10, 0));
        builder.LineTo(new(10, 10));
        builder.LineTo(new(0, 10));
        builder.Close();
        Assert.False(DrawingProgram2D.TryRectangle(builder.Build(), out _));
    }

    /// <summary>Nested canonical clips compile without edge data or a winding instruction.</summary>
    [Fact]
    public void CompiledRectangleClipsNeedNoEdgeUpload()
    {
        var target = new DrawingTargetInfo2D { Description = new() { Width = 64, Height = 48, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ClipRect(new(0, 0, 64, 48));
        canvas.ClipRect(new(1.125f, 2.25f, 17.5f, 13.5f));
        canvas.FillCircle(new(8, 8), 10, new Color2D(1, 0, 0, 1));
        DrawingCommand2D command = Assert.Single(canvas.End().Commands);
        var program = new DrawingProgram2D(new ArgumentTablePlan(new ProfilingDevice()).CreateShaderResources(), target);
        program.Compile(command);
        Assert.Empty(program.Data);
        Assert.Equal(2, program.Instructions.Count(instruction => instruction.Kind == 1));
        Assert.DoesNotContain(program.Instructions, instruction => instruction.Kind == 4);
        program.ValidateFiniteData();
    }

    /// <summary>A captured clip whose determinant underflows must remain transparent.</summary>
    [Fact]
    public void NonInvertibleNestedRectangleClipRemainsTransparent()
    {
        var target = new DrawingTargetInfo2D { Description = new() { Width = 8, Height = 8, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Scale(new Vector2(1e-24f));
        canvas.ClipRect(new(0, 0, 32, 32));
        canvas.Scale(new Vector2(1e24f));
        canvas.ClipRect(new(0, 0, 8, 8));
        canvas.FillCircle(new(1, 1), 4, new Color2D(1, 0, 0, 1));
        DrawingCommand2D command = Assert.Single(canvas.End().Commands);
        Assert.False(Matrix3x2.Invert(command.State.Clip.Elements[0].Transform, out _));
        Assert.True(Matrix3x2.Invert(command.State.Transform, out _));
        var program = new DrawingProgram2D(new ArgumentTablePlan(new ProfilingDevice()).CreateShaderResources(), target);
        program.CompileClip(command.State);
        DrawingInstruction[] constants = program.Instructions.Where(instruction => instruction.Kind == 0).ToArray();
        Assert.Equal(2, constants.Length);
        Assert.Equal(Vector4.One, constants[0].A);
        Assert.Equal(Vector4.Zero, constants[1].A);
        program.ValidateFiniteData();
    }

    private static Path2D Rectangle(float left, float top, float right, float bottom)
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(new(left, top));
        builder.LineTo(new(right, top));
        builder.LineTo(new(right, bottom));
        builder.LineTo(new(left, bottom));
        builder.Close();
        return builder.Build();
    }
}
