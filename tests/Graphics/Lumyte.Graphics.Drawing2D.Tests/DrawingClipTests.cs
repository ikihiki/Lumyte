using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks scissor results across command, clip and source-policy branches.</summary>
public sealed class DrawingClipTests
{
    /// <summary>Nonempty clips intersect correctly with a command or a standalone clip state.</summary>
    /// <param name="withCommand">Whether command bounds are intersected first.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonemptyClipsIntersectBounds(bool withCommand)
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(10, 20));
        path.LineTo(new(30, 20));
        path.LineTo(new(30, 40));
        path.Close();
        Path2D shape = path.Build();
        var state = new DrawingState2D(Matrix3x2.Identity, new ClipStack2D([
            new(shape, Matrix3x2.CreateTranslation(5, 7), FillRule2D.NonZero),
            new(shape, Matrix3x2.CreateTranslation(8, 9), FillRule2D.EvenOdd),
        ]));
        DrawingCommand2D? command = withCommand ? new FillRectCommand2D(new(0, 0, 100, 100), PaintGraph2D.Solid(new(1, 1, 1, 1)), state) : null;
        AssertScissor(command, state, null, false, false, null, new(17, 28, 19, 20));
    }

    /// <summary>Empty, absent, normalized and source-policy branches preserve their bounds.</summary>
    /// <param name="branch">The branch to exercise.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void OtherScissorBranchesPreserveBounds(int branch)
    {
        var state = new DrawingState2D(Matrix3x2.Identity, ClipStack2D.Empty);
        DrawingCommand2D? command = branch == 0 ? new FillRectCommand2D(new(10, 20, 30, 40), PaintGraph2D.Solid(new(1, 1, 1, 1)), state) : null;
        Rect2D? source = branch >= 3 ? new(20, 30, 40, 50) : null;
        ScissorRect? normalized = branch == 2 ? new(3, 4, 5, 6) : null;
        ScissorRect expected = branch switch
        {
            0 => new(19, 39, 62, 82),
            2 => new(3, 4, 5, 6),
            3 => new(19, 29, 42, 52),
            _ => new(0, 0, 256, 256),
        };
        AssertScissor(command, branch == 1 ? state : null, source, branch == 4, branch == 5, normalized, expected);
    }

    private static void AssertScissor(DrawingCommand2D? command, DrawingState2D? state, Rect2D? source, bool hasOriginal, bool replace, ScissorRect? normalized, ScissorRect expected)
    {
        DrawingTargetInfo2D target = new() { PixelScale = 2, Description = new() { Width = 256, Height = 256, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };

        // Nonempty fixtures use unit pixels so their independent intersection is easy to audit.
        if (state is not null && state.Clip.Elements.Count != 0)
        {
            target = target with { PixelScale = 1 };
        }

        Assert.Equal(expected, DrawingBounds2D.GetScissor(command, state, target, source, hasOriginal, replace, normalized));
    }
}
