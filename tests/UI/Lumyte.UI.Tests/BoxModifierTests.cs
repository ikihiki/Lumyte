using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Lumyte.UI;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace C1.PublicConsumer.Tests;

/// <summary>Exercises generated actions across the public assembly boundary.</summary>
public sealed class BoxModifierTests
{
    /// <summary>Checks repeated order places borders at their own layer rectangles.</summary>
    [Fact]
    public void BorderOrderChangesRecordedGeometryAndRetainedMatchesDirect()
    {
        using var outerPadding = new UiSurface(Panel()[SizedBox(width: 10, height: 8, with: [Padding.All(3), Border.Stroke(new(1, 0, 0, 1), 2)])]);
        using var outerBorder = new UiSurface(Panel()[SizedBox(width: 10, height: 8, with: [Border.Stroke(new(1, 0, 0, 1), 2), Padding.All(3)])]);
        outerPadding.SetViewport(new(100, 100));
        outerBorder.SetViewport(new(100, 100));
        var canvas = new Canvas2D();
        canvas.Begin();
        outerPadding.Record(canvas);
        DrawList2D padded = canvas.End();
        canvas.Begin();
        outerBorder.Record(canvas);
        DrawList2D bordered = canvas.End();
        Assert.Equal(4, padded.Count);
        Assert.Equal(4, bordered.Count);
        Assert.Equal(new Vector2(20, 18), outerPadding.RootElement.Children[0].Size);
        Assert.Equal(outerPadding.RootElement.Children[0].ContentBounds, outerBorder.RootElement.Children[0].ContentBounds);
        using UiDrawingScene2D drawing = outerPadding.CreateDrawingScene();
        drawing.Update();
        drawing.Update();
        Assert.Equal(0, drawing.LastRecordingCount);
    }

    /// <summary>Checks unbounded layout measures children once and honors body-dependent height.</summary>
    [Fact]
    public void UnboundedAxisAndWidthDependentHeightUseOneChildPass()
    {
        var child = new MeasureWidget();
        using var surface = new UiSurface(Stack(orientation: UiOrientation.Vertical)[SizedBox(width: 30, with: [Padding.All(4)])[child]]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Equal(1, child.Calls);
        UiElement box = surface.RootElement.Children[0];
        Assert.Equal(new Vector2(38, 68), box.Size);
        Assert.Equal(new Vector2(30, 60), box.ContentSize);
    }

    /// <summary>Checks border color alone does not cause a layout pass.</summary>
    [Fact]
    public void ColorOnlyUpdatePreservesLayoutCache()
    {
        using var surface = new UiSurface(Panel()[SizedBox(width: 10, height: 10, with: [Border.Stroke(new(1, 0, 0, 1), 2)])]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        UiElement child = surface.RootElement.Children[0];
        surface.Rebuild(Panel()[SizedBox(width: 10, height: 10, with: [Border.Stroke(new(0, 1, 0, 1), 2)])]);
        surface.UpdateTree();
        Assert.False(child.IsLayoutDirty);
        surface.UpdateLayout();
        Assert.Equal(new Vector2(14, 14), child.Size);
    }

    /// <summary>Checks single assignment, cardinality and invalid configuration rejection.</summary>
    [Fact]
    public void BasicWidgetValidationAndActionRangesAreEnforced()
    {
        Definitions.SizedBox box = SizedBox()[Rectangle()];
        Assert.Throws<InvalidOperationException>(() => box.Children.Initialize(Rectangle()));
        Assert.Throws<ArgumentException>(() => SizedBox()[Rectangle(), Rectangle()]);
        Assert.Throws<ArgumentOutOfRangeException>(() => SizedBox(with: [Align.At(-1, 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => SizedBox(with: [Align.At(0, float.NaN)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => SizedBox(with: [Border.Stroke(default, -1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiBoxConstraints(3, 2, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiSurface(Separator(thickness: -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiSurface(Separator(orientation: (UiOrientation)99)));
    }

    /// <summary>Checks a hidden child is excluded while its box retains parent minima.</summary>
    [Fact]
    public void HiddenChildDoesNotViolateBoxMinima()
    {
        using var surface = new UiSurface(SizedBox(with: [Padding.All(2)])[Rectangle(isVisible: false)]);
        surface.SetViewport(new(30, 20));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(30, 20), surface.RootElement.Size);
        Assert.Equal(new Vector2(26, 16), surface.RootElement.ContentSize);
        Assert.Equal(Vector2.Zero, surface.RootElement.Children[0].Size);
    }

    /// <summary>Checks modifier adoption keeps a pressed button's identity and capture.</summary>
    [Fact]
    public void ModifierRebuildPreservesCaptureAndFocus()
    {
        int clicks = 0;
        using var surface = new UiSurface(Panel()[Button(key: "save", width: 20, height: 10, onClick: () => clicks++)]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        var element = (ButtonElement)surface.RootElement.Children[0];
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(5, 5)));
        Assert.True(element.IsPressed);
        surface.Rebuild(Panel()[Button(key: "save", width: 20, height: 10, onClick: () => clicks++, with: [Padding.All(4)])]);
        surface.UpdateLayout();
        Assert.Same(element, surface.RootElement.Children[0]);
        Assert.True(element.IsPressed);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(8, 8)));
        Assert.False(element.IsPressed);
        Assert.Equal(1, clicks);
    }

    /// <summary>Checks custom callbacks see body coordinates and drawing state is restored.</summary>
    [Fact]
    public void CustomElementUsesBodyGeometryWithoutDoubleTranslation()
    {
        var widget = new BodyWidget { Width = 20, Height = 10 };
        Padding.Sides(11, 7, 3, 2)(widget);
        using var surface = new UiSurface(Panel()[widget]);
        surface.SetViewport(new(100, 100));
        var canvas = new InspectCanvas();
        canvas.Begin();
        surface.Record(canvas);
        Assert.Equal(Matrix3x2.Identity, canvas.CurrentTransform);
        Assert.Equal(new Vector2(11, 7), widget.RecordOrigin);
        Assert.Equal(new Vector2(20, 10), widget.BodySize);
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Down, new(14, 9)));
        Assert.Equal(new Vector2(3, 2), widget.Pointer);
        _ = canvas.End();
    }

    /// <summary>Checks construction-time reads replace, rather than append, modifiers.</summary>
    [Fact]
    public void SignalRebuildDoesNotAccumulateLayers()
    {
        var inset = Signal.Create(2f);
        using var surface = new UiSurface(() => Panel()[SizedBox(width: 10, height: 10, with: [Padding.All(inset.Value)])]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        UiElement child = surface.RootElement.Children[0];
        inset.Value = 5;
        surface.UpdateLayout();
        Assert.Same(child, surface.RootElement.Children[0]);
        Assert.Equal(new Vector2(20, 20), child.Size);
        inset.Value = 1;
        surface.UpdateLayout();
        Assert.Equal(new Vector2(12, 12), child.Size);
    }

    /// <summary>Checks failed PrepareUpdate keeps the old declaration, identity and body geometry.</summary>
    [Fact]
    public void FailedUpdateLeavesModifierGeometryUntouched()
    {
        var before = new FailingWidget { Width = 20, Height = 10 };
        Padding.All(3)(before);
        using var surface = new UiSurface(Panel()[before]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        UiElement element = surface.RootElement.Children[0];
        var next = new FailingWidget { Width = 20, Height = 10, Reject = true };
        Padding.All(9)(next);
        surface.Rebuild(Panel()[next]);
        Assert.Throws<InvalidOperationException>(surface.UpdateTree);
        Assert.Same(before, element.Widget);
        Assert.Equal(new Rect2D(3, 3, 20, 10), element.ContentBounds);
        Assert.Equal(new Vector2(26, 16), element.Size);
    }

    /// <summary>Checks alignment on an unbounded axis follows the inner finite size.</summary>
    [Fact]
    public void UnboundedAlignmentDoesNotPublishInfinity()
    {
        using var surface = new UiSurface(Stack(orientation: UiOrientation.Vertical)[SizedBox(width: 20, height: 10, with: [Align.Center(), Padding.All(2)])]);
        surface.SetViewport(new(100, 80));
        surface.UpdateLayout();
        UiElement box = surface.RootElement.Children[0];
        Assert.Equal(new Vector2(100, 14), box.Size);
        Assert.Equal(new Rect2D(40, 2, 20, 10), box.ContentBounds);
    }

    /// <summary>Checks typed factories, repeated insets and innermost dimensions.</summary>
    [Fact]
    public void GeneratedActionsPreserveTypedFactoryAndSlots()
    {
        Definitions.Button button = Button(width: 120, height: 32, with: [Padding.All(12), Padding.Sides(1, 2, 3, 4), Border.Stroke(new(1, 0, 0, 1), 1)])[Rectangle(width: 16, height: 16), Button.Background()[Rectangle(hitTestVisible: false)]];
        using var surface = new UiSurface(Panel()[button]);
        surface.SetViewport(new(400, 200));
        surface.UpdateLayout();
        UiElement body = surface.RootElement.Children[0];
        Assert.Equal(new Vector2(150, 64), body.Size);
        Assert.Equal(new Rect2D(14, 15, 120, 32), body.ContentBounds);
        Assert.Equal(2, body.Children.Count);
        Assert.IsNotAssignableFrom<Widget>(new Definitions.Padding());
    }

    /// <summary>Checks first-outermost alignment and inherited tight constraints.</summary>
    [Fact]
    public void AlignmentLoosensAndMovesBodyWithoutChangingChildSize()
    {
        using var surface = new UiSurface(SizedBox(width: 20, height: 10, with: [Align.Center(), Padding.All(5)])[Rectangle()]);
        surface.SetViewport(new(100, 80));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(100, 80), surface.RootElement.Size);
        Assert.Equal(new Rect2D(40, 35, 20, 10), surface.RootElement.ContentBounds);
        Assert.Equal(new Vector2(20, 10), surface.RootElement.Children[0].Size);
    }

    /// <summary>Checks excess insets keep leading offsets while body shrinks to zero.</summary>
    [Fact]
    public void OverlargeInsetsAndHiddenSubtreeRespectExclusion()
    {
        using var surface = new UiSurface(SizedBox(with: [Padding.All(20)]));
        surface.SetViewport(new(10, 8));
        surface.UpdateLayout();
        Assert.Equal(new Rect2D(20, 20, 0, 0), surface.RootElement.ContentBounds);
        surface.Rebuild(SizedBox(isVisible: false, with: [Padding.All(20)]));
        surface.UpdateLayout();
        Assert.Equal(Vector2.Zero, surface.RootElement.Size);
        Assert.Equal(default, surface.RootElement.ContentBounds);
    }

    /// <summary>Checks additional constraints prioritize the parent's legal interval.</summary>
    [Fact]
    public void AdditionalConstraintsClampToParent()
    {
        using var surface = new UiSurface(Panel()[SizedBox(with: [ConstrainedBox.Limits(new(50, 60, 7, 9))])]);
        surface.SetViewport(new(30, 20));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(30, 7), surface.RootElement.Children[0].Size);
    }

    /// <summary>Checks actions reject invalid values at application, without storing a failed item.</summary>
    /// <param name="value">The invalid inset.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]

    public void InsetsValidateWhenApplied(float value)
    {
        Action<Widget> action = Padding.All(value);
        var widget = new Definitions.SizedBox();
        Assert.Throws<ArgumentOutOfRangeException>(() => action(widget));
    }

    /// <summary>Checks overflow is not exposed as infinite geometry.</summary>
    [Fact]
    public void InsetsOverflowFailsLayout()
    {
        using var surface = new UiSurface(SizedBox(with: [Padding.All(float.MaxValue)]));
        surface.SetViewport(new(100, 100));
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
    }

    /// <summary>Checks reused identity, independent surfaces, and no accumulation after rebuild.</summary>
    [Fact]
    public void RebuildAndSharedDeclarationsKeepGeometryOwnedByElements()
    {
        Definitions.SizedBox declaration = SizedBox(width: 10, height: 20, with: [Align.At(1, 0), Padding.All(3)]);
        using var first = new UiSurface(declaration);
        using var second = new UiSurface(declaration);
        first.SetViewport(new(100, 100));
        second.SetViewport(new(200, 200));
        first.UpdateLayout();
        second.UpdateLayout();
        UiElement element = first.RootElement;
        UiAutomationId id = element.AutomationId;
        Assert.Equal(87, element.ContentBounds.X);
        Assert.Equal(187, second.RootElement.ContentBounds.X);
        first.Rebuild(SizedBox(width: 10, height: 20, with: [Align.At(1, 0), Padding.All(3)]));
        first.UpdateLayout();
        Assert.Same(element, first.RootElement);
        Assert.Equal(id, element.AutomationId);
        Assert.Equal(87, element.ContentBounds.X);
    }

    /// <summary>Checks empty box minima and separator direction and semantics.</summary>
    [Fact]
    public void BasicWidgetsHaveFiniteSizesAndNoInventedActions()
    {
        using var surface = new UiSurface(Panel()[SizedBox(width: 10), Separator(thickness: 3), Separator(orientation: UiOrientation.Vertical, thickness: 2)]);
        surface.SetViewport(new(100, 80));
        surface.UpdateLayout();
        Assert.Equal(new Vector2(10, 0), surface.RootElement.Children[0].Size);
        Assert.Equal(new Vector2(100, 3), surface.RootElement.Children[1].Size);
        Assert.Equal(new Vector2(2, 80), surface.RootElement.Children[2].Size);
        Assert.All(surface.CaptureAutomationSnapshot().Nodes, node => Assert.Equal(UiAutomationActions.None, node.Actions));
    }

    /// <summary>Checks finite ancestor and leading inset sums fail before input geometry is published.</summary>
    [Fact]
    public void BodyOriginOverflowFailsGeometryPreparation()
    {
        Definitions.SizedBox child = SizedBox(with: [Padding.Sides(float.MaxValue, 0, 0, 0)]);
        using var surface = new UiSurface(new OffsetParent(child));
        surface.SetViewport(new(100, 100));
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        UiElement element = surface.RootElement.Children[0];
        Assert.True(float.IsFinite(element.InputOrigin.X));
        Assert.True(float.IsFinite(element.InputOrigin.Y));
        Assert.Throws<InvalidOperationException>(() => surface.CaptureAutomationSnapshot());
    }

    /// <summary>Checks snapshots validate body origins even for input-excluded nodes.</summary>
    [Fact]
    public void InputExcludedBodyOriginOverflowCannotEscapeInSnapshot()
    {
        Definitions.SizedBox child = SizedBox(hitTestVisible: false, with: [Padding.Sides(float.MaxValue, 0, 0, 0)]);
        using var surface = new UiSurface(new OffsetParent(child));
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Throws<InvalidOperationException>(() => surface.CaptureAutomationSnapshot());
    }

    /// <summary>Checks layer-only changes advance geometry validity even with an identical body rectangle.</summary>
    [Fact]
    public void ModifierRevisionIncludesLayerRectanglesAndOnlySuccessfulGeometry()
    {
        using var surface = new UiSurface(SizedBox(width: 10, height: 8, with: [Align.At(0, 0), Border.Stroke(new(1, 0, 0, 1), 2), Align.At(0, 0)]));
        surface.SetViewport(new(100, 80));
        surface.UpdateLayout();
        UiElement element = surface.RootElement;
        Rect2D body = element.ContentBounds;
        ulong revision = element.ModifierGeometryRevision;
        surface.SetViewport(new(120, 90));
        surface.UpdateLayout();
        Assert.Equal(body, element.ContentBounds);
        Assert.True(element.ModifierGeometryRevision > revision);
        revision = element.ModifierGeometryRevision;
        surface.UpdateLayout();
        Assert.Equal(revision, element.ModifierGeometryRevision);
        surface.Rebuild(SizedBox(with: [Padding.All(float.MaxValue)]));
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(revision, element.ModifierGeometryRevision);
    }

    private sealed class OffsetParent : Definitions.Panel
    {
        internal OffsetParent(Widget child)
        {
            Children.Initialize(child);
        }

        protected override UiElement CreateElement() => new OffsetParentElement(this);
    }

    private sealed class OffsetParentElement(OffsetParent widget) : PanelElement(widget)
    {
        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            UiElement child = GetChildren(Widget.Children)[0];
            LayoutChild(child, constraints.Loosen());
            SetChildOffset(child, new(float.MaxValue, 0));
            return constraints.Constrain(Vector2.Zero);
        }
    }

    private sealed class FailingWidget : Widget
    {
        internal bool Reject { get; init; }

        protected override UiElement CreateElement() => new FailingElement(this);

        private sealed class FailingElement(FailingWidget widget) : UiElement<FailingWidget>(widget)
        {
            protected override Action? PrepareUpdate(FailingWidget next, UiElementUpdateContext context) => next.Reject ? throw new InvalidOperationException("Rejected") : null;
        }
    }

    private sealed class InspectCanvas : DrawingCanvas2D
    {
        internal Matrix3x2 CurrentTransform => State.Transform;

        internal DrawList2D End() => CompleteRecording();
    }

    private sealed class BodyWidget : Widget
    {
        internal Vector2 RecordOrigin { get; private set; }

        internal Vector2 BodySize { get; private set; }

        internal Vector2? Pointer { get; private set; }

        protected override UiElement CreateElement() => new BodyElement(this);

        private sealed class BodyElement(BodyWidget widget) : UiElement<BodyWidget>(widget)
        {
            protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(Vector2.Zero);

            protected override void OnRecord(DrawingCanvas2D canvas)
            {
                Widget.RecordOrigin = Vector2.Transform(Vector2.Zero, ((InspectCanvas)canvas).CurrentTransform);
                Widget.BodySize = ContentSize;
                canvas.FillRect(new(0, 0, ContentSize.X, ContentSize.Y), new Color2D(1, 0, 0, 1));
            }

            protected override UiInputResult OnInput(in UiInputContext context)
            {
                Widget.Pointer = context.PointerPosition;
                return UiInputResult.Handled;
            }
        }
    }

    private sealed class MeasureWidget : Widget
    {
        internal int Calls { get; private set; }

        protected override UiElement CreateElement() => new MeasureElement(this);

        private sealed class MeasureElement(MeasureWidget widget) : UiElement<MeasureWidget>(widget)
        {
            protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
            {
                Widget.Calls++;
                return constraints.Constrain(new(constraints.MaxWidth, constraints.MaxWidth * 2));
            }
        }
    }
}
