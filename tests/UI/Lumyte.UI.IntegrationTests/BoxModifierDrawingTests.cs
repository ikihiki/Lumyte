using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Xunit;

namespace Lumyte.UI.IntegrationTests;

/// <summary>Checks immutable direct and retained geometry at the drawing boundary.</summary>
public sealed class BoxModifierDrawingTests
{
    /// <summary>Checks border layers differ by order and body drawing is translated once.</summary>
    [Fact]
    public void OrderedBordersAndBodyUseTheirOwnRectangles()
    {
        using var first = new UiSurface(Compose.Panel()[Compose.Rectangle(width: 10, height: 8, fill: new Color2D(0, 1, 0, 1), with: [Compose.Padding.All(3), Compose.Border.Stroke(new(1, 0, 0, 1), 2)])]);
        using var second = new UiSurface(Compose.Panel()[Compose.Rectangle(width: 10, height: 8, fill: new Color2D(0, 1, 0, 1), with: [Compose.Border.Stroke(new(1, 0, 0, 1), 2), Compose.Padding.All(3)])]);
        first.SetViewport(new(100, 100));
        second.SetViewport(new(100, 100));
        FillRectCommand2D[] padded = Record(first).Commands.Cast<FillRectCommand2D>().ToArray();
        FillRectCommand2D[] bordered = Record(second).Commands.Cast<FillRectCommand2D>().ToArray();
        Assert.Equal(new Rect2D(3, 3, 14, 2), padded[0].Rect);
        Assert.Equal(new Rect2D(0, 0, 20, 2), bordered[0].Rect);
        Assert.Equal(new Rect2D(0, 0, 10, 8), padded[^1].Rect);
        Assert.Equal(new Vector2(5, 5), padded[^1].State.Transform.Translation);
        Assert.Equal(padded[^1].Rect, bordered[^1].Rect);
        Assert.Equal(padded[^1].State.Transform, bordered[^1].State.Transform);
        Assert.Equal(padded[^1].Paint.Root, bordered[^1].Paint.Root);
    }

    /// <summary>Checks retained child placement includes parent body offset exactly once.</summary>
    [Fact]
    public void RetainedAndDirectNestedBodyCoordinatesAgree()
    {
        using var surface = new UiSurface(Compose.Panel(with: [Compose.Padding.Sides(11, 7, 0, 0)])[Compose.Rectangle(width: 10, height: 8, fill: new Color2D(1, 0, 0, 1), with: [Compose.Padding.All(2)])]);
        surface.SetViewport(new(100, 100));
        FillRectCommand2D direct = Assert.IsType<FillRectCommand2D>(Assert.Single(Record(surface).Commands));
        using UiDrawingScene2D scene = surface.CreateDrawingScene();
        scene.Update();
        using DrawingSceneReadLease2D lease = scene.Borrow();
        DrawingSceneEntry2D entry = Assert.Single(((IDrawingSceneSource2D)lease).Entries, e => e.Content.Count != 0);
        FillRectCommand2D retained = Assert.IsType<FillRectCommand2D>(Assert.Single(entry.Content.Commands));
        Assert.Equal(direct.Rect, retained.Rect);
        Assert.Equal(new Vector2(13, 9), direct.State.Transform.Translation);
        Assert.Equal(direct.State.Transform, retained.State.Transform * entry.Transform);
    }

    /// <summary>Checks a wide border stays inside a zero-content tight box.</summary>
    [Fact]
    public void OverlargeBorderDoesNotDrawOutsideItsBox()
    {
        using var surface = new UiSurface(Compose.SizedBox(with: [Compose.Border.Stroke(new(1, 0, 0, 0.5f), 50)]));
        surface.SetViewport(new(10, 8));
        foreach (FillRectCommand2D command in Record(surface).Commands.Cast<FillRectCommand2D>())
        {
            Assert.InRange(command.Rect.X, 0, 10);
            Assert.InRange(command.Rect.Y, 0, 8);
            Assert.InRange(command.Rect.X + command.Rect.Width, 0, 10);
            Assert.InRange(command.Rect.Y + command.Rect.Height, 0, 8);
        }
    }

    /// <summary>Checks runtime measurement invalidates retained body and border geometry.</summary>
    [Fact]
    public void RuntimeMeasurementInvalidatesAllModifierRectangles()
    {
        var child = new RuntimeMeasureWidget();
        Compose.Definitions.SizedBox box = Compose.SizedBox(with: [Compose.Align.Center(), Compose.Border.Stroke(new(1, 0, 0, 1), 2)])[child];
        using var surface = new UiSurface(box);
        surface.SetViewport(new(100, 80));
        using UiDrawingScene2D scene = surface.CreateDrawingScene();
        scene.Update();
        UiElement element = surface.RootElement;
        Rect2D body = element.ContentBounds;
        Vector2 outer = element.Size;
        FillRectCommand2D[] before = Record(surface).Commands.Cast<FillRectCommand2D>().ToArray();
        child.Element!.Measure(new(30, 20));
        scene.Update();
        Assert.Same(box, element.Widget);
        Assert.Equal(outer, element.Size);
        Assert.NotEqual(body, element.ContentBounds);

        FillRectCommand2D[] direct = Record(surface).Commands.Cast<FillRectCommand2D>().ToArray();
        Assert.Contains(before.Zip(direct), pair => pair.First.Rect != pair.Second.Rect);
        using DrawingSceneReadLease2D lease = scene.Scene.Borrow();
        (Rect2D Rect, Matrix3x2 Transform)[] retained = ((IDrawingSceneSource2D)lease).Entries
            .SelectMany(entry => entry.Content.Commands.Cast<FillRectCommand2D>()
                .Select(command => (command.Rect, Transform: command.State.Transform * entry.Transform)))
            .ToArray();
        Assert.Equal(direct.Length, retained.Length);
        for (int i = 0; i < direct.Length; i++)
        {
            Assert.Equal(direct[i].Rect, retained[i].Rect);
            Assert.Equal(direct[i].State.Transform, retained[i].Transform);
        }
    }

    /// <summary>Checks retained border rectangles update when body bounds stay identical.</summary>
    [Fact]
    public void LayerOnlyGeometryRefreshesRetainedCommands()
    {
        using var surface = new UiSurface(Compose.SizedBox(width: 10, height: 8, with: [Compose.Align.At(0, 0), Compose.Border.Stroke(new(1, 0, 0, 1), 2), Compose.Align.At(0, 0)]));
        surface.SetViewport(new(100, 80));
        using UiDrawingScene2D scene = surface.CreateDrawingScene();
        scene.Update();
        Rect2D body = surface.RootElement.ContentBounds;
        surface.SetViewport(new(120, 90));
        scene.Update();
        Assert.Equal(body, surface.RootElement.ContentBounds);
        FillRectCommand2D[] direct = Record(surface).Commands.Cast<FillRectCommand2D>().ToArray();
        using DrawingSceneReadLease2D lease = scene.Scene.Borrow();
        DrawingSceneEntry2D entry = Assert.Single(((IDrawingSceneSource2D)lease).Entries, e => e.Content.Count != 0);
        FillRectCommand2D[] retained = entry.Content.Commands.Cast<FillRectCommand2D>().ToArray();
        Assert.Equal(direct.Length, retained.Length);
        for (int i = 0; i < direct.Length; i++)
        {
            Assert.Equal(direct[i].Rect, retained[i].Rect);
            Assert.Equal(direct[i].State.Transform, retained[i].State.Transform * entry.Transform);
        }
    }

    private static DrawList2D Record(UiSurface surface)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        return canvas.End();
    }

    private sealed class RuntimeMeasureWidget : Compose.Definitions.Widget
    {
        internal RuntimeMeasureElement? Element { get; private set; }

        protected override UiElement CreateElement() => Element = new(this);
    }

    private sealed class RuntimeMeasureElement(RuntimeMeasureWidget widget) : UiElement<RuntimeMeasureWidget>(widget)
    {
        private Vector2 _measured = new(10, 8);

        internal void Measure(Vector2 measured)
        {
            _measured = measured;
            InvalidateLayout();
        }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(_measured);
    }
}
