using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Media.Compose;

namespace Lumyte.UI.Media.Tests;

/// <summary>Checks actual visual replacement slots on mounted surfaces.</summary>
public sealed class VisualSlotTests
{
    /// <summary>Empty slots remove primitives, not merely cover them.</summary>
    [Fact]
    public void EmptySlotsHideDefaultsAndNonDrawingPartsReplaceThem()
    {
        using var surface = new UiSurface(ProgressBar(value: 0.5));
        surface.SetViewport(new(120, 4));
        Assert.Equal(2, Record(surface).Count);
        surface.Rebuild(ProgressBar(value: 0.5)[ProgressBar.Track()[Array.Empty<Lumyte.UI.Compose.Definitions.Widget>()], ProgressBar.Fill()[Array.Empty<Lumyte.UI.Compose.Definitions.Widget>()]]);
        surface.UpdateTree();
        Assert.Equal(0, Record(surface).Count);
        surface.Rebuild(ProgressBar(value: 0.5)[ProgressBar.Track()[Panel()], ProgressBar.Fill()[Panel()]]);
        surface.UpdateTree();
        Assert.Equal(0, Record(surface).Count);
        surface.Rebuild(ProgressBar(value: 0.5)[ProgressBar.Track()[Array.Empty<Lumyte.UI.Compose.Definitions.Widget>()]]);
        surface.UpdateTree();
        Assert.Equal(1, Record(surface).Count);
    }

    /// <summary>Decoration cannot supply intrinsic size or receive input, focus or semantics.</summary>
    [Fact]
    public void PartsFollowValueWithoutMeasuringOrOwningInput()
    {
        var value = Signal.Create(0.25);
        Compose.Definitions.ProgressBar declaration = ProgressBar(value: value)[
            ProgressBar.Track()[Button(testId: "track", width: 900, height: 900)],
            ProgressBar.Fill()[Button(testId: "fill", width: 900, height: 900)]];
        using var surface = new UiSurface(Panel()[declaration]);
        surface.SetViewport(new(300, 100));
        surface.UpdateLayout();
        UiElement progress = Assert.Single(surface.RootElement.Children);
        Assert.Equal(new Vector2(120, 4), progress.Size);
        Assert.Equal(new Vector2(120, 4), progress.Children[0].Size);
        Assert.Equal(new Vector2(30, 4), progress.Children[1].Size);
        Assert.False(surface.TryFocus(progress.Children[0]));
        Assert.False(surface.TryFocus(progress.Children[1]));
        Assert.DoesNotContain(surface.CaptureAutomationSnapshot().Nodes, node => node.TestId is "track" or "fill");
        value.Value = 0.75;
        surface.UpdateTree();
        surface.UpdateLayout();
        Assert.Equal(new Vector2(90, 4), progress.Children[1].Size);
        value.Value = 0;
        surface.UpdateTree();
        surface.UpdateLayout();
        Assert.Equal(0, ((Compose.Definitions.ProgressBar)progress.Widget).Value);
        Assert.Equal(2, progress.Children.Count);
        Assert.Equal(new Vector2(0, 4), progress.Children[1].Size);
    }

    /// <summary>Shared declarations mount independently and failed preparation does not publish replacement parts.</summary>
    [Fact]
    public void SharedPartsAndInvalidRebuildAreAtomic()
    {
        Compose.Definitions.ProgressBar declaration = ProgressBar(value: 0.25)[ProgressBar.Fill()[Rectangle(fill: new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1))]];
        using var first = new UiSurface(declaration);
        using var second = new UiSurface(declaration);
        first.SetViewport(new(100, 8));
        second.SetViewport(new(200, 12));
        first.UpdateLayout();
        second.UpdateLayout();
        UiElement child = Assert.Single(first.RootElement.Children);
        Assert.NotSame(child, Assert.Single(second.RootElement.Children));
        Assert.Equal(new Vector2(25, 8), child.Size);
        Assert.Equal(new Vector2(50, 12), second.RootElement.Children[0].Size);
        first.Rebuild(ProgressBar(value: 0.5)[ProgressBar.Fill()[new InvalidPart()]]);
        Assert.Throws<ArgumentOutOfRangeException>(first.UpdateTree);
        Assert.Same(child, first.RootElement.Children[0]);
        Assert.Equal(0.25, ((Compose.Definitions.ProgressBar)first.Root).Value);
        first.Rebuild(ProgressBar(value: 1)[ProgressBar.Fill()[Rectangle(fill: new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1))]]);
        first.UpdateTree();
        first.UpdateLayout();
        Assert.Same(child, first.RootElement.Children[0]);
        Assert.Equal(new Vector2(100, 8), child.Size);
        Assert.Equal(new Vector2(50, 12), second.RootElement.Children[0].Size);
    }

    /// <summary>Capacity rejection leaves storage available for a valid initialization.</summary>
    [Fact]
    public void InvalidChildrenDoNotInitializeSlots()
    {
        Compose.Definitions.ProgressBar declaration = ProgressBar();
        Assert.Throws<ArgumentException>(() => declaration.TrackChildren.Initialize(Panel(), Panel()));
        Assert.Throws<ArgumentException>(() => declaration.FillChildren.Initialize([null!]));
        declaration.TrackChildren.Initialize(Panel());
        declaration.FillChildren.Initialize(Panel());
        Assert.Single(declaration.TrackChildren);
        Assert.Single(declaration.FillChildren);
    }

    /// <summary>Oversized custom drawing is clipped to the value-dependent fill region.</summary>
    [Fact]
    public void CustomFillDrawingIsClippedAndZeroDoesNotRecordIt()
    {
        var value = Signal.Create(0.25);
        DrawingState2D? state = null;
        int calls = 0;
        var fill = new ProbePart
        {
            Record = canvas =>
            {
                state = ((ProbeCanvas)canvas).Current;
                calls++;
            },
        };
        using var surface = new UiSurface(ProgressBar(value: value)[ProgressBar.Track()[Array.Empty<Lumyte.UI.Compose.Definitions.Widget>()], ProgressBar.Fill()[fill]]);
        surface.SetViewport(new(100, 8));
        var canvas = new ProbeCanvas();
        canvas.Begin();
        surface.Record(canvas);
        Assert.Equal(1, canvas.End().Count);
        Assert.Equal(1, calls);
        Assert.NotNull(state);
        ClipElement2D clip = state.Clip.Elements[^1];
        Assert.Equal(25, clip.Path.Segments.Max(segment => segment.P0.X));
        Assert.Equal(8, clip.Path.Segments.Max(segment => segment.P0.Y));
        value.Value = 0.75;
        surface.UpdateTree();
        canvas.Begin();
        surface.Record(canvas);
        Assert.Equal(1, canvas.End().Count);
        Assert.Equal(2, calls);
        Assert.Equal(75, state!.Clip.Elements[^1].Path.Segments.Max(segment => segment.P0.X));
        value.Value = 0;
        surface.UpdateTree();
        canvas.Begin();
        surface.Record(canvas);
        Assert.Equal(0, canvas.End().Count);
        Assert.Equal(2, calls);
    }

    /// <summary>Repeated reconstruction preserves the distinction between omitted and explicitly empty visuals.</summary>
    /// <param name="empty">Whether to explicitly suppress both defaults.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReactiveReconstructionPreservesDefaultAndEmptySemantics(bool empty)
    {
        var value = Signal.Create(0.25);
        Compose.Definitions.ProgressBar declaration = ProgressBar(value: value);
        if (empty)
        {
            declaration = declaration[ProgressBar.Track()[Array.Empty<Lumyte.UI.Compose.Definitions.Widget>()], ProgressBar.Fill()[Array.Empty<Lumyte.UI.Compose.Definitions.Widget>()]];
        }

        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(100, 8));
        Assert.Equal(empty ? 0 : 2, Record(surface).Count);
        value.Value = 0.75;
        surface.UpdateTree();
        Assert.Equal(empty ? 0 : 2, Record(surface).Count);
        value.Value = 0.5;
        surface.UpdateTree();
        Assert.Equal(empty ? 0 : 2, Record(surface).Count);
    }

    /// <summary>Direct public storage initialization has the same visual and reactive semantics as named Slots.</summary>
    /// <param name="empty">Whether to hide both parts instead of supplying visual children.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectStorageInitializationReplacesDrawingAndSurvivesSignals(bool empty)
    {
        var value = Signal.Create(0.25);
        Compose.Definitions.ProgressBar declaration = ProgressBar(value: value);
        declaration.TrackChildren.Initialize(empty ? [] : [Panel()]);
        declaration.FillChildren.Initialize(empty ? [] : [Rectangle(fill: new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1))]);
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(100, 8));
        Assert.Equal(empty ? 0 : 1, Record(surface).Count);
        value.Value = 0.75;
        surface.UpdateTree();
        Assert.Equal(empty ? 0 : 1, Record(surface).Count);
        value.Value = 0.5;
        surface.UpdateTree();
        Assert.Equal(empty ? 0 : 1, Record(surface).Count);
        if (!empty)
        {
            Assert.Equal(new Vector2(50, 8), surface.RootElement.Children[1].Size);
        }
    }

    private static DrawList2D Record(UiSurface surface)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        return canvas.End();
    }

    private sealed class ProbeCanvas : DrawingCanvas2D
    {
        public DrawingState2D Current => State;

        public DrawList2D End() => CompleteRecording();
    }

    private sealed class ProbePart : Lumyte.UI.Compose.Definitions.Widget
    {
        public Action<DrawingCanvas2D>? Record { get; init; }

        protected override UiElement CreateElement() => new ProbeElement(this);
    }

    private sealed class ProbeElement(ProbePart widget) : UiElement<ProbePart>(widget)
    {
        protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(new(900));

        protected override void OnRecord(DrawingCanvas2D canvas)
        {
            Widget.Record?.Invoke(canvas);
            canvas.FillRect(new(-100, -100, 900, 900), new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1));
        }
    }

    private sealed class InvalidPart : Lumyte.UI.Compose.Definitions.Panel
    {
        protected override void ValidateDeclaration(UiWidgetValidationContext context) => throw new ArgumentOutOfRangeException(nameof(context));
    }
}
