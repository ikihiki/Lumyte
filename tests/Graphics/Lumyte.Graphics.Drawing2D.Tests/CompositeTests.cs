using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Verifies isolated composite compilation and shared paint graph evaluation.</summary>
public sealed class CompositeTests
{
    /// <summary>All COLR operators preserve ordered operands in the GPU instruction graph.</summary>
    [Fact]
    public void EveryOperatorCompilesSourceAndBackdropInOrder()
    {
        CompositeMode2D[] modes = Enum.GetValues<CompositeMode2D>();
        Assert.Equal(28, modes.Length);
        var source = PaintGraph2D.Solid(new Color2D(1, 0, 0, 0.5f));
        var backdrop = PaintGraph2D.Solid(new Color2D(0, 0, 1, 0.25f));
        for (int i = 0; i < modes.Length; i++)
        {
            var graph = PaintGraph2D.Composite(source, backdrop, modes[i]);
            CompositePaintNode2D node = Assert.IsType<CompositePaintNode2D>(graph.Root);
            Assert.Same(source.Root, node.Source);
            Assert.Same(backdrop.Root, node.Backdrop);
            DrawingProgram2D program = Compile(graph);
            DrawingInstruction composite = Assert.Single(program.Instructions, instruction => instruction.Kind == 13);
            Assert.Equal((uint)i, composite.Mode);
            Assert.Equal(new Vector4(0.5f, 0, 0, 0.5f), program.Instructions[(int)composite.First].A);
            Assert.Equal(new Vector4(0, 0, 0.25f, 0.25f), program.Instructions[(int)composite.Count].A);
            Assert.True(composite.First < composite.Count);
            Assert.Equal(5, program.Instructions.Count);
            program.ValidateFiniteData();
        }
    }

    /// <summary>Shared source and backdrop references are compiled once instead of expanding a binary tree.</summary>
    [Fact]
    public void SharedSubgraphsRetainDagEvaluation()
    {
        var solid = PaintGraph2D.Solid(new Color2D(0, 1, 0, 0.4f));
        PaintGraph2D graph = solid;
        for (int i = 0; i < 8; i++)
        {
            graph = PaintGraph2D.Composite(graph, graph, CompositeMode2D.SourceOver);
        }

        DrawingProgram2D program = Compile(graph);
        Assert.Equal(11, program.Instructions.Count);
        Assert.Single(program.Instructions, instruction => instruction.Kind == 0);
        foreach (DrawingInstruction composite in program.Instructions.Where(instruction => instruction.Kind == 13))
        {
            Assert.Equal(composite.First, composite.Count);
        }
    }

    /// <summary>The same paint reused under different transforms retains separate spatial evaluations.</summary>
    [Fact]
    public void SharedPaintRetainsDistinctTransforms()
    {
        var gradient = PaintGraph2D.LinearGradient(Vector2.Zero, Vector2.UnitX, [new(0, new Color2D(1, 0, 0, 1)), new(1, new Color2D(0, 0, 1, 1))]);
        var source = PaintGraph2D.Transform(Matrix3x2.CreateTranslation(2, 3), gradient);
        var backdrop = PaintGraph2D.Transform(Matrix3x2.CreateTranslation(5, 7), gradient);
        DrawingProgram2D program = Compile(PaintGraph2D.Composite(source, backdrop));
        DrawingInstruction[] gradients = program.Instructions.Where(instruction => instruction.Kind == 6).ToArray();
        Assert.Equal(2, gradients.Length);
        Assert.Equal(new Vector4(1, 0, -2, 0), gradients[0].T0);
        Assert.Equal(new Vector4(0, 1, -3, 0), gradients[0].T1);
        Assert.Equal(new Vector4(1, 0, -5, 0), gradients[1].T0);
        Assert.Equal(new Vector4(0, 1, -7, 0), gradients[1].T1);
    }

    /// <summary>Composite operands remain independent of canvas clip; clipping applies once to the completed group.</summary>
    [Fact]
    public void CanvasClipAppliesAfterIsolatedComposite()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ClipRect(new Rect2D(1, 2, 8, 9));
        var source = PaintGraph2D.Solid(new Color2D(1, 0, 0, 0.5f));
        var backdrop = PaintGraph2D.Solid(new Color2D(0, 0, 1, 0.5f));
        canvas.FillRect(new Rect2D(0, 0, 20, 20), PaintGraph2D.Composite(source, backdrop, CompositeMode2D.SourceIn));
        DrawingProgram2D program = Program();
        uint root = program.Compile(Assert.Single(canvas.End().Commands));
        Assert.Equal(13u, program.Instructions[3].Kind);
        Assert.Equal(11u, program.Instructions[4].Kind);
        Assert.Equal(4u, program.Instructions[5].Kind);
        Assert.Equal(11u, program.Instructions[(int)root].Kind);
        Assert.Equal(4u, program.Instructions[(int)root].First);
        Assert.Equal(5u, program.Instructions[(int)root].Count);
    }

    /// <summary>Invalid operands and excessive expanded graphs are rejected before recording.</summary>
    [Fact]
    public void FactoryRejectsInvalidOperandsModesAndBudgets()
    {
        var solid = PaintGraph2D.Solid(new Color2D(1, 0, 0, 1));
        Assert.Throws<ArgumentNullException>(() => PaintGraph2D.Composite(null!, solid));
        Assert.Throws<ArgumentNullException>(() => PaintGraph2D.Composite(solid, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaintGraph2D.Composite(solid, solid, (CompositeMode2D)28));
        PaintGraph2D graph = solid;
        for (int i = 0; i < 15; i++)
        {
            graph = PaintGraph2D.Composite(graph, graph);
        }

        Assert.Throws<ArgumentException>(() => PaintGraph2D.Composite(graph, graph));
    }

    private static DrawingProgram2D Compile(PaintGraph2D paint)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new Rect2D(0, 0, 20, 20), paint);
        DrawingProgram2D program = Program();
        program.Compile(Assert.Single(canvas.End().Commands));
        return program;
    }

    private static DrawingProgram2D Program() => new(null!, null!, new DrawingTargetInfo2D
    {
        Description = new TextureDesc { Width = 64, Height = 64, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment },
    });
}
