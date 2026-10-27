using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Xunit;

namespace Lumyte.Graphics.Text.Tests;

/// <summary>Exercises immutable engine-independent text snapshots and their vector-to-canvas contract.</summary>
public sealed class TextLayoutTests
{
    /// <summary>Snapshots copy the caller's painter-ordered drawing list and expose immutable geometry views.</summary>
    [Fact]
    public void LayoutCopiesDrawingsAndRetainsPainterOrder()
    {
        var outline = new TextGeometry2D(Triangle(), PaintGraph2D.Solid(new Color2D(1, 0, 0, 1)));
        var color = new TextPaintGeometry2D(PaintGraph2D.Solid(new Color2D(0, 0, 1, 1)), new Rect2D(12, 0, 10, 20));
        var drawings = new List<TextDrawing2D> { outline, color, outline };
        var bounds = new Rect2D(0, 0, 22, 20);
        var advance = new Rect2D(0, 0, 30, 24);
        var layout = TextLayout2D.FromDrawings("ABA", bounds, advance, 1, drawings);
        drawings.Clear();

        Assert.Equal("ABA", layout.Text);
        Assert.Equal(bounds, layout.Bounds);
        Assert.Equal(advance, layout.AdvanceBounds);
        Assert.Equal(1, layout.LineCount);
        Assert.Equal(new TextDrawing2D[] { outline, color, outline }, layout.Drawings);
        Assert.Equal(new[] { outline, outline }, layout.Geometry);
        Assert.Throws<NotSupportedException>(() => ((IList<TextDrawing2D>)layout.Drawings).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<TextGeometry2D>)layout.Geometry).Clear());
    }

    /// <summary>Mixed outline and color recording preserves the saved transform and clip across repeated recordings.</summary>
    [Fact]
    public void DrawingTextRestoresStateAndPreservesMixedDrawingOrder()
    {
        Path2D path = Triangle();
        var ink = PaintGraph2D.Solid(new Color2D(1, 0, 0, 1));
        var layout = TextLayout2D.FromDrawings("ABA", new Rect2D(0, 0, 30, 20), new Rect2D(0, 0, 30, 24), 1, [new TextGeometry2D(path, ink), new TextPaintGeometry2D(ink, new Rect2D(10, 0, 10, 20)), new TextGeometry2D(path, ink)]);
        var canvas = new InspectCanvas();
        Assert.Throws<InvalidOperationException>(() => canvas.DrawText(layout));
        canvas.Begin();
        canvas.Translate(new Vector2(11, 23));
        canvas.Scale(new Vector2(2, 3));
        canvas.ClipRect(new Rect2D(1, 2, 90, 40));
        DrawingState2D state = canvas.CurrentState;
        var origin = new Vector2(5, 7);
        canvas.Save();
        canvas.DrawText(layout, origin);
        Assert.Equal(state.Transform, canvas.CurrentState.Transform);
        Assert.Same(state.Clip, canvas.CurrentState.Clip);
        canvas.Restore();

        canvas.End();
        canvas.Begin();
        canvas.DrawText(layout, new Vector2(-1, 2));
        Assert.Equal(Matrix3x2.Identity, canvas.CurrentState.Transform);
        Assert.Empty(canvas.CurrentState.Clip.Elements);
        canvas.End();
    }

    /// <summary>Invalid snapshot metadata is rejected before a caller can record a corrupt layout.</summary>
    [Fact]
    public void InvalidSnapshotMetadataIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => TextLayout2D.FromDrawings(null!, default, default, 0, []));
        Assert.Throws<ArgumentNullException>(() => TextLayout2D.FromDrawings(string.Empty, default, default, 0, null!));
        Assert.Throws<ArgumentException>(() => TextLayout2D.FromDrawings(string.Empty, default, default, 0, [null!]));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextLayout2D.FromDrawings(string.Empty, default, default, -1, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextLayout2D.FromDrawings(string.Empty, new Rect2D(float.NaN, 0, 1, 1), default, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextLayout2D.FromDrawings(string.Empty, default, new Rect2D(0, 0, -1, 1), 0, []));
    }

    /// <summary>Invalid origins and unsupported drawing types fail before changing the canvas state.</summary>
    [Fact]
    public void InvalidDrawingInputLeavesCanvasUsable()
    {
        var empty = TextLayout2D.FromDrawings(string.Empty, default, default, 0, []);
        var unsupported = TextLayout2D.FromDrawings("?", default, default, 1, [new UnsupportedDrawing()]);
        Assert.Throws<ArgumentNullException>(() => TextCanvas2DExtensions.DrawText(null!, empty));
        var canvas = new InspectCanvas();
        canvas.Begin();
        canvas.Translate(new Vector2(3, 4));
        DrawingState2D state = canvas.CurrentState;
        Assert.Throws<ArgumentNullException>(() => canvas.DrawText(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.DrawText(empty, new Vector2(float.NaN, 0)));
        Assert.Throws<NotSupportedException>(() => canvas.DrawText(unsupported));
        Assert.Equal(state.Transform, canvas.CurrentState.Transform);
        Assert.Same(state.Clip, canvas.CurrentState.Clip);
        canvas.DrawText(empty);
        canvas.FillPath(Triangle(), new Color2D(0, 0, 0, 1));
        canvas.End();
    }

    private static Path2D Triangle()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.LineTo(new Vector2(10, 0));
        builder.LineTo(new Vector2(5, 20));
        builder.Close();
        return builder.Build();
    }

    private sealed record UnsupportedDrawing : TextDrawing2D;

    private sealed class InspectCanvas : DrawingCanvas2D
    {
        public DrawingState2D CurrentState => State;

        public DrawList2D End() => CompleteRecording();
    }
}
