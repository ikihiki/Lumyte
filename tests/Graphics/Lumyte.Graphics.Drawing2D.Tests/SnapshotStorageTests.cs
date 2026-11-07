using System.Numerics;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Protects immutable typed command indexing for homogeneous and mixed tapes.</summary>
public sealed class SnapshotStorageTests
{
    /// <summary>Typed slices retain exact object identity after caller mutation.</summary>
    /// <param name="count">The command count around pool boundaries.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(10000)]
    public void HomogeneousSnapshotCopiesInput(int count)
    {
        var state = new DrawingState2D(Matrix3x2.Identity, ClipStack2D.Empty);
        var paint = PaintGraph2D.Solid(new(1, 0, 0, 1));
        DrawingCommand2D[] input = Enumerable.Range(0, count).Select(i => (DrawingCommand2D)new FillRectCommand2D(new(i, 0, 1, 1), paint, state)).ToArray();
        var snapshot = new DrawList2D(input);
        FillRectCommand2D[] expected = input.Cast<FillRectCommand2D>().ToArray();
        Array.Clear(input);
        Assert.Equal(expected, snapshot.GetCommands<FillRectCommand2D>(0, count).ToArray());
        Assert.Equal(expected, snapshot.Commands);
        Assert.Throws<NotSupportedException>(() => ((IList<DrawingCommand2D>)snapshot.Commands).Clear());
    }

    /// <summary>Immutable snapshot storage stays linear without a temporary growing typed list.</summary>
    [Fact]
    public void SnapshotAllocationStaysWithinExactArrayBudget()
    {
        var state = new DrawingState2D(Matrix3x2.Identity, ClipStack2D.Empty);
        var paint = PaintGraph2D.Solid(new(1, 0, 0, 1));
        DrawingCommand2D[] commands = Enumerable.Range(0, 10000).Select(i => (DrawingCommand2D)new FillRectCommand2D(new(i, 0, 1, 1), paint, state)).ToArray();
        _ = new DrawList2D(commands);
        long start = GC.GetAllocatedBytesForCurrentThread();
        var snapshot = new DrawList2D(commands);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        long payload = 10000L * ((2 * IntPtr.Size) + sizeof(int));
        Assert.InRange(allocated, payload, payload + 10000);
        GC.KeepAlive(snapshot);
    }

    /// <summary>Mixed type runs keep painter order and reject slices crossing a different command.</summary>
    [Fact]
    public void MixedRunsKeepTypedOffsets()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 1, 1), new Color2D(1, 0, 0, 1));
        canvas.FillCircle(Vector2.Zero, 2, new Color2D(0, 1, 0, 1));
        canvas.FillRect(new(2, 0, 1, 1), new Color2D(0, 0, 1, 1));
        canvas.FillRect(new(3, 0, 1, 1), new Color2D(1, 1, 0, 1));
        DrawList2D snapshot = canvas.End();
        Assert.Same(snapshot.Commands[2], snapshot.GetCommands<FillRectCommand2D>(2, 2).Span[0]);
        Assert.Same(snapshot.Commands[3], snapshot.GetCommands<FillRectCommand2D>(2, 2).Span[1]);
        Assert.Same(snapshot.Commands[1], snapshot.GetCommands<FillCircleCommand2D>(1, 1).Span[0]);
        Assert.Throws<ArgumentException>(() => snapshot.GetCommands<FillRectCommand2D>(0, 3));
        canvas.Begin();
        Assert.Empty(canvas.End().Commands);
        Assert.Equal(4, snapshot.Commands.Count);
    }
}
