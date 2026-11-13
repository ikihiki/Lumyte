using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks opt-in retained recording without a second UI ownership tree.</summary>
public sealed class RetainedDrawingTests
{
    /// <summary>Checks unchanged built-in drawing is not captured again.</summary>
    [Fact]
    public void StaticBuiltinsReuseLocalContent()
    {
        using var surface = new UiSurface(Panel()[Rectangle(fill: new SrgbRgba<float>(1, 0, 0, 1))]);
        surface.SetViewport(new(100, 80));
        using UiDrawingScene2D drawing = surface.CreateDrawingScene();
        drawing.Update();
        Assert.Equal(2, drawing.LastRecordingCount);
        drawing.Update();
        Assert.Equal(0, drawing.LastRecordingCount);
    }

    /// <summary>Checks leaf invalidation never recaptures its clean sibling.</summary>
    [Fact]
    public void CustomOptInInvalidatesOnlyItsOwnFragmentAndAncestorSelf()
    {
        var first = new Probe(true);
        var second = new Probe(true);
        using var surface = new UiSurface(Panel()[first, second]);
        surface.SetViewport(new(100, 80));
        using var drawing = new UiDrawingScene2D(surface);
        drawing.Update();
        first.Runtime.Change();
        drawing.Update();
        Assert.Equal(2, first.Runtime.Calls);
        Assert.Equal(1, second.Runtime.Calls);
        drawing.Update();
        Assert.Equal(0, drawing.LastRecordingCount);
    }

    /// <summary>Checks unknown time-dependent custom drawing is never cached implicitly.</summary>
    [Fact]
    public void UnknownCustomDrawingRunsEveryUpdate()
    {
        var probe = new Probe(false);
        using var surface = new UiSurface(Panel()[probe]);
        surface.SetViewport(new(100, 80));
        using var drawing = new UiDrawingScene2D(surface);
        drawing.Update();
        drawing.Update();
        Assert.Equal(2, probe.Runtime.Calls);
        Assert.Equal(1, drawing.LastRecordingCount);
    }

    /// <summary>Checks borrow exclusion occurs before layout or drawing callbacks.</summary>
    [Fact]
    public void BorrowRejectsUpdatesUntilReleased()
    {
        var probe = new Probe(false);
        using var surface = new UiSurface(probe);
        surface.SetViewport(new(100, 80));
        using var drawing = new UiDrawingScene2D(surface);
        using (DrawingSceneReadLease2D lease = drawing.Borrow())
        {
            surface.SetViewport(new(200, 80));
            Assert.Throws<InvalidOperationException>(drawing.Update);
            Assert.Equal(1, probe.Runtime.Calls);
            Assert.Equal(new Vector2(100, 80), probe.Runtime.Size);
        }

        drawing.Update();
        Assert.Equal(new Vector2(200, 80), probe.Runtime.Size);
    }

    /// <summary>Checks disposing the adapter leaves its borrowed surface usable.</summary>
    [Fact]
    public void AdapterOwnsOnlyItsScene()
    {
        using var surface = new UiSurface(Panel());
        var drawing = new UiDrawingScene2D(surface);
        drawing.Dispose();
        drawing.Dispose();
        Assert.Throws<ObjectDisposedException>(drawing.Update);
        surface.SetViewport(new(10, 10));
        surface.UpdateLayout();
        using var next = new UiDrawingScene2D(surface);
        surface.Dispose();
        Assert.Throws<ObjectDisposedException>(next.Update);
    }

    /// <summary>Checks invisible descendants are neither recorded nor retained after removal.</summary>
    [Fact]
    public void HiddenSubtreeAndLaterRemovalDoNotKeepStalePlacements()
    {
        var probe = new Probe(true);
        using var surface = new UiSurface(Panel()[Panel(isVisible: false)[probe]]);
        surface.SetViewport(new(100, 80));
        using var drawing = new UiDrawingScene2D(surface);
        drawing.Update();
        Assert.Equal(0, probe.Runtime.Calls);
        surface.Rebuild(Panel()[Panel()[probe]]);
        drawing.Update();
        Assert.Equal(1, probe.Runtime.Calls);
        surface.Rebuild(Panel());
        drawing.Update();
        drawing.Update();
        Assert.Equal(0, drawing.LastRecordingCount);
    }

    /// <summary>Checks host composition constraints take the existing whole-surface path.</summary>
    [Fact]
    public void PageTransitionsUseFreshOpaqueRecordings()
    {
        var probe = new Probe(true);
        using var surface = new UiSurface(Panel()[probe]);
        surface.SetViewport(new(100, 80));
        using var drawing = new UiDrawingScene2D(surface);
        drawing.Update();
        surface.SetPageTransitioning(true);
        drawing.Update();
        drawing.Update();
        Assert.Equal(3, probe.Runtime.Calls);
        Assert.Equal(1, drawing.LastRecordingCount);
        surface.SetPageTransitioning(false);
        drawing.Update();
        drawing.Update();
        Assert.Equal(0, drawing.LastRecordingCount);
    }

    /// <summary>Checks custom before/child/after ordering remains a single fresh opaque fragment.</summary>
    [Fact]
    public void CustomRecordOverridePreservesInterleavingAndCanvasState()
    {
        var child = new Probe(true);
        var root = new Interleaved(child);
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 80));
        using var drawing = new UiDrawingScene2D(surface);
        drawing.Update();
        drawing.Update();
        Assert.Equal(new[] { "before", "after", "before", "after" }, root.Events);
        Assert.Equal(2, child.Runtime.Calls);
        Assert.Equal(1, drawing.LastRecordingCount);
    }

    /// <summary>Checks moving a clean leaf changes its placement without calling local drawing again.</summary>
    [Fact]
    public void PositionOnlyChangeKeepsLocalContent()
    {
        var first = new Probe(true) { Width = 10, Height = 10 };
        var second = new Probe(true) { Width = 10, Height = 10 };
        using var surface = new UiSurface(Stack()[first, second]);
        surface.SetViewport(new(100, 80));
        using var drawing = new UiDrawingScene2D(surface);
        drawing.Update();
        surface.Rebuild(Stack(spacing: 7)[first, second]);
        drawing.Update();
        Assert.Equal(1, first.Runtime.Calls);
        Assert.Equal(1, second.Runtime.Calls);
        Assert.Equal(17, second.Runtime.Bounds.Y);
    }

    /// <summary>Checks keyed reorder and sibling removal keep surviving instance-local recordings.</summary>
    [Fact]
    public void ReorderedPersistentChildrenKeepTheirRecordings()
    {
        var first = new Probe(true) { Key = "first" };
        var second = new Probe(true) { Key = "second" };
        using var surface = new UiSurface(Panel()[first, second]);
        surface.SetViewport(new(100, 80));
        using var drawing = new UiDrawingScene2D(surface);
        drawing.Update();
        ProbeElement firstInstance = first.Runtime;
        ProbeElement secondInstance = second.Runtime;
        surface.Rebuild(Panel()[second, first]);
        drawing.Update();
        Assert.Same(secondInstance, surface.RootElement.Children[0]);
        Assert.Same(firstInstance, surface.RootElement.Children[1]);
        Assert.Equal(1, firstInstance.Calls);
        Assert.Equal(1, secondInstance.Calls);
        surface.Rebuild(Panel()[second]);
        drawing.Update();
        drawing.Update();
        Assert.Equal(0, drawing.LastRecordingCount);
        Assert.Equal(1, secondInstance.Calls);
    }

    private sealed class Interleaved : Widget
    {
        internal Interleaved(Widget child)
        {
            Content = CreateChildren();
            Content.Initialize(child);
        }

        internal WidgetChildren Content { get; }

        internal List<string> Events { get; } = [];

        protected override UiElement CreateElement() => new InterleavedElement(this);
    }

    private sealed class InterleavedElement(Interleaved widget) : UiElement<Interleaved>(widget)
    {
        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            foreach (UiElement child in GetChildren(Widget.Content))
            {
                LayoutChild(child, constraints.Loosen());
                SetChildOffset(child, Vector2.Zero);
            }

            return constraints.Constrain(Vector2.Zero);
        }

        protected override void RecordOverride(DrawingCanvas2D canvas)
        {
            Widget.Events.Add("before");
            canvas.Translate(new(3, 4));
            canvas.FillRect(new(0, 0, 2, 2), new SrgbRgba<float>(1, 0, 0, 1));
            foreach (UiElement child in GetChildren(Widget.Content))
            {
                RecordChild(child, canvas);
            }

            canvas.FillRect(new(0, 0, 2, 2), new SrgbRgba<float>(0, 0, 1, 1));
            Widget.Events.Add("after");
        }
    }

    private sealed class Probe(bool cache) : Widget
    {
        internal bool Cache { get; } = cache;

        internal ProbeElement Runtime { get; private set; } = null!;

        protected override UiElement CreateElement() => Runtime = new(this);
    }

    private sealed class ProbeElement(Probe widget) : UiElement<Probe>(widget)
    {
        internal int Calls { get; private set; }

        protected override bool CacheRetainedDrawing => Widget.Cache;

        internal void Change() => InvalidateDrawing();

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(new(10, 10));

        protected override void OnRecord(DrawingCanvas2D canvas)
        {
            Calls++;
            canvas.FillRect(new(0, 0, Size.X, Size.Y), new SrgbRgba<float>(1, 0, 0, 1));
        }
    }
}
