using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Verifies the outline drawing route used by shaped text.</summary>
public sealed class PathFillTests
{
    /// <summary>Invalid fills do not append commands, and fills retain independent state and contour snapshots.</summary>
    [Fact]
    public void RecordingCapturesPathsAndRejectsInvalidRulesTransactionally()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.LineTo(new Vector2(10, 0));
        builder.LineTo(new Vector2(0, 10));
        Path2D path = builder.Build();
        var canvas = new Canvas2D();
        Assert.Throws<InvalidOperationException>(() => canvas.FillPath(path, default(Color2D)));
        canvas.Begin();
        canvas.Translate(new Vector2(3, 4));
        Assert.Throws<ArgumentNullException>(() => canvas.FillPath(null!, default(Color2D)));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.FillPath(path, default(Color2D), (FillRule2D)42));
        canvas.FillPath(path, new Color2D(1, 1, 1, 1), FillRule2D.EvenOdd);
        canvas.Translate(Vector2.One);
        builder.Close();

        DrawList2D list = canvas.End();
        FillPathCommand2D command = Assert.Single(list.GetCommands<FillPathCommand2D>(0, 1).ToArray());
        Assert.Same(path, command.Path);
        Assert.Equal(3, command.Path.Segments.Count);
        Assert.Equal(new Vector2(3, 4), command.State.Transform.Translation);
        Assert.Equal(FillRule2D.EvenOdd, command.Rule);
    }

    /// <summary>Open outlines close on the GPU, retain counters as separate contours, and combine with clip and paint.</summary>
    [Fact]
    public void CompilationPreservesContoursAndTransformsWithoutJoiningThem()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(new Vector2(1, 2));
        builder.LineTo(new Vector2(21, 2));
        builder.LineTo(new Vector2(21, 22));
        builder.LineTo(new Vector2(1, 22));
        builder.MoveTo(new Vector2(5, 6));
        builder.LineTo(new Vector2(17, 6));
        builder.LineTo(new Vector2(17, 18));
        builder.LineTo(new Vector2(5, 18));
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ClipRect(new Rect2D(0, 0, 40, 40));
        canvas.Translate(new Vector2(10, 20));
        canvas.FillPath(builder.Build(), new Color2D(1, 0, 0, 0.5f), FillRule2D.EvenOdd);
        DrawingCommand2D command = Assert.Single(canvas.End().Commands);
        var target = new DrawingTargetInfo2D
        {
            Description = new TextureDesc { Width = 200, Height = 200, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment },
            PixelScale = 2,
        };
        var program = new DrawingProgram2D(null!, null!, target);
        uint root = program.Compile(command);
        program.ValidateFiniteData();

        DrawingInstruction outline = program.Instructions[0];
        Assert.Equal(4u, outline.Kind);
        Assert.Equal((uint)FillRule2D.EvenOdd, outline.Mode);
        Assert.Equal(8u, outline.Count);
        Assert.Equal(new Vector4(1, 22, 1, 2), program.Data[3]);
        Assert.Equal(new Vector4(5, 18, 5, 6), program.Data[7]);
        Assert.Equal(new Vector4(0.5f, 0, -10, 0), outline.T0);
        Assert.Equal(new Vector4(0, 0.5f, -20, 0), outline.T1);
        Assert.Equal(new Vector4(0.5f, 0, 0, 0.5f), program.Instructions[1].A);
        Assert.Equal(11u, program.Instructions[(int)root].Kind);
        Assert.Equal(2, program.Instructions.Count(instruction => instruction.Kind == 4));

        ScissorRect scissor = DrawingBounds2D.GetScissor(command, null, target, null, false, false);
        Assert.Equal(new ScissorRect(21, 43, 42, 38), scissor);
    }
}
