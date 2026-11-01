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

    /// <summary>Checks a component can interleave itself and arbitrary groups independently of registration order.</summary>
    [Fact]
    public void ComponentsControlTheirOwnChildPaintingOrder()
    {
        var root = new LayeredWidget { ClipToBounds = true };
        root.Foreground.Initialize(Rectangle(width: 10, height: 10, fill: _red));
        root.Embeddeds.Initialize(Rectangle(width: 10, height: 10, fill: _blue));
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        FillRectCommand2D[] commands = Record(surface).Commands.Cast<FillRectCommand2D>().ToArray();
        Assert.Equal(new[] { _blue, _green, _red }, commands.Select(Color));
        Assert.Equal(Matrix3x2.Identity, commands[0].State.Transform);
        Assert.Equal(new Vector2(3, 4), commands[1].State.Transform.Translation);
        Assert.Equal(Matrix3x2.Identity, commands[2].State.Transform);
        Assert.All(commands, command => Assert.Equal(2, command.State.Clip.Elements.Count));
    }

    /// <summary>Checks custom record helpers cannot traverse unowned children or children of another component.</summary>
    [Fact]
    public void RecordChildRequiresDirectOwnership()
    {
        using var surface = new UiSurface(new LayeredWidget());
        using var foreignSurface = new UiSurface(Panel()[Rectangle(fill: _red)]);
        using var unownedSurface = new UiSurface(Rectangle(fill: _blue));
        LayeredElement element = Assert.IsType<LayeredElement>(surface.RootElement);
        var canvas = new Canvas2D();
        canvas.Begin();
        Assert.Throws<ArgumentNullException>(() => element.TryRecord(null!, canvas));
        Assert.Throws<ArgumentException>(() => element.TryRecord(foreignSurface.RootElement.Children[0], canvas));
        Assert.Throws<ArgumentException>(() => element.TryRecord(unownedSurface.RootElement, canvas));
        Assert.Empty(canvas.End().Commands);
    }

    /// <summary>Checks declaration setters have no phase validation while keeping the recorded values unchanged.</summary>
    [Fact]
    public void DeclarationSettersDoNotInspectTheRecordingPhase()
    {
        Definitions.Rectangle rectangle = Rectangle(fill: _red, width: 10, height: 10);
        Definitions.Stack stack = Stack()[rectangle];
        CallbackPanel? root = null;
        UiSurface? surface = null;
        root = new CallbackPanel(_ =>
        {
            root!.Width = null;
            root.Height = null;
            root.IsVisible = true;
            rectangle.Fill = _red;
            rectangle.ClipToBounds = false;
            stack.Orientation = UiOrientation.Vertical;
            stack.Spacing = 0;
            surface!.SetViewport(new(100, 100));
        });
        root.Children.Initialize(stack);
        using (surface = new UiSurface(root))
        {
            surface.SetViewport(new(100, 100));
            FillRectCommand2D command = Assert.IsType<FillRectCommand2D>(Assert.Single(Record(surface).Commands));
            Assert.Equal(_red, Color(command));
        }
    }

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
        inner.Children.Initialize(Rectangle(fill: _red, width: 15, height: 10));
        var root = new OffsetPanel(new(3, 5)) { ClipToBounds = true };
        root.Children.Initialize(inner);
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
        Assert.Equal(new Rect2D(0, 80, 20, 80), surface.RootElement.Children[0].Children[1].Bounds);
        Assert.Equal(new Vector2(0, 80), command.State.Transform.Translation);
        Assert.Equal(clip ? 2 : 1, command.State.Clip.Elements.Count);
    }

    /// <summary>Checks an unclipped zero-sized parent still records its overflowing children.</summary>
    [Fact]
    public void ZeroSizedParentDoesNotCullAnOverflowingSubtree()
    {
        var parent = new OffsetPanel(new(-4, -5)) { Width = 0, Height = 0 };
        parent.Children.Initialize(Rectangle(fill: _red, width: 12, height: 13));
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
        });
        root.Children.Initialize(Rectangle(fill: _red, width: 10, height: 10), Rectangle(fill: _blue, width: 10, height: 10));
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
        var child = new CallbackPanel(_ => calls++) { IsVisible = false };
        child.Children.Initialize(new CallbackPanel(_ => calls++));
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

    /// <summary>Checks rebuilt visual declarations leave earlier recorded snapshots independent.</summary>
    [Fact]
    public void RebuiltVisualDeclarationsProduceIndependentCommandSnapshots()
    {
        static CallbackPanel Build(Color2D color, bool clip)
        {
            var root = new CallbackPanel(_ => { }) { ClipToBounds = clip };
            root.Children.Initialize(Rectangle(fill: color, width: 12, height: 8));
            return root;
        }

        CallbackPanel previous = Build(_red, false);
        using var previousSurface = new UiSurface(previous);
        previousSurface.SetViewport(new(100, 100));
        DrawList2D first = Record(previousSurface);
        CallbackPanel next = Build(_blue, true);
        using var nextSurface = new UiSurface(next);
        nextSurface.SetViewport(new(100, 100));
        DrawList2D second = Record(nextSurface);
        Record(nextSurface);
        Assert.Equal(1, previous.LayoutCalls);
        Assert.Equal(1, next.LayoutCalls);
        Assert.Equal(2, next.RecordCalls);
        Assert.Equal(_red, Color(Assert.IsType<FillRectCommand2D>(Assert.Single(first.Commands))));
        Assert.Equal(_blue, Color(Assert.IsType<FillRectCommand2D>(Assert.Single(second.Commands))));
        Assert.Single(first.Commands[0].State.Clip.Elements);
        Assert.Equal(2, second.Commands[0].State.Clip.Elements.Count);
        Assert.Equal(new Rect2D(0, 0, 12, 8), Assert.IsType<FillRectCommand2D>(first.Commands[0]).Rect);
    }

    /// <summary>Checks null, Begin and disposed validation.</summary>
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
        surface.SetViewport(new(100, 100));
        surface.Record(canvas);
        canvas.End();
        surface.Dispose();
        Assert.Throws<ObjectDisposedException>(() => surface.Record(canvas));
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

    /// <summary>Checks replacing configuration on retained instances cannot change an already recorded frame.</summary>
    [Fact]
    public void RetainedElementsRecordTheCurrentConfigurationIntoIndependentCommands()
    {
        using var surface = new UiSurface(Panel()[Rectangle(width: 12, height: 8, fill: _red)]);
        surface.SetViewport(new(100, 100));
        DrawList2D first = Record(surface);
        UiElement root = surface.RootElement;
        UiElement child = root.Children[0];
        surface.Rebuild(Panel()[Rectangle(width: 12, height: 8, fill: _blue)]);
        DrawList2D second = Record(surface);
        Assert.Same(root, surface.RootElement);
        Assert.Same(child, root.Children[0]);
        Assert.Equal(_red, Color(Assert.IsType<FillRectCommand2D>(Assert.Single(first.Commands))));
        Assert.Equal(_blue, Color(Assert.IsType<FillRectCommand2D>(Assert.Single(second.Commands))));
    }

    private static Color2D Color(FillRectCommand2D command) => Assert.IsType<SolidPaintNode2D>(command.Paint.Root).Color;

    private static DrawList2D Record(UiSurface surface)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        return canvas.End();
    }

    private sealed class LayeredWidget : Widget
    {
        public LayeredWidget()
        {
            Foreground = CreateChildren();
            Embeddeds = CreateChildren();
        }

        public WidgetChildren Foreground { get; }

        public WidgetChildren Embeddeds { get; }

        protected override UiElement CreateElement() => new LayeredElement(this);
    }

    private sealed class LayeredElement(LayeredWidget widget) : UiElement<LayeredWidget>(widget)
    {
        public void TryRecord(UiElement child, DrawingCanvas2D canvas) => RecordChild(child, canvas);

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            foreach (UiElement child in GetChildren(Widget.Foreground).Concat(GetChildren(Widget.Embeddeds)))
            {
                LayoutChild(child, constraints.Loosen());
                SetChildOffset(child, Vector2.Zero);
            }

            return constraints.Constrain(Vector2.Zero);
        }

        protected override void RecordOverride(DrawingCanvas2D canvas)
        {
            foreach (UiElement child in GetChildren(Widget.Embeddeds))
            {
                RecordChild(child, canvas);
            }

            RecordSelf(canvas);
            foreach (UiElement child in GetChildren(Widget.Foreground))
            {
                RecordChild(child, canvas);
            }
        }

        protected override void OnRecord(DrawingCanvas2D canvas)
        {
            canvas.Translate(new(3, 4));
            canvas.FillRect(new(0, 0, 10, 10), _green);
        }
    }

    private sealed class PaintedButton : Definitions.Button
    {
        protected override UiElement CreateElement() => new PaintedButtonElement(this);
    }

    private sealed class PaintedButtonElement(PaintedButton widget) : ButtonElement(widget)
    {
        protected override void OnRecord(DrawingCanvas2D canvas) => canvas.FillRect(new(0, 0, Size.X, Size.Y), _green);
    }

    private sealed class OffsetPanel(Vector2 offset) : Definitions.Panel
    {
        public Vector2 Offset { get; } = offset;

        protected override UiElement CreateElement() => new OffsetElement(this);
    }

    private sealed class OffsetElement(OffsetPanel widget) : UiElement<OffsetPanel>(widget)
    {
        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            foreach (UiElement child in GetChildren(Widget.Children))
            {
                LayoutChild(child, new UiBoxConstraints(0, float.PositiveInfinity, 0, float.PositiveInfinity));
                SetChildOffset(child, Widget.Offset);
            }

            return constraints.Constrain(Vector2.Zero);
        }
    }

    private sealed class CallbackPanel(Action<DrawingCanvas2D> callback) : Definitions.Panel
    {
        public CallbackElement Runtime { get; private set; } = null!;

        public int LayoutCalls => Runtime.LayoutCalls;

        public int RecordCalls => Runtime.RecordCalls;

        public Action<DrawingCanvas2D> Callback { get; } = callback;

        public Action? BeforeLayout { get; set; }

        protected override UiElement CreateElement() => Runtime = new(this);
    }

    private sealed class CallbackElement(CallbackPanel widget) : PanelElement(widget)
    {
        public int LayoutCalls { get; private set; }

        public int RecordCalls { get; private set; }

        private CallbackPanel Configuration => (CallbackPanel)Widget;

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            LayoutCalls++;
            Configuration.BeforeLayout?.Invoke();
            return base.LayoutOverride(constraints);
        }

        protected override void OnRecord(DrawingCanvas2D canvas)
        {
            RecordCalls++;
            Configuration.Callback(canvas);
        }
    }
}
