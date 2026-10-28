using System.Numerics;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.UI;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks UI recording against immutable Drawing2D command snapshots across the library boundary.</summary>
public sealed class UiRecordingTests
{
    private static readonly Color2D _red = new(1, 0, 0, 0.5f);
    private static readonly Color2D _green = new(0, 1, 0, 0.5f);
    private static readonly Color2D _blue = new(0, 0, 1, 0.5f);

    /// <summary>Checks static construction, inherited parameters and transparent defaults.</summary>
    [Fact]
    public void RectangleFactoriesExposeInheritedParametersAndDefaultToTransparent()
    {
        Definitions.Rectangle rectangle = Rectangle(width: 12, height: 8, clipToBounds: true);
        using var surface = new UiSurface(Panel()[rectangle]);
        surface.SetViewport(new(100, 100));
        Assert.Empty(Record(surface).Commands);
        rectangle.Fill = _red;
        FillRectCommand2D command = Assert.IsType<FillRectCommand2D>(Assert.Single(Record(surface).Commands));
        Assert.Equal(new Rect2D(0, 0, 12, 8), command.Rect);
        Assert.Equal(_red, Color(command));
        Assert.Equal(2, command.State.Clip.Elements.Count);
    }

    /// <summary>Checks background order, custom drawing, and content order without sorting translucent colors.</summary>
    [Fact]
    public void PainterOrderIsBackgroundThenSelfThenContent()
    {
        var button = new PaintedButton();
        _ = button[Rectangle(fill: _blue, width: 20, height: 10), Button.Background()[Rectangle(fill: _red), Rectangle(fill: _blue)]];
        using var surface = new UiSurface(Panel()[button, Rectangle(fill: _red, width: 1, height: 1)]);
        surface.SetViewport(new(100, 100));
        FillRectCommand2D[] commands = Record(surface).Commands.Cast<FillRectCommand2D>().ToArray();
        Assert.Equal(new[] { _red, _blue, _green, _blue, _red }, commands.Select(Color));
        Assert.All(commands.Take(4), command => Assert.Equal(new Rect2D(0, 0, 20, 10), command.Rect));
    }

    /// <summary>Checks accumulated origins, fixed parent clips, viewport clips and the host's affine transform.</summary>
    [Fact]
    public void NestedOriginsAndClipsComposeInsideTheHostTransform()
    {
        var inner = new OffsetPanel(new(7, 11)) { Width = 30, Height = 20, ClipToBounds = true };
        inner.Children = [Rectangle(fill: _red, width: 15, height: 10)];
        var root = new OffsetPanel(new(3, 5)) { ClipToBounds = true, Children = [inner] };
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 80));
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Translate(new(13, 17));
        canvas.Scale(new(2, 3));
        canvas.ClipRect(new(-1, -2, 500, 500));
        surface.Record(canvas);
        canvas.FillRect(new(0, 0, 1, 1), _blue);
        DrawList2D list = canvas.End();
        FillRectCommand2D command = Assert.IsType<FillRectCommand2D>(list.Commands[0]);
        Matrix3x2 host = Matrix3x2.CreateScale(2, 3) * Matrix3x2.CreateTranslation(13, 17);
        Assert.Equal(Matrix3x2.CreateTranslation(10, 16) * host, command.State.Transform);
        Assert.Equal(4, command.State.Clip.Elements.Count);
        Assert.Equal(host, command.State.Clip.Elements[1].Transform);
        Assert.Equal(host, command.State.Clip.Elements[2].Transform);
        Assert.Equal(Matrix3x2.CreateTranslation(3, 5) * host, command.State.Clip.Elements[3].Transform);
        Assert.Equal(new Vector2(100, 0), command.State.Clip.Elements[1].Path.Segments[1].P0);
        DrawingCommand2D following = list.Commands[1];
        Assert.Equal(host, following.State.Transform);
        Assert.Single(following.State.Clip.Elements);
    }

    /// <summary>Checks clipping changes only the recorded clip and never shrinks overflowing children.</summary>
    /// <param name="clip">Whether the stack clips its bounds.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverflowRetainsTheSecondChildsSize(bool clip)
    {
        Definitions.Rectangle second = Rectangle(fill: _blue, height: 80, width: 20);
        Definitions.Stack stack = Stack(width: 40, height: 100, clipToBounds: clip)[Rectangle(fill: _red, height: 80, width: 20), second];
        using var surface = new UiSurface(Panel()[stack]);
        surface.SetViewport(new(200, 200));
        FillRectCommand2D command = Assert.IsType<FillRectCommand2D>(Record(surface).Commands[1]);
        Assert.Equal(new Rect2D(0, 0, 20, 80), command.Rect);
        Assert.Equal(new Rect2D(0, 80, 20, 80), second.Bounds);
        Assert.Equal(new Vector2(0, 80), command.State.Transform.Translation);
        Assert.Equal(clip ? 2 : 1, command.State.Clip.Elements.Count);
    }

    /// <summary>Checks an unclipped zero-sized parent still records its overflowing children.</summary>
    [Fact]
    public void ZeroSizedParentDoesNotCullAnOverflowingSubtree()
    {
        var parent = new OffsetPanel(new(-4, -5)) { Width = 0, Height = 0, Children = [Rectangle(fill: _red, width: 12, height: 13)] };
        using var surface = new UiSurface(Panel()[parent]);
        surface.SetViewport(new(100, 100));
        FillRectCommand2D command = Assert.IsType<FillRectCommand2D>(Assert.Single(Record(surface).Commands));
        Assert.Equal(new Vector2(-4, -5), command.State.Transform.Translation);
        Assert.Equal(new Rect2D(0, 0, 12, 13), command.Rect);
        Assert.Single(command.State.Clip.Elements);
    }

    /// <summary>Checks custom local transforms and clips do not move or clip contents or siblings.</summary>
    [Fact]
    public void CustomDrawingStateIsIsolatedFromChildrenAndSiblings()
    {
        var root = new CallbackPanel(canvas =>
        {
            canvas.Translate(new(90, 91));
            canvas.ClipRect(new(0, 0, 1, 1));
            canvas.Save();
            canvas.FillRect(new(0, 0, 2, 2), _green);
            canvas.Restore();
        })
        { Children = [Rectangle(fill: _red, width: 10, height: 10), Rectangle(fill: _blue, width: 10, height: 10)] };
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        DrawList2D list = Record(surface);
        Assert.Equal(new Vector2(90, 91), list.Commands[0].State.Transform.Translation);
        Assert.Equal(2, list.Commands[0].State.Clip.Elements.Count);
        Assert.All(list.Commands.Skip(1), command =>
        {
            Assert.Equal(Matrix3x2.Identity, command.State.Transform);
            Assert.Single(command.State.Clip.Elements);
        });
    }

    /// <summary>Checks hidden subtrees and zero viewport dimensions omit custom callbacks.</summary>
    [Fact]
    public void HiddenSubtreesAndZeroViewportDoNotInvokeCallbacks()
    {
        int calls = 0;
        var child = new CallbackPanel(_ => calls++) { IsVisible = false, Children = [new CallbackPanel(_ => calls++)] };
        using var surface = new UiSurface(Panel()[child]);
        surface.SetViewport(new(100, 100));
        Assert.Empty(Record(surface).Commands);
        child.IsVisible = true;
        surface.SetViewport(new(0, 100));
        Assert.Empty(Record(surface).Commands);
        surface.SetViewport(new(100, 0));
        Assert.Empty(Record(surface).Commands);
        Assert.Equal(0, calls);
        surface.SetViewport(new(100, 100));
        Record(surface);
        Assert.Equal(2, calls);
    }

    /// <summary>Checks transparent and zero-sized rectangles produce no fill commands.</summary>
    [Fact]
    public void EmptyRectanglesAreSkipped()
    {
        using var surface = new UiSurface(Panel()[Rectangle(fill: _red, width: 0, height: 10), Rectangle(fill: _red, width: 10, height: 0), Rectangle(width: 10, height: 10)]);
        surface.SetViewport(new(100, 100));
        Assert.Empty(Record(surface).Commands);
    }

    /// <summary>Checks every color component is validated before updating the previous fill.</summary>
    /// <param name="component">The component to invalidate.</param>
    /// <param name="invalid">The invalid value.</param>
    [Theory]
    [InlineData(0, float.NaN)]
    [InlineData(1, float.NaN)]
    [InlineData(2, float.NaN)]
    [InlineData(3, float.NaN)]
    [InlineData(0, float.PositiveInfinity)]
    [InlineData(1, float.NegativeInfinity)]
    [InlineData(2, -0.01f)]
    [InlineData(3, 1.01f)]
    public void InvalidFillPreservesPreviousColor(int component, float invalid)
    {
        Definitions.Rectangle rectangle = Rectangle(fill: _red);
        float[] values = [0, 0, 0, 0];
        values[component] = invalid;
        Assert.Throws<ArgumentOutOfRangeException>(() => rectangle.Fill = new(values[0], values[1], values[2], values[3]));
        Assert.Equal(_red, rectangle.Fill);
    }

    /// <summary>Checks visual changes are redrawn without causing another layout and snapshots remain independent.</summary>
    [Fact]
    public void FillAndClipChangesDoNotInvalidateLayoutButEveryRecordIsFresh()
    {
        Definitions.Rectangle rectangle = Rectangle(fill: _red, width: 12, height: 8);
        var root = new CallbackPanel(_ => { }) { Children = [rectangle] };
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        DrawList2D first = Record(surface);
        rectangle.Fill = _blue;
        root.ClipToBounds = true;
        DrawList2D second = Record(surface);
        Assert.Equal(1, root.LayoutCalls);
        Assert.Equal(2, root.RecordCalls);
        Assert.Equal(_red, Color(Assert.IsType<FillRectCommand2D>(Assert.Single(first.Commands))));
        Assert.Equal(_blue, Color(Assert.IsType<FillRectCommand2D>(Assert.Single(second.Commands))));
        Assert.Single(first.Commands[0].State.Clip.Elements);
        Assert.Equal(2, second.Commands[0].State.Clip.Elements.Count);
        rectangle.Width = 20;
        surface.Dispose();
        Assert.Equal(new Rect2D(0, 0, 12, 8), Assert.IsType<FillRectCommand2D>(first.Commands[0]).Rect);
    }

    /// <summary>Checks null, Begin, disposed and layout-time reentrancy validation.</summary>
    [Fact]
    public void RecordValidatesItsLifecycleBeforeInvokingLayout()
    {
        var root = new CallbackPanel(_ => { });
        using var surface = new UiSurface(root);
        var canvas = new Canvas2D();
        Assert.Throws<ArgumentNullException>(() => surface.Record(null!));
        Assert.Throws<InvalidOperationException>(() => surface.Record(canvas));
        Assert.Equal(0, root.LayoutCalls);
        canvas.Begin();
        root.BeforeLayout = () => Assert.Throws<InvalidOperationException>(() => surface.Record(canvas));
        surface.SetViewport(new(100, 100));
        surface.Record(canvas);
        canvas.End();
        surface.Dispose();
        Assert.Throws<ObjectDisposedException>(() => surface.Record(canvas));
    }

    /// <summary>Checks built-in property and structure mutation, nested passes and disposal are denied while recording.</summary>
    /// <param name="operation">The attempted operation.</param>
    [Theory]
    [InlineData("fill")]
    [InlineData("clip")]
    [InlineData("size")]
    [InlineData("visible")]
    [InlineData("children")]
    [InlineData("viewport")]
    [InlineData("layout")]
    [InlineData("record")]
    [InlineData("dispose")]
    public void CallbackCannotMutateOrReenterTheSurface(string operation)
    {
        Definitions.Rectangle rectangle = Rectangle(fill: _red, width: 10, height: 10);
        UiSurface? surface = null;
        var canvas = new Canvas2D();
        var root = new CallbackPanel(_ =>
        {
            Action action = operation switch
            {
                "fill" => () => rectangle.Fill = _blue,
                "clip" => () => rectangle.ClipToBounds = true,
                "size" => () => rectangle.Width = 50,
                "visible" => () => rectangle.IsVisible = false,
                "children" => () => ((Definitions.Panel)surface!.Root).Children = [],
                "viewport" => () => surface!.SetViewport(new(50, 50)),
                "layout" => () => surface!.UpdateLayout(),
                "record" => () => surface!.Record(canvas),
                "dispose" => () => surface!.Dispose(),
                _ => throw new ArgumentException(operation),
            };
            Assert.Throws<InvalidOperationException>(action);
        })
        { Children = [rectangle] };
        using (surface = new UiSurface(root))
        {
            surface.SetViewport(new(100, 100));
            canvas.Begin();
            surface.Record(canvas);
            Assert.Single(canvas.End().Commands);
        }

        Assert.Equal(_red, rectangle.Fill);
        Assert.False(rectangle.ClipToBounds);
        Assert.Equal(10, rectangle.Width);
        Assert.True(rectangle.IsVisible);
        Assert.Same(root, rectangle.Parent);
    }

    /// <summary>Checks exceptions restore caller state, retain prior commands and permit Cancel plus a new frame.</summary>
    [Fact]
    public void CallbackFailureRestoresStateAndTheCallerCanCancelAndRetry()
    {
        bool fail = true;
        var root = new CallbackPanel(canvas =>
        {
            canvas.Translate(new(20, 30));
            canvas.ClipRect(new(0, 0, 3, 3));
            canvas.FillRect(new(0, 0, 2, 2), _green);
            if (fail)
            {
                throw new ArithmeticException("custom drawing failed");
            }
        });
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Translate(new(7, 8));
        canvas.Save();
        Assert.Throws<ArithmeticException>(() => surface.Record(canvas));
        canvas.FillRect(new(0, 0, 1, 1), _red);
        canvas.Restore();
        DrawList2D failed = canvas.End();
        Assert.Equal(2, failed.Commands.Count);
        Assert.Equal(new Vector2(7, 8), failed.Commands[1].State.Transform.Translation);
        Assert.Empty(failed.Commands[1].State.Clip.Elements);
        canvas.Begin();
        Assert.Throws<ArithmeticException>(() => surface.Record(canvas));
        canvas.Cancel();
        fail = false;
        root.Width = 50;
        Assert.Single(Record(surface).Commands);
    }

    /// <summary>Checks failed layout restores the canvas and does not record UI commands.</summary>
    [Fact]
    public void LayoutFailureLeavesTheCallerRecordingUsable()
    {
        var root = new CallbackPanel(_ => { }) { BeforeLayout = () => throw new ArithmeticException() };
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Translate(new(9, 10));
        Assert.Throws<ArithmeticException>(() => surface.Record(canvas));
        canvas.FillRect(new(0, 0, 1, 1), _red);
        Assert.Equal(new Vector2(9, 10), Assert.Single(canvas.End().Commands).State.Transform.Translation);
        root.BeforeLayout = null;
        Assert.Empty(Record(surface).Commands);
    }

    /// <summary>Checks multiple surfaces append to a caller's recording without removing earlier content or carrying clips.</summary>
    [Fact]
    public void MultipleSurfacesAppendWithoutClearingOrLeakingState()
    {
        using var first = new UiSurface(Rectangle(fill: _red));
        using var second = new UiSurface(Rectangle(fill: _blue));
        first.SetViewport(new(10, 20));
        second.SetViewport(new(30, 40));
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 100, 100), _green);
        first.Record(canvas);
        canvas.Translate(new(50, 60));
        second.Record(canvas);
        DrawList2D list = canvas.End();
        Assert.Equal(new[] { _green, _red, _blue }, list.Commands.Cast<FillRectCommand2D>().Select(Color));
        Assert.Empty(list.Commands[0].State.Clip.Elements);
        Assert.Single(list.Commands[1].State.Clip.Elements);
        Assert.Single(list.Commands[2].State.Clip.Elements);
        Assert.Equal(new Vector2(50, 60), list.Commands[2].State.Transform.Translation);
    }

    /// <summary>Checks the same recorder works within a specialized canvas layer without consuming caller scopes.</summary>
    [Fact]
    public void SpecializedCanvasLayerKeepsItsOwnSaveBoundary()
    {
        using var surface = new UiSurface(Rectangle(fill: _red));
        surface.SetViewport(new(10, 20));
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.Save();
        canvas.BeginLayer(new() { ContentBounds = new(0, 0, 100, 100) });
        canvas.Save();
        surface.Record(canvas);
        canvas.Restore();
        canvas.EndLayer();
        canvas.Restore();
        DrawList2D list = canvas.End().Commands;
        Assert.Equal(3, list.Commands.Count);
        Assert.IsType<BeginLayerCommand2D>(list.Commands[0]);
        Assert.IsType<FillRectCommand2D>(list.Commands[1]);
        Assert.IsType<EndLayerCommand2D>(list.Commands[2]);
    }

    /// <summary>Checks the same connected surface and canvas support sequential use from a worker thread.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task RecordingHasNoCreatorThreadAffinityAsync()
    {
        Definitions.Rectangle rectangle = Rectangle(fill: _red);
        using var surface = new UiSurface(rectangle);
        surface.SetViewport(new(10, 20));
        var canvas = new Canvas2D();
        await Task.Run(() =>
        {
            rectangle.Fill = _blue;
            canvas.Begin();
            surface.Record(canvas);
        });
        Assert.Equal(_blue, Color(Assert.IsType<FillRectCommand2D>(Assert.Single(canvas.End().Commands))));
    }

    private static Color2D Color(FillRectCommand2D command) => Assert.IsType<SolidPaintNode2D>(command.Paint.Root).Color;

    private static DrawList2D Record(UiSurface surface)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        return canvas.End();
    }

    private sealed class PaintedButton : Definitions.Button
    {
        protected override void OnRecord(DrawingCanvas2D canvas) => canvas.FillRect(new(0, 0, Size.X, Size.Y), _green);
    }

    private sealed class OffsetPanel(Vector2 offset) : Definitions.Panel
    {
        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            foreach (Widget child in Children)
            {
                LayoutChild(child, new UiBoxConstraints(0, float.PositiveInfinity, 0, float.PositiveInfinity));
                SetChildOffset(child, offset);
            }

            return constraints.Constrain(Vector2.Zero);
        }
    }

    private sealed class CallbackPanel(Action<DrawingCanvas2D> callback) : Definitions.Panel
    {
        public int LayoutCalls { get; private set; }

        public int RecordCalls { get; private set; }

        public Action? BeforeLayout { get; set; }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            LayoutCalls++;
            BeforeLayout?.Invoke();
            return base.LayoutOverride(constraints);
        }

        protected override void OnRecord(DrawingCanvas2D canvas)
        {
            RecordCalls++;
            callback(canvas);
        }
    }
}
