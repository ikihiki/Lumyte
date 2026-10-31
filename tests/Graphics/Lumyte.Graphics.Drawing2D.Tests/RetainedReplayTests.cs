using System.Numerics;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks retained recording replay composes transforms and clips without changing the source.</summary>
public sealed class RetainedReplayTests
{
    /// <summary>Checks a retained child keeps its local state and inherits each caller's clip and transform.</summary>
    [Fact]
    public void ReplayRebasesStateWithoutMutatingTheRetainedCommands()
    {
        var child = new Canvas2D();
        child.Begin();
        child.Translate(new(2, 3));
        child.ClipRect(new(0, 0, 10, 10));
        child.FillRect(new(0, 0, 8, 9), new Color2D(1, 0, 0, 1));
        DrawList2D retained = child.End();
        DrawingState2D original = Assert.Single(retained.Commands).State;
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ClipRect(new(0, 0, 50, 50));
        canvas.Translate(new(20, 30));
        canvas.Replay(retained);
        canvas.Translate(new(5, 0));
        canvas.Replay(retained);
        DrawList2D result = canvas.End();
        Assert.Equal(2, result.Commands.Count);
        Assert.Equal(new Vector2(22, 33), Vector2.Transform(Vector2.Zero, result.Commands[0].State.Transform));
        Assert.Equal(new Vector2(27, 33), Vector2.Transform(Vector2.Zero, result.Commands[1].State.Transform));
        Assert.Equal(2, result.Commands[0].State.Clip.Elements.Count);
        Assert.Equal(Matrix3x2.Identity, result.Commands[0].State.Clip.Elements[0].Transform);
        Assert.Equal(result.Commands[0].State.Transform, result.Commands[0].State.Clip.Elements[1].Transform);
        Assert.Same(original, Assert.Single(retained.Commands).State);
        Assert.Equal(new Rect2D(0, 0, 8, 9), Assert.IsType<FillRectCommand2D>(result.Commands[0]).Rect);
    }
}
