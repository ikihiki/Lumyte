using System.Numerics;
using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Checks presence state, layout, action gates and generation-safe presentation notifications.</summary>
public sealed class PresenceTests
{
    /// <summary>Checks ordinary static factories accept presence signals and delay notification until final presentation.</summary>
    [Fact]
    public void EntryGatesActionsWithoutRemovingCapabilitiesAndWaitsForPresentation()
    {
        int entered = 0;
        var clock = new ManualClock();
        using var surface = new UiSurface(Button(width: 80, height: 24, transitions: new UiElementTransitions() { Enter = FadeIn(Duration.FromSeconds(1)) }, onEntered: () => entered++));
        surface.VisualHost = new Host();
        surface.SetAnimationClock(clock);
        surface.SetViewport(new(100, 40));
        surface.UpdateLayout();
        UiPresenceEntry entry = Assert.Single(surface.VisualEntries);
        Assert.Equal(UiPresenceState.Entering, surface.Root.PresenceState);
        UiAutomationNode node = Assert.Single(surface.CaptureAutomationSnapshot().Nodes);
        Assert.True(node.IsTransitioning);
        Assert.True(node.IsEnabled);
        Assert.True((node.Actions & UiAutomationActions.Click) != 0);
        Assert.False(surface.MoveFocus(UiFocusNavigation.First));
        entry.Begin(clock.Now);
        clock.Advance(Duration.FromSeconds(0.5));
        surface.AdvanceAnimations();
        Assert.InRange(entry.Pose.Opacity, 0.5f, 0.99f);
        Assert.Equal(0, entered);
        clock.Advance(Duration.FromSeconds(0.5));
        surface.AdvanceAnimations();
        surface.UpdateLayout();
        Assert.Equal(UiPresenceState.Visible, surface.Root.PresenceState);
        Assert.True(Assert.Single(surface.CaptureAutomationSnapshot().Nodes).IsTransitioning);
        surface.ConfirmVisualPresentation(surface.CaptureVisualPresentation());
        Assert.Equal(0, entered);
        surface.AdvanceAnimations();
        surface.UpdateLayout();
        Assert.Equal(1, entered);
        Assert.False(Assert.Single(surface.CaptureAutomationSnapshot().Nodes).IsTransitioning);
        Assert.True(surface.MoveFocus(UiFocusNavigation.First));
    }

    /// <summary>Checks exit reserves layout until completion and final hidden presentation precedes notification.</summary>
    [Fact]
    public void ExitRetainsSpaceUntilHiddenAndDoesNotChangeDeclarationVisibility()
    {
        var visible = Signal.Create(true);
        var clock = new ManualClock();
        int exited = 0;
        using var surface = new UiSurface(Stack()[Rectangle(width: 20, height: 20, isPresent: visible, transitions: new UiElementTransitions() { AnimateInitial = false, Exit = FadeOut(Duration.FromSeconds(1)) }, onExited: () => exited++), Rectangle(width: 20, height: 10)]);
        surface.VisualHost = new Host();
        surface.SetAnimationClock(clock);
        surface.SetViewport(new(40, 100));
        surface.UpdateLayout();
        Present(surface);
        visible.Value = false;
        surface.UpdateLayout();
        Definitions.Stack stack = Assert.IsType<Definitions.Stack>(surface.Root);
        UiPresenceEntry entry = Assert.Single(surface.VisualEntries);
        Assert.Equal(UiPresenceState.Exiting, stack.Children[0].PresenceState);
        Assert.True(stack.Children[0].IsVisible);
        Assert.Equal(20, stack.Children[1].Bounds.Y);
        entry.Begin(clock.Now);
        clock.Advance(Duration.FromSeconds(1));
        surface.AdvanceAnimations();
        surface.UpdateLayout();
        Assert.Equal(Vector2.Zero, stack.Children[0].Size);
        Assert.Equal(0, stack.Children[1].Bounds.Y);
        Assert.Equal(0, exited);
        Present(surface);
        Assert.Equal(1, exited);
    }

    /// <summary>Checks current pose and one identity survive reconstruction and an intent reversal.</summary>
    [Fact]
    public void ReversalStartsFromCurrentPoseAndRevokesEarlierPresentationTickets()
    {
        var visible = Signal.Create(true);
        var clock = new ManualClock();
        int entered = 0;
        int exited = 0;
        var transitions = new UiElementTransitions { Enter = new(new() { Opacity = 0 }, new(), Duration.FromSeconds(1), easing: AnimationEasing.Linear), Exit = FadeOut(Duration.FromSeconds(1)) };
        using var surface = new UiSurface(Rectangle(width: 20, height: 20, isPresent: visible, transitions: transitions, onEntered: () => entered++, onExited: () => exited++));
        surface.VisualHost = new Host();
        surface.SetAnimationClock(clock);
        surface.SetViewport(new(40, 40));
        surface.UpdateLayout();
        UiPresenceEntry entry = Assert.Single(surface.VisualEntries);
        entry.Begin(clock.Now);
        clock.Advance(Duration.FromSeconds(0.5));
        surface.AdvanceAnimations();
        Assert.Equal(0.5f, entry.Pose.Opacity);
        visible.Value = false;
        surface.UpdateLayout();
        Assert.Same(entry, Assert.Single(surface.VisualEntries));
        Assert.Equal(0.5f, entry.Pose.Opacity);
        entry.Begin(clock.Now);
        clock.Advance(Duration.FromSeconds(1));
        surface.AdvanceAnimations();
        surface.UpdateLayout();
        (UiPresenceEntry Entry, ulong Generation)[] stale = surface.CaptureVisualPresentation();
        visible.Value = true;
        surface.UpdateLayout();
        surface.ConfirmVisualPresentation(stale);
        surface.DeliverVisualNotifications();
        Assert.Equal(0, entered);
        Assert.Equal(0, exited);
        entry.Begin(clock.Now);
        clock.Advance(Duration.FromSeconds(1));
        surface.AdvanceAnimations();
        surface.UpdateLayout();
        Present(surface);
        Assert.Equal(1, entered);
        Assert.Equal(0, exited);
    }

    /// <summary>Checks unsupported standalone effects preserve notification and model lifetime without requiring a runtime.</summary>
    [Fact]
    public void StandaloneFallbackUsesVirtualPresentationAndDetachmentRevokesNotifications()
    {
        int entered = 0;
        using var surface = new UiSurface(Rectangle(width: 20, height: 20, transitions: new UiElementTransitions() { Enter = FadeIn(Duration.FromSeconds(1)) }, onEntered: () => entered++));
        surface.UpdateLayout();
        Assert.Equal(UiPresenceState.Visible, surface.Root.PresenceState);
        Assert.Equal(1, entered);
        Assert.False(surface.HasRunningAnimations);
        surface.UpdateLayout();
        Assert.Equal(1, entered);
        surface.Rebuild(Rectangle(width: 20, height: 20, key: "replacement", transitions: new UiElementTransitions() { Enter = FadeIn(Duration.FromSeconds(1)) }, onEntered: () => entered++));
        surface.VisualHost = new Host();
        surface.UpdateLayout();
        (UiPresenceEntry Entry, ulong Generation)[] pending = surface.CaptureVisualPresentation();
        surface.Dispose();
        surface.ConfirmVisualPresentation(pending);
        surface.DeliverVisualNotifications();
        Assert.Equal(1, entered);
    }

    /// <summary>Checks immutable definitions and default/duplicate shared identities fail before publishing ownership.</summary>
    [Fact]
    public void InvalidDefinitionsAndDuplicateKeysDoNotAttachTheCandidate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiVisualTransition(new() { Opacity = float.NaN }, new(), Duration.Zero));
        Assert.Throws<ArgumentException>(() => new UiSharedElementKey("bad\nkey"));
        var key = new UiSharedElementKey("photo");
        Definitions.Panel duplicate = Panel()[Rectangle(sharedElementKey: key), Rectangle(sharedElementKey: key)];
        Assert.Throws<ArgumentException>(() => new UiSurface(duplicate));
        Assert.Null(duplicate.Surface);
        Assert.Throws<ArgumentException>(() => new UiSurface(Rectangle(sharedElementKey: default(UiSharedElementKey))));
        Assert.Throws<ArgumentException>(() => new UiSurface(Rectangle(transitions: new UiElementTransitions() { Enter = FadeOut(Duration.Zero) })));
    }

    /// <summary>Checks adding an exit definition to an already visible keyed node preserves its prior presence.</summary>
    [Fact]
    public void ReconstructionCanIntroduceExitWithoutTreatingItAsInitialHiddenContent()
    {
        using var surface = new UiSurface(Rectangle(key: "same", width: 20, height: 20));
        surface.VisualHost = new Host();
        surface.SetViewport(new(40, 40));
        surface.UpdateLayout();
        surface.Rebuild(Rectangle(key: "same", width: 20, height: 20, isPresent: false, transitions: new UiElementTransitions { Exit = FadeOut(Duration.FromSeconds(1)) }));
        surface.UpdateLayout();
        Assert.Equal(UiPresenceState.Exiting, surface.Root.PresenceState);
        Assert.Equal(new Vector2(40, 40), surface.Root.Size);
    }

    /// <summary>Checks reduced motion removes spatial movement and bounds the finite fade.</summary>
    [Fact]
    public void ReducedMotionRemovesMovementAndFinishesWithinOneHundredMilliseconds()
    {
        var clock = new ManualClock();
        using var surface = new UiSurface(Rectangle(width: 20, height: 20, transitions: new UiElementTransitions { Enter = new(new() { Opacity = 0, Offset = new(100, 0), Scale = new(2, 2) }, new(), Duration.FromSeconds(1)) }));
        surface.VisualHost = new Host(UiMotionPolicy.Reduced);
        surface.SetAnimationClock(clock);
        surface.SetViewport(new(40, 40));
        surface.UpdateLayout();
        UiPresenceEntry entry = Assert.Single(surface.VisualEntries);
        Assert.Equal(Vector2.Zero, entry.Pose.Offset);
        Assert.Equal(Vector2.One, entry.Pose.Scale);
        entry.Begin(clock.Now);
        clock.Advance(Duration.FromSeconds(0.1));
        surface.AdvanceAnimations();
        Assert.Equal(UiPresenceState.Visible, surface.Root.PresenceState);
        Present(surface);
        Assert.False(surface.HasRunningAnimations);
    }

    /// <summary>Checks a hidden ancestor prevents child entry and obsolete notifications.</summary>
    [Fact]
    public void HiddenParentDefersChildEntryUntilTheParentBecomesPresent()
    {
        var visible = Signal.Create(false);
        int entered = 0;
        using var surface = new UiSurface(Panel(isPresent: visible)[Rectangle(width: 20, height: 20, transitions: new UiElementTransitions { Enter = FadeIn(Duration.FromSeconds(1)) }, onEntered: () => entered++)]);
        surface.VisualHost = new Host();
        surface.SetViewport(new(40, 40));
        surface.UpdateLayout();
        UiPresenceEntry child = surface.VisualEntries.Single(entry => entry.Widget.Parent is not null);
        Assert.False(child.Initialized);
        Present(surface);
        Assert.Equal(0, entered);
        visible.Value = true;
        surface.UpdateLayout();
        Present(surface);
        surface.UpdateLayout();
        Assert.True(child.Initialized);
        Assert.Equal(UiPresenceState.Entering, child.State);
        Assert.Equal(0, entered);
    }

    private static void Present(UiSurface surface)
    {
        surface.ConfirmVisualPresentation(surface.CaptureVisualPresentation());
        surface.DeliverVisualNotifications();
        surface.UpdateLayout();
    }

    private sealed class Host(UiMotionPolicy policy = UiMotionPolicy.Full) : IUiVisualHost
    {
        public bool CanPresent => true;

        public UiMotionOptions Motion { get; } = new() { Policy = policy };

        public bool Record(Definitions.Widget widget, Lumyte.Graphics.Drawing2D.DrawingCanvas2D canvas) => false;
    }
}
