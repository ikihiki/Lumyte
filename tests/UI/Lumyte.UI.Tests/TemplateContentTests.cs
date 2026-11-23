using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Xunit;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Exercises idle owned-content refresh, rollback, and reentrant invalidation.</summary>
public sealed class TemplateContentTests
{
    /// <summary>Requests coalesce and preparation cannot recursively update the published tree.</summary>
    [Fact]
    public void CoalescingAndReentrancyUseLaterBoundaries()
    {
        using var surface = new UiSurface(new Host());
        var owner = (HostElement)surface.RootElement;
        Assert.Equal(1, owner.Evaluations);
        owner.Request();
        owner.Request();
        owner.DuringBuild = () =>
        {
            surface.UpdateTree();
            owner.Request();
        };
        surface.UpdateTree();
        Assert.Equal(2, owner.Evaluations);
        owner.DuringBuild = null;
        surface.UpdateTree();
        Assert.Equal(3, owner.Evaluations);
        surface.UpdateTree();
        Assert.Equal(3, owner.Evaluations);
    }

    /// <summary>Failed preparation leaves the tree and publication revision intact and remains retryable.</summary>
    [Fact]
    public void FailureDoesNotPublishAndCanRetry()
    {
        using var surface = new UiSurface(new Host());
        var owner = (HostElement)surface.RootElement;
        UiElement previous = Assert.Single(owner.Children);
        owner.DuringBuild = () => throw new InvalidOperationException("template");
        owner.Request();
        Assert.Throws<InvalidOperationException>(surface.UpdateTree);
        Assert.Same(previous, Assert.Single(owner.Children));
        Assert.True(previous.IsMounted);
        owner.DuringBuild = null;
        surface.UpdateTree();
        Assert.Equal(3, owner.Evaluations);
    }

    /// <summary>Removing or disposing a queued owner never invokes its template again.</summary>
    [Fact]
    public void UnmountAndDisposalDiscardQueuedRequests()
    {
        var surface = new UiSurface(new Host());
        var removed = (HostElement)surface.RootElement;
        removed.Request();
        surface.Rebuild(Lumyte.UI.Compose.Rectangle());
        surface.UpdateTree();
        Assert.False(removed.IsMounted);
        Assert.Equal(1, removed.Evaluations);
        surface.Dispose();
        removed.Request();
        Assert.Equal(1, removed.Evaluations);

        using var second = new UiSurface(new Host());
        var disposed = (HostElement)second.RootElement;
        disposed.Request();
        second.Dispose();
        Assert.Equal(1, disposed.Evaluations);
    }

    /// <summary>An owned fixed root can remain on one host but cannot mount on another simultaneously.</summary>
    [Fact]
    public void OwnedContentUsesExistingExclusiveTreeValidation()
    {
        Widget fixedRoot = Lumyte.UI.Compose.Rectangle();
        using var first = new UiSurface(new Host { FixedRoot = fixedRoot });
        var owner = (HostElement)first.RootElement;
        UiElement previous = Assert.Single(owner.Children);
        owner.Request();
        first.UpdateTree();
        Assert.Same(previous, Assert.Single(owner.Children));
        Assert.Throws<InvalidOperationException>(() => new UiSurface(new Host { FixedRoot = fixedRoot }));
    }

    /// <summary>Even explicit reentrant updates during delivery cannot execute queued content.</summary>
    [Fact]
    public void InputDeliveryNeverRunsQueuedTemplates()
    {
        using var surface = new UiSurface(new Host());
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        var owner = (HostElement)surface.RootElement;
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(5)));
        Assert.Equal(1, owner.Evaluations);
        surface.UpdateTree();
        Assert.Equal(2, owner.Evaluations);
    }

    /// <summary>A newer request raised for another owner is not consumed from an older drain snapshot.</summary>
    [Fact]
    public void ReentrantRequestsForOtherOwnersSurviveTheSnapshot()
    {
        using var surface = new UiSurface(Lumyte.UI.Compose.Panel()[new Host(), new Host()]);
        var first = (HostElement)surface.RootElement.Children[0];
        var second = (HostElement)surface.RootElement.Children[1];
        first.Request();
        second.Request();
        first.DuringBuild = second.Request;
        surface.UpdateTree();
        Assert.Equal(2, first.Evaluations);
        Assert.Equal(1, second.Evaluations);
        surface.UpdateTree();
        Assert.Equal(2, second.Evaluations);
    }

    /// <summary>Ordinary and retained recording defer nested content updates until an idle boundary.</summary>
    /// <param name="retained">Whether recording uses the retained fragment path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordingCannotRefreshContentReentrantly(bool retained)
    {
        using var surface = new UiSurface(new Host());
        surface.SetViewport(new(100));
        surface.UpdateLayout();
        var owner = (HostElement)surface.RootElement;
        ulong revision = surface.TreeRevision;
        UiElement previous = Assert.Single(owner.Children);
        owner.DuringRecord = () =>
        {
            owner.Request();
            surface.UpdateTree();
            Assert.Equal(1, owner.Evaluations);
            Assert.Equal(revision, surface.TreeRevision);
            Assert.Same(previous, Assert.Single(owner.Children));
            Assert.Throws<InvalidOperationException>(() => surface.RefreshElementContent(owner));
        };
        if (retained)
        {
            using UiDrawingScene2D drawing = surface.CreateDrawingScene();
            drawing.Update();
        }
        else
        {
            var canvas = new Canvas2D();
            canvas.Begin();
            surface.Record(canvas);
            _ = canvas.End();
        }

        Assert.Equal(1, owner.Evaluations);
        Assert.Equal(revision, surface.TreeRevision);
        owner.DuringRecord = null;
        surface.UpdateTree();
        Assert.Equal(2, owner.Evaluations);
        Assert.True(surface.TreeRevision > revision);
    }

    private sealed class Host : Widget
    {
        internal Widget? FixedRoot { get; init; }

        protected override UiElement CreateElement() => new HostElement(this);
    }

    private sealed class HostElement(Host widget) : UiElement<Host>(widget)
    {
        internal int Evaluations { get; private set; }

        internal Action? DuringBuild { get; set; }

        internal Action? DuringRecord { get; set; }

        internal void Request() => InvalidateContent();

        protected override UiElementContent? BuildContent(Widget next, UiElementBuildContext context)
        {
            Evaluations++;
            DuringBuild?.Invoke();
            return new(((Host)next).FixedRoot ?? Lumyte.UI.Compose.Rectangle(), ownsContent: true);
        }

        protected override void OnRecord(DrawingCanvas2D canvas) => DuringRecord?.Invoke();

        protected override UiInputResult OnInput(in UiInputContext context)
        {
            if (context.Input is UiPointerInput { Phase: UiPointerPhase.Down })
            {
                Request();
                Surface!.UpdateTree();
                return UiInputResult.Handled;
            }

            return UiInputResult.Unhandled;
        }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            Vector2 size = constraints.Constrain(new(20));
            foreach (UiElement child in Children)
            {
                LayoutChild(child, UiBoxConstraints.Tight(size));
                SetChildOffset(child, Vector2.Zero);
            }

            return size;
        }
    }
}
