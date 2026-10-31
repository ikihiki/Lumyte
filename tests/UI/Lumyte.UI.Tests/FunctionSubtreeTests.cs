using Lumyte.Core.Time;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Tests.FunctionTestUi;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks independent function subscriptions and atomic subtree publication.</summary>
public sealed class FunctionSubtreeTests
{
    /// <summary>Checks a single source retains the root and every unrelated function and body.</summary>
    [Fact]
    public void LocalChangeRetainsUnrelatedDeclarations()
    {
        Signal<int>[] sources = Enumerable.Range(0, 128).Select(_ => Signal.Create(10)).ToArray();
        int[] builds = new int[sources.Length];
        Lumyte.UI.Compose.Definitions.Panel root = Panel()[sources.Select((source, index) => Observed(source, () => builds[index]++)).ToArray()];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Widget[] before = root.Children.ToArray();
        sources[64].Value = 20;
        surface.UpdateLayout();
        Assert.Same(root, surface.Root);
        for (int index = 0; index < before.Length; index++)
        {
            Assert.Equal(index == 64 ? 2 : 1, builds[index]);
            if (index != 64)
            {
                Assert.Same(before[index], root.Children[index]);
            }
        }

        Assert.NotSame(before[64], root.Children[64]);
        Assert.Same(root, root.Children[64].Parent);
        Assert.Equal(20, Body(root.Children[64]).Height);
    }

    /// <summary>Checks sibling updates share a slot publication and dirty children are folded into their parent.</summary>
    [Fact]
    public void BatchedParentsAndSiblingsArePublishedTogether()
    {
        var parent = Signal.Create(1);
        var child = Signal.Create(10);
        var sibling = Signal.Create(10);
        int parents = 0;
        int children = 0;
        int siblings = 0;
        Lumyte.UI.Compose.Definitions.Panel root = Panel()[ObservedParent(parent, child, () => parents++, () => children++), Observed(sibling, () => siblings++)];
        using var surface = new UiSurface(root);
        Signal.Batch(() =>
        {
            parent.Value++;
            child.Value = 20;
            sibling.Value = 30;
        });
        surface.UpdateTree();
        Assert.Same(root, surface.Root);
        Assert.Equal(2, parents);
        Assert.Equal(2, children);
        Assert.Equal(2, siblings);
        Lumyte.UI.Compose.Definitions.Panel content = Assert.IsType<Lumyte.UI.Compose.Definitions.Panel>(Assert.IsAssignableFrom<UiFunctionWidget>(root.Children[0]).Content);
        Assert.Equal(20, Body(Assert.Single(content.Children)).Height);
        Assert.Equal(30, Body(root.Children[1]).Height);
    }

    /// <summary>Checks removed descendants release their observers while the parent remains reactive.</summary>
    [Fact]
    public void RemovedFunctionStopsObservingItsSource()
    {
        var show = Signal.Create(true);
        var value = Signal.Create(10);
        int builds = 0;
        using var surface = new UiSurface(OptionalFunction(show, value, () => builds++));
        show.Value = false;
        surface.UpdateTree();
        UiSignalRevision revision = surface.SignalRevision;
        value.Value = 20;
        surface.UpdateTree();
        Assert.Equal(revision, surface.SignalRevision);
        Assert.Equal(1, builds);
        show.Value = true;
        surface.UpdateTree();
        Assert.Equal(2, builds);
    }

    /// <summary>Checks subtree updates retain captures and focus both within and outside the changed branch.</summary>
    /// <param name="updateCaptured">Whether to replace the captured branch itself.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PointerCaptureSurvivesLocalPublication(bool updateCaptured)
    {
        var first = Signal.Create(10);
        var second = Signal.Create(10);
        int clicks = 0;
        Lumyte.UI.Compose.Definitions.Stack root = Stack()[Observed(first, () => { }, () => clicks++), Observed(second, () => { })];
        using var surface = new UiSurface(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(1, 1))));
        (updateCaptured ? first : second).Value = 15;
        surface.UpdateLayout();
        Assert.True(Body(root.Children[0]).IsPressed);
        Assert.Same(Body(root.Children[0]), surface.FocusedWidget);
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(1, 1))));
        Assert.Equal(1, clicks);
        Assert.False(Body(root.Children[0]).IsPressed);
        Assert.Equal(updateCaptured ? 15 : 10, root.Children[1].Bounds.Y);
    }

    /// <summary>Checks all affected functions finish preparation before any sibling is published.</summary>
    [Fact]
    public void FailedBatchKeepsEveryPreviousBranch()
    {
        var value = Signal.Create(10);
        var fail = Signal.Create(false);
        int builds = 0;
        Lumyte.UI.Compose.Definitions.Panel root = Panel()[Fallible(fail), Observed(value, () => builds++)];
        using var surface = new UiSurface(root);
        Widget[] before = root.Children.ToArray();
        Signal.Batch(() =>
        {
            value.Value = 20;
            fail.Value = true;
        });
        Assert.Throws<UiStateUpdateException>(surface.UpdateTree);
        Assert.Equal(2, builds);
        Assert.Same(root, surface.Root);
        Assert.Same(before[0], root.Children[0]);
        Assert.Same(before[1], root.Children[1]);
    }

    /// <summary>Checks a component with surface-wide validation keeps the complete validation boundary.</summary>
    [Fact]
    public void UnknownValidationFallsBackToACompleteDeclaration()
    {
        var value = Signal.Create(10);
        int validations = 0;
        using var surface = new UiSurface(() =>
        {
            var root = new GlobalValidator { Validated = () => validations++ };
            root.Children.Initialize(Observed(value, () => { }));
            return root;
        });
        Widget previous = surface.Root;
        value.Value = 20;
        surface.UpdateTree();
        Assert.NotSame(previous, surface.Root);
        Assert.Equal(2, validations);
        Assert.Equal(20, Body(Assert.Single(Assert.IsType<GlobalValidator>(surface.Root).Children)).Height);
    }

    /// <summary>Checks an explicit root request made during reconciliation supersedes uncommitted local updates.</summary>
    [Fact]
    public void ReentrantRootRequestWinsOverPreparedFunctions()
    {
        var value = Signal.Create(10);
        Lumyte.UI.Compose.Definitions.Panel last = Panel();
        UiSurface? owner = null;
        using var surface = new UiSurface(Panel()[ReentrantBody(value, () => owner!.Rebuild(last))]);
        owner = surface;
        value.Value = 20;
        surface.UpdateTree();
        Assert.Same(last, surface.Root);
        UiSignalRevision revision = surface.SignalRevision;
        value.Value = 30;
        surface.UpdateTree();
        Assert.Equal(revision, surface.SignalRevision);
        Assert.Same(last, surface.Root);
    }

    /// <summary>Checks every region of a shared source updates, including explicit updates while paused.</summary>
    /// <param name="paused">Whether automatic application is suspended.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedSourcesUpdateAllRegions(bool paused)
    {
        var value = Signal.Create(10);
        int builds = 0;
        Lumyte.UI.Compose.Definitions.Panel root = Panel()[Observed(value, () => builds++), Observed(value, () => builds++), Observed(value, () => builds++)];
        using var surface = new UiSurface(root);
        surface.SetSignalUpdatesEnabled(!paused);
        UiSignalRevision before = surface.SignalRevision;
        value.Value = 20;
        Assert.NotEqual(before, surface.SignalRevision);
        surface.UpdateTree();
        Assert.Same(root, surface.Root);
        Assert.Equal(6, builds);
        Assert.All(root.Children, function => Assert.Equal(20, Body(function).Height));
        surface.SetSignalUpdatesEnabled(true);
        surface.UpdateTree();
        Assert.Equal(6, builds);
    }

    /// <summary>Checks producer visibility is refreshed after a local update keeps the same surface root.</summary>
    [Fact]
    public void AnimationCreatedInAFunctionContinuesAfterLocalReplacement()
    {
        var target = Signal.Create<float?>(10);
        var version = Signal.Create(0);
        var clock = new ManualClock();
        Lumyte.UI.Compose.Definitions.Panel root = Panel()[AnimatedFunction(target, version)];
        using var surface = new UiSurface(root);
        surface.SetAnimationClock(clock);
        surface.AdvanceAnimations();
        target.Value = 30;
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(0.5));
        surface.AdvanceAnimations();
        Assert.Equal(20, Assert.IsAssignableFrom<UiFunctionWidget>(Assert.Single(root.Children)).Content.Width);
        version.Value++;
        surface.UpdateTree();
        Assert.Same(root, surface.Root);
        clock.Advance(Duration.FromSeconds(0.5));
        surface.AdvanceAnimations();
        Assert.Equal(30, Assert.IsAssignableFrom<UiFunctionWidget>(Assert.Single(root.Children)).Content.Width);
    }

    private static Lumyte.UI.Compose.Definitions.Button Body(Widget widget) =>
        Assert.IsType<Lumyte.UI.Compose.Definitions.Button>(Assert.IsAssignableFrom<UiFunctionWidget>(widget).Content);

    private sealed class GlobalValidator : Lumyte.UI.Compose.Definitions.Panel
    {
        internal Action? Validated { get; init; }

        protected override void ValidateDeclaration(Widget root) => Validated?.Invoke();
    }
}
