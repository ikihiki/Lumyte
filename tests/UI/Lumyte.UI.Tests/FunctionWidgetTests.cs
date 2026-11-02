using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Tests.FunctionTestUi;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Exercises generated construction functions with real tree, layout, input and signal lifetimes.</summary>
public sealed class FunctionWidgetTests
{
    /// <summary>Checks callbacks update a direct local read and generated parameters remain reactive.</summary>
    [Fact]
    public void LocalStateSurvivesParameterChangesAndInput()
    {
        var label = Signal.Create("first");
        var notifications = new List<int>();
        using var surface = new UiSurface(Counter(label, onChanged: notifications.Add));
        surface.SetViewport(new(100, 50));
        surface.UpdateLayout();
        Assert.Equal("first:2", Body(surface.RootElement).AutomationName);
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(1, 1))));
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(1, 1))));
        surface.UpdateLayout();
        Assert.Equal("first:3", Body(surface.RootElement).AutomationName);
        label.Value = "second";
        surface.UpdateTree();
        Assert.Equal("second:3", Body(surface.RootElement).AutomationName);
        Assert.Equal(new[] { 3 }, notifications);
    }

    /// <summary>Checks parent rebuilds retain same-type/key state and initialize a changed key.</summary>
    [Fact]
    public void ParentRebuildPreservesStateAndKeyChangeResetsIt()
    {
        using var surface = new UiSurface(Counter("a", key: "one"));
        Body(surface.RootElement).OnClick!();
        surface.UpdateTree();
        surface.Rebuild(Counter("b", start: 99, key: "one"));
        surface.UpdateTree();
        Assert.Equal("b:3", Body(surface.RootElement).AutomationName);
        surface.Rebuild(Counter("c", start: 99, key: "two"));
        surface.UpdateTree();
        Assert.Equal("c:99", Body(surface.RootElement).AutomationName);
    }

    /// <summary>Checks keyed reordering preserves independent local values and child function boundaries.</summary>
    [Fact]
    public void KeyedNestedFunctionsRetainIndependentState()
    {
        using var surface = new UiSurface(Panel()[Nested("a", key: "a"), Nested("b", key: "b")]);
        Body(surface.RootElement.Children[0]).OnClick!();
        surface.UpdateTree();
        surface.Rebuild(Panel()[Nested("new b", key: "b"), Nested("new a", key: "a")]);
        surface.UpdateTree();
        Assert.Equal("new b:2", Body(surface.RootElement.Children[0]).AutomationName);
        Assert.Equal("new a:3", Body(surface.RootElement.Children[1]).AutomationName);
    }

    /// <summary>Checks lazy initialization runs once per function identity and removed state is not retained globally.</summary>
    [Fact]
    public void LazyValuesBelongToTheLogicalFunctionInstance()
    {
        int initializations = 0;
        Func<int> initialize = () => ++initializations;
        using var surface = new UiSurface(Lazy(initialize));
        surface.Rebuild(Lazy(initialize));
        surface.UpdateTree();
        Assert.Equal(1, initializations);
        surface.Rebuild(Panel());
        surface.UpdateTree();
        surface.Rebuild(Lazy(initialize));
        surface.UpdateTree();
        Assert.Equal(2, initializations);
        Assert.Equal("2", Body(surface.RootElement).AutomationName);
    }

    /// <summary>Checks function direct reads replace conditional dependencies and stop observing after disposal.</summary>
    [Fact]
    public void ConditionalReadsFollowTheCurrentBody()
    {
        var select = Signal.Create(true);
        var left = Signal.Create("left");
        var right = Signal.Create("right");
        using var surface = new UiSurface(Branch(select, left, right));
        Assert.Equal("left", Body(surface.RootElement).AutomationName);
        select.Value = false;
        surface.UpdateTree();
        Assert.Equal("right", Body(surface.RootElement).AutomationName);
        Widget current = surface.Root;
        left.Value = "unused";
        surface.UpdateTree();
        Assert.Same(current, surface.Root);
        right.Value = "updated";
        surface.UpdateTree();
        Assert.Equal("updated", Body(surface.RootElement).AutomationName);
        Lumyte.UI.Compose.Definitions.Button lastBody = Body(surface.RootElement);
        surface.Dispose();
        right.Value = "detached";
        Assert.Equal("updated", lastBody.AutomationName);
    }

    /// <summary>Checks failed preparation preserves the live state's values and restores the ambient function context.</summary>
    [Fact]
    public void FailedBuildDoesNotReplaceTheLiveFunction()
    {
        using var surface = new UiSurface(Fallible(false));
        Body(surface.RootElement).OnClick!();
        surface.UpdateTree();
        Widget previous = surface.Root;
        surface.Rebuild(Fallible(true));
        Assert.Throws<InvalidOperationException>(surface.UpdateTree);
        Assert.Same(previous, surface.Root);
        surface.Rebuild(Fallible(false));
        surface.UpdateTree();
        Assert.Equal("1", Body(surface.RootElement).AutomationName);
        Assert.Throws<InvalidOperationException>(() => UseSignal(1));
    }

    /// <summary>Checks closed generic wrappers and exact typed source arguments are emitted without reflection.</summary>
    [Fact]
    public void GenericFunctionParametersRemainReactive()
    {
        var value = Signal.Create(7);
        using var surface = new UiSurface(Generic<int>(value));
        Assert.Equal("7", Body(surface.RootElement).AutomationName);
        value.Value = 9;
        surface.UpdateTree();
        Assert.Equal("9", Body(surface.RootElement).AutomationName);
    }

    /// <summary>Checks local signals can be passed directly into child arguments without direct reads.</summary>
    [Fact]
    public void ChildArgumentDependenciesRebuildTheFunctionBody()
    {
        using var surface = new UiSurface(ChildSource());
        Body(surface.RootElement).OnClick!();
        surface.UpdateTree();
        Assert.Equal(11, Assert.Single(((UiFunctionElement)surface.RootElement).Content.Children).Widget.Width);
    }

    /// <summary>Checks opaque function recipes regenerate children that have no individual factory.</summary>
    [Fact]
    public void FunctionsCanReturnHandwrittenWidgetTrees()
    {
        var label = Signal.Create("before");
        using var surface = new UiSurface(Handwritten(label));
        label.Value = "after";
        surface.UpdateTree();
        Assert.Equal("after", Body(surface.RootElement).AutomationName);
    }

    /// <summary>Checks first initialization of a connected slot evaluates and connects function descendants.</summary>
    [Fact]
    public void ConnectedEmptySlotsAcceptFunctions()
    {
        Lumyte.UI.Compose.Definitions.Panel panel = Panel();
        using var surface = new UiSurface(panel);
        panel.Children.Initialize(Counter("unmounted configuration"));
        Assert.Empty(surface.RootElement.Children);
        surface.Rebuild(Panel()[Counter("connected")]);
        surface.SetViewport(new(100, 50));
        surface.UpdateLayout();
        Assert.Equal("connected:2", Body(Assert.Single(surface.RootElement.Children)).AutomationName);
        Body(Assert.Single(surface.RootElement.Children)).OnClick!();
        surface.UpdateTree();
        Assert.Equal("connected:3", Body(Assert.Single(surface.RootElement.Children)).AutomationName);
    }

    /// <summary>Checks inherited animation poses rebuild concrete function parameters before presentation.</summary>
    [Fact]
    public void AnimationParametersPreserveTheCurrentPoseOnParentRebuild()
    {
        var target = Signal.Create<float?>(10);
        var clock = new ManualClock();
        using var surface = new UiSurface(Sizing(Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear)));
        surface.SetAnimationClock(clock);
        surface.AdvanceAnimations();
        target.Value = 30;
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(0.5));
        surface.AdvanceAnimations();
        Assert.Equal(20, Assert.IsAssignableFrom<UiFunctionElement>(surface.RootElement).Content.Widget.Width);
        surface.Rebuild(Sizing(Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear)));
        surface.UpdateTree();
        Assert.Equal(20, Assert.IsAssignableFrom<UiFunctionElement>(surface.RootElement).Content.Widget.Width);
        clock.Advance(Duration.FromSeconds(0.5));
        surface.AdvanceAnimations();
        Assert.Equal(30, Assert.IsAssignableFrom<UiFunctionElement>(surface.RootElement).Content.Widget.Width);
    }

    /// <summary>Checks conditional call sites do not change the identity of later local signals.</summary>
    [Fact]
    public void LocalSignalIdentityUsesTheCallSite()
    {
        using var surface = new UiSurface(Conditional(false));
        Body(surface.RootElement).OnClick!();
        surface.UpdateTree();
        surface.Rebuild(Conditional(true));
        surface.UpdateTree();
        Assert.Equal("1", Body(surface.RootElement).AutomationName);
        surface.Rebuild(Conditional(false));
        surface.UpdateTree();
        Assert.Equal("1", Body(surface.RootElement).AutomationName);
    }

    /// <summary>Checks sequential thread moves preserve state without requiring callers to enter a build context.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task SequentialThreadMovesPreserveStateAsync()
    {
        using var surface = new UiSurface(Counter("before"));
        Body(surface.RootElement).OnClick!();
        surface.UpdateTree();
        await Task.Run(() => surface.Rebuild(Counter("after")));
        await Task.Run(surface.UpdateTree);
        Assert.Equal("after:3", Body(surface.RootElement).AutomationName);
    }

    /// <summary>Checks a passive function declaration owns no shared local state when mounted more than once.</summary>
    [Fact]
    public void OneDeclarationCreatesIndependentFunctionInstances()
    {
        Widget declaration = Counter("shared");
        using var first = new UiSurface(declaration);
        using var second = new UiSurface(declaration);
        UiElement retained = first.RootElement;
        Assert.NotSame(retained, second.RootElement);
        Body(first.RootElement).OnClick!();
        first.UpdateTree();
        second.UpdateTree();
        Assert.Equal("shared:3", Body(first.RootElement).AutomationName);
        Assert.Equal("shared:2", Body(second.RootElement).AutomationName);
        first.Rebuild(Counter("updated"));
        first.UpdateTree();
        Assert.Same(retained, first.RootElement);
        Assert.Equal("updated:3", Body(first.RootElement).AutomationName);
        Assert.Equal("shared:2", Body(second.RootElement).AutomationName);
    }

    private static Lumyte.UI.Compose.Definitions.Button Body(UiElement element)
    {
        while (element is UiFunctionElement function)
        {
            element = function.Content;
        }

        return Assert.IsType<Lumyte.UI.Compose.Definitions.Button>(element.Widget);
    }
}
