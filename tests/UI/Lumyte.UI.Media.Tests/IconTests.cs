using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Media.Compose;

namespace Lumyte.UI.Media.Tests;

/// <summary>Checks immutable vector and glyph icon sources through public drawing APIs.</summary>
public sealed class IconTests
{
    /// <summary>Checks explicit source coordinates, sizing, accessible name and noninteractive semantics.</summary>
    [Fact]
    public void VectorAndGlyphIconsRecordInSizedBodyAndRemainReadOnly()
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(10, 10));
        path.LineTo(new(20, 10));
        path.LineTo(new(15, 20));
        path.Close();
        Path2D geometry = path.Build();
        var paint = PaintGraph2D.Solid(new(1, 0, 0, 1));
        var vector = IconSource.Vector(geometry, new(10, 10, 10, 10), paint);
        var layout = TextLayout2D.FromDrawings("x", new(10, 10, 10, 10), new(0, 0, 20, 20), 1, [new TextGeometry2D(geometry, paint)]);
        var glyph = IconSource.Glyph(layout, new(10, 10, 10, 10));
        using var surface = new UiSurface(Stack()[Icon(source: vector, size: 20, automationName: "Save", testId: "vector", with: [Padding.All(2)]), Icon(source: glyph, size: 16)]);
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(24), surface.RootElement.Children[0].Size);
        UiAutomationNode node = surface.CaptureAutomationSnapshot().Nodes.Single(value => value.TestId == "vector");
        Assert.Equal("Save", node.Name);
        Assert.Equal(UiRole.Icon, node.Role);
        Assert.Equal(UiAutomationActions.None, node.Actions);
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        Assert.Equal(2, canvas.End().Count);
        using UiDrawingScene2D scene = surface.CreateDrawingScene();
        scene.Update();
        scene.Update();
        Assert.Equal(0, scene.LastRecordingCount);
    }

    /// <summary>Checks invalid source rectangles fail before drawing.</summary>
    [Fact]
    public void IconSourceRejectsEmptyOrNonfiniteViewBox()
    {
        var layout = TextLayout2D.FromDrawings(string.Empty, default, default, 0, []);
        Assert.Throws<ArgumentOutOfRangeException>(() => IconSource.Glyph(layout, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => IconSource.Glyph(layout, new(float.NaN, 0, 1, 1)));
    }
}
