using System.Reflection;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Lumyte.UI.Application.Headless;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks indexed shared target lookup against live layout, ownership and publication.</summary>
public sealed class SharedLookupTests
{
    /// <summary>Checks the index follows successful key changes and ignores a failed candidate.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task KeysFollowPublishedIdentityChangesAndRemovalAsync()
    {
        var first = new UiSharedElementKey("first");
        var second = new UiSharedElementKey("second");
        await using UiApplication app = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => Panel()).Build();
        using var surface = new UiSurface(Panel()[Rectangle(key: "identity", width: 10, height: 10, sharedElementKey: first)]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        var flight = new UiPageVisualFlight(app, new(app, surface, static () => null), null, null, static () => false);
        Func<UiSharedElementKey, UiElement?> resolve = Lookup(flight);
        UiElement retained = Assert.IsAssignableFrom<UiElement>(resolve(first));
        surface.Rebuild(Panel()[Rectangle(key: "identity", width: 10, height: 10, sharedElementKey: second)]);
        surface.UpdateLayout();
        Assert.Null(resolve(first));
        Assert.Same(retained, resolve(second));
        surface.Rebuild(Panel()[Rectangle(width: 10, height: 10, sharedElementKey: second), Rectangle(width: 10, height: 10, sharedElementKey: second)]);
        Assert.Throws<ArgumentException>(surface.UpdateTree);
        Assert.Same(retained, resolve(second));
        surface.Rebuild(Panel()[Rectangle(key: "replacement", width: 10, height: 10, sharedElementKey: second)]);
        surface.UpdateLayout();
        Assert.NotSame(retained, resolve(second));
        Assert.False(retained.IsMounted);
        surface.Rebuild(Panel());
        surface.UpdateLayout();
        Assert.Null(resolve(second));
    }

    /// <summary>Checks current visibility, size, viewport and shared ancestors are checked on every lookup.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task NestedTargetsAndCurrentLayoutControlEligibilityAsync()
    {
        var outer = new UiSharedElementKey("outer");
        var inner = new UiSharedElementKey("inner");
        var visible = Signal.Create(true);
        var width = Signal.Create<float?>(20);
        await using UiApplication app = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => Panel()).Build();
        using var surface = new UiSurface(Panel()[Panel(width: width, height: 20, isVisible: visible, sharedElementKey: outer)[Rectangle(width: 10, height: 10, sharedElementKey: inner)]]);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        var flight = new UiPageVisualFlight(app, new(app, surface, static () => null), null, null, static () => false);
        Func<UiSharedElementKey, UiElement?> resolve = Lookup(flight);
        Select(flight, outer, inner);
        Assert.Same(surface.RootElement.Children[0], resolve(outer));
        Assert.Null(resolve(inner));
        Assert.Same(surface.RootElement.Children[0], Assert.Single(Exclusions(flight)()));
        visible.Value = false;
        surface.UpdateLayout();
        Assert.Null(resolve(outer));
        Assert.Empty(Exclusions(flight)());
        visible.Value = true;
        width.Value = 0;
        surface.UpdateLayout();
        Assert.Null(resolve(outer));
        width.Value = 20;
        surface.SetViewport(default);
        surface.UpdateLayout();
        Assert.Null(resolve(outer));
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        Assert.Same(surface.RootElement.Children[0], resolve(outer));
        surface.Rebuild(Panel()[Rectangle(width: 10, height: 10, sharedElementKey: inner)]);
        surface.UpdateLayout();
        Assert.Null(resolve(outer));
        Assert.Same(surface.RootElement.Children[0], resolve(inner));
    }

    /// <summary>Checks exiting content remains excluded from the normal page layer after it stops being a connected target.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ExitingElementRemainsInExclusionsUntilHiddenAsync()
    {
        var key = new UiSharedElementKey("item");
        var present = Signal.Create(true);
        var clock = new ManualClock();
        await using UiApplication app = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => Panel()).Build();
        using var surface = new UiSurface(Rectangle(width: 10, height: 10, sharedElementKey: key, isPresent: present, transitions: new UiElementTransitions { AnimateInitial = false, Exit = FadeOut(Duration.FromSeconds(1)) }));
        surface.VisualHost = new Host();
        surface.SetAnimationClock(clock);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        var flight = new UiPageVisualFlight(app, new(app, surface, static () => null), null, null, static () => false);
        Select(flight, key);
        present.Value = false;
        surface.UpdateLayout();
        Assert.Equal(UiPresenceState.Exiting, surface.RootElement.PresenceState);
        Assert.Null(Lookup(flight)(key));
        Assert.Same(surface.RootElement, Assert.Single(Exclusions(flight)()));
        Assert.Single(surface.VisualEntries).Begin(clock.Now);
        clock.Advance(Duration.FromSeconds(1));
        surface.AdvanceAnimations();
        surface.UpdateLayout();
        Assert.Empty(Exclusions(flight)());
    }

    private static Func<UiSharedElementKey, UiElement?> Lookup(UiPageVisualFlight flight) => typeof(UiPageVisualFlight).GetMethod("ResolveTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Func<UiSharedElementKey, UiElement?>>(flight);

    private static Func<IReadOnlySet<UiElement>> Exclusions(UiPageVisualFlight flight) => typeof(UiPageVisualFlight).GetMethod("ResolveTargets", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Func<IReadOnlySet<UiElement>>>(flight);

    private static void Select(UiPageVisualFlight flight, params UiSharedElementKey[] keys)
    {
        var selected = (HashSet<UiSharedElementKey>)typeof(UiPageVisualFlight).GetField("_sharedKeys", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flight)!;
        selected.UnionWith(keys);
    }

    private sealed class Host : IUiVisualHost
    {
        public bool CanPresent => true;

        public UiMotionOptions Motion { get; } = new();

        public bool Record(UiElement widget, DrawingCanvas2D canvas) => false;
    }
}
