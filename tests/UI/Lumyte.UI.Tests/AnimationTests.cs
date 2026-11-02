using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Checks real generated animation arguments and surface-owned time, state and notification boundaries.</summary>
public sealed class AnimationTests
{
    /// <summary>Checks model updates tween from the current sampled pose rather than the previous displayed value.</summary>
    [Fact]
    public void TargetsRetargetAtTheCurrentTimeAndNotifyOnlyTheEffectiveCompletion()
    {
        var target = Signal.Create<float?>(10);
        var clock = new ManualClock();
        int completed = 0;
        UiAnimated<float?> width = Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear, () => completed++);
        using var surface = new UiSurface(Rectangle(width: width));
        surface.SetAnimationClock(clock);
        surface.AdvanceAnimations();
        Assert.Equal(10, surface.Root.Width);
        target.Value = 30;
        Assert.True(surface.HasPendingAnimationUpdates);
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(0.5));
        surface.AdvanceAnimations();
        Assert.Equal(20, surface.Root.Width);
        clock.Advance(Duration.FromSeconds(0.25));
        target.Value = 50;
        Assert.Equal(20, surface.Root.Width);
        surface.AdvanceAnimations();
        Assert.Equal(25, surface.Root.Width);
        clock.Advance(Duration.FromSeconds(0.5));
        surface.AdvanceAnimations();
        Assert.Equal(37.5f, surface.Root.Width);
        clock.Advance(Duration.FromSeconds(0.5));
        surface.AdvanceAnimations();
        Assert.Equal(50, surface.Root.Width);
        Assert.Equal(1, completed);
        Assert.False(surface.HasRunningAnimations);
        Assert.False(surface.HasPendingAnimationUpdates);
    }

    /// <summary>Checks suspension does not catch up wall time and permits only one surface per producer.</summary>
    [Fact]
    public void ProducerLifetimeFollowsItsSurfaceAndSuspensionFreezesTime()
    {
        var target = Signal.Create(0f);
        var clock = new ManualClock();
        UiAnimated<float?> width = Animate(target, Duration.FromSeconds(1), AnimationInterpolators.Float, AnimationEasing.Linear).Select<float?>(value => value);
        using var surface = new UiSurface(Rectangle(width: width));
        surface.SetAnimationClock(clock);
        surface.AdvanceAnimations();
        target.Value = 100;
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(0.25));
        surface.AdvanceAnimations();
        Assert.Equal(25, surface.Root.Width);
        Assert.Throws<InvalidOperationException>(() => new UiSurface(Rectangle(width: width)));
        surface.SetAnimationUpdatesEnabled(false);
        clock.Advance(Duration.FromSeconds(10));
        surface.AdvanceAnimations();
        Assert.Equal(25, surface.Root.Width);
        Assert.False(surface.HasRunningAnimations);
        surface.SetAnimationUpdatesEnabled(true);
        surface.AdvanceAnimations();
        Assert.Equal(25, surface.Root.Width);
        clock.Advance(Duration.FromSeconds(0.25));
        surface.AdvanceAnimations();
        Assert.Equal(50, surface.Root.Width);
        surface.Dispose();
        target.Value = 75;
        using var reconnected = new UiSurface(Rectangle(width: width));
        reconnected.SetAnimationClock(clock);
        reconnected.AdvanceAnimations();
        Assert.Equal(75, reconnected.Root.Width);
        Assert.False(reconnected.HasRunningAnimations);
    }

    /// <summary>Checks shared timeline channels, marker ordering, queued controls and held values after release.</summary>
    [Fact]
    public void TimelineChannelsAdvanceTogetherAndControlsDoNotReplayMarkers()
    {
        var clock = new ManualClock();
        var x = AnimationChannel<float?>.Create();
        var y = AnimationChannel<float?>.Create();
        var builder = new AnimationTimelineBuilder();
        builder.Add(Duration.Zero, new Tween<float?>(10, 30, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear), x);
        builder.Add(Duration.Zero, new Tween<float?>(20, 60, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear), y);
        builder.AddEvent(Duration.FromSeconds(0.5), "middle");
        var events = new List<string>();
        UiAnimationClip clip = Animate(
            builder.Build(),
            onEvent: _ => events.Add("event"),
            onCompleted: () => events.Add("done"));
        using var surface = new UiSurface(Rectangle(width: clip.Value(x, (float?)10), height: clip.Value(y, (float?)20)));
        surface.SetAnimationClock(clock);
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(0.5));
        surface.AdvanceAnimations();
        Assert.Equal(20, surface.Root.Width);
        Assert.Equal(40, surface.Root.Height);
        Assert.Equal(["event"], events);
        clip.Pause();
        surface.AdvanceAnimations();
        Assert.False(surface.HasRunningAnimations);
        clock.Advance(Duration.FromSeconds(5));
        surface.AdvanceAnimations();
        Assert.Equal(20, surface.Root.Width);
        clip.Seek(Duration.FromSeconds(0.75));
        surface.AdvanceAnimations();
        Assert.Equal(25, surface.Root.Width);
        Assert.Equal(["event"], events);
        clip.Resume();
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(0.25));
        surface.AdvanceAnimations();
        Assert.Equal(30, surface.Root.Width);
        Assert.Equal(["event", "done"], events);
        clip.Stop();
        surface.AdvanceAnimations();
        Assert.Equal(30, surface.Root.Width);
        Assert.False(surface.HasRunningAnimations);
    }

    /// <summary>Checks all producer samples succeed before any projected value changes.</summary>
    [Fact]
    public void SamplingFailureDoesNotPublishEarlierProducerValues()
    {
        var target = Signal.Create<float?>(10);
        var clock = new ManualClock();
        UiAnimated<float?> first = Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear);
        UiAnimated<float?> failing = Animate(target, Duration.FromSeconds(1), new FailingInterpolator(), AnimationEasing.Linear);
        using var surface = new UiSurface(Rectangle(width: first, height: failing));
        surface.SetAnimationClock(clock);
        surface.AdvanceAnimations();
        target.Value = 30;
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(0.5));
        Assert.Throws<ArithmeticException>(surface.AdvanceAnimations);
        Assert.Equal(10, surface.Root.Width);
        Assert.Equal(10, surface.Root.Height);
        Assert.False(surface.HasRunningAnimations);
    }

    /// <summary>Checks the display color space and nullable endpoints without UI-specific color clamping.</summary>
    [Fact]
    public void ColorInterpolationUsesLinearPremultipliedComponents()
    {
        Color2D midpoint = UiAnimationInterpolators.Color.Interpolate(new(0, 0, 0, 1), new(1, 1, 1, 1), 0.5f);
        Assert.InRange(midpoint.R, 0.735f, 0.736f);
        Assert.Equal(1, midpoint.A);
        Color2D transparent = UiAnimationInterpolators.Color.Interpolate(new(1, 0, 0, 0), new(0, 0, 1, 0), 0.5f);
        Assert.Equal(default, transparent);
        IAnimationInterpolator<float?> nullable = UiAnimationInterpolators.Nullable(AnimationInterpolators.Float);
        Assert.Null(nullable.Interpolate(null, 10, 0.5f));
        Assert.Equal(10, nullable.Interpolate(null, 10, 1));
    }

    /// <summary>Checks a fresh declaration retains a compatible producer's current interpolation across full builds.</summary>
    [Fact]
    public void ReconstructingAnimateRetainsTheCurrentTween()
    {
        var target = Signal.Create<float?>(10);
        var clock = new ManualClock();
        Definitions.Rectangle Build() => Rectangle(width: Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear));
        using var surface = new UiSurface(Build());
        surface.SetAnimationClock(clock);
        surface.AdvanceAnimations();
        target.Value = 50;
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(0.25));
        surface.AdvanceAnimations();
        Assert.Equal(20, surface.Root.Width);
        surface.Rebuild(Build());
        surface.UpdateTree();
        Assert.Equal(20, surface.Root.Width);
        clock.Advance(Duration.FromSeconds(0.25));
        surface.AdvanceAnimations();
        Assert.Equal(30, surface.Root.Width);
        clock.Advance(Duration.FromSeconds(0.5));
        surface.AdvanceAnimations();
        Assert.Equal(50, surface.Root.Width);
        Assert.False(surface.HasRunningAnimations);
    }

    /// <summary>Checks reads and layout never sample time, and immediate completion produces no recurring work.</summary>
    [Fact]
    public void ZeroDurationCompletesOnceAtTheExplicitAnimationBoundary()
    {
        var target = Signal.Create<float?>(10);
        int completed = 0;
        UiAnimated<float?> width = Animate(target, Duration.Zero, UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), onCompleted: () => completed++);
        using var surface = new UiSurface(Rectangle(width: width));
        surface.AdvanceAnimations();
        target.Value = 40;
        surface.UpdateLayout();
        Assert.Equal(10, width.Value);
        Assert.Equal(0, completed);
        surface.AdvanceAnimations();
        Assert.Equal(40, surface.Root.Width);
        Assert.Equal(1, completed);
        Assert.False(surface.HasRunningAnimations);
        surface.AdvanceAnimations();
        Assert.Equal(1, completed);
    }

    /// <summary>Checks hidden components do not catch up a missed interval and model watchers remain live.</summary>
    [Fact]
    public void HiddenSubtreeFreezesDisplayTimeAndKeepsTheLatestTarget()
    {
        var target = Signal.Create<float?>(10);
        var visible = Signal.Create(true);
        var clock = new ManualClock();
        UiAnimated<float?> width = Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear);
        using var surface = new UiSurface(Rectangle(width: width, isVisible: visible));
        surface.SetAnimationClock(clock);
        surface.AdvanceAnimations();
        target.Value = 50;
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(0.25));
        surface.AdvanceAnimations();
        visible.Value = false;
        surface.UpdateTree();
        surface.AdvanceAnimations();
        Assert.False(surface.HasRunningAnimations);
        clock.Advance(Duration.FromSeconds(10));
        target.Value = 100;
        visible.Value = true;
        surface.UpdateTree();
        surface.AdvanceAnimations();
        Assert.Equal(20, surface.Root.Width);
        clock.Advance(Duration.FromSeconds(0.5));
        surface.AdvanceAnimations();
        Assert.Equal(60, surface.Root.Width);
    }

    /// <summary>Checks cancellation from one marker revokes the remaining staged markers and completion.</summary>
    [Fact]
    public void CancellationInsideAMarkerRevokesLaterNotifications()
    {
        var channel = AnimationChannel<float?>.Create();
        var builder = new AnimationTimelineBuilder();
        builder.Add(Duration.Zero, new Tween<float?>(10, 30, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear), channel);
        builder.AddEvent(Duration.FromSeconds(0.25), "first");
        builder.AddEvent(Duration.FromSeconds(0.5), "second");
        var clock = new ManualClock();
        int markers = 0;
        int completed = 0;
        UiAnimationClip? clip = null;
        clip = Animate(
            builder.Build(),
            onEvent: _ =>
            {
                markers++;
                clip!.Cancel();
            },
            onCompleted: () => completed++);
        using var surface = new UiSurface(Rectangle(width: clip.Value(channel, (float?)10)));
        surface.SetAnimationClock(clock);
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(1));
        surface.AdvanceAnimations();
        Assert.Equal(1, markers);
        Assert.Equal(0, completed);
        Assert.Equal(30, surface.Root.Width);
        surface.AdvanceAnimations();
        Assert.Equal(AnimationPlaybackState.Cancelled, clip.State);
        Assert.False(surface.HasRunningAnimations);
    }

    /// <summary>Checks looped instantaneous clips evaluate once without allocating recurring work.</summary>
    [Fact]
    public void ZeroDurationLoopAndZeroSpeedReleaseContinuousFrames()
    {
        var channel = AnimationChannel<float?>.Create();
        var builder = new AnimationTimelineBuilder();
        builder.Add(Duration.Zero, new Tween<float?>(10, 30, Duration.Zero, UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear), channel);
        UiAnimationClip clip = Animate(builder.Build(), AnimationWrapMode.Loop);
        using var surface = new UiSurface(Rectangle(width: clip.Value(channel, (float?)10)));
        surface.AdvanceAnimations();
        Assert.Equal(30, surface.Root.Width);
        Assert.False(surface.HasRunningAnimations);
        Assert.False(surface.HasPendingAnimationUpdates);
        clip.Speed = 0;
        Assert.True(surface.HasPendingAnimationUpdates);
        surface.AdvanceAnimations();
        Assert.False(surface.HasRunningAnimations);
        Assert.False(surface.HasPendingAnimationUpdates);
    }

    /// <summary>Checks passive definitions do not connect, mutable selectors are rejected and detached surfaces release watches.</summary>
    [Fact]
    public void PassiveDefinitionsAndSelectorsRespectModelOwnership()
    {
        var target = Signal.Create(10f);
        UiAnimated<float> value = Animate(target, Duration.FromSeconds(1), AnimationInterpolators.Float);
        Assert.False(((IUiAnimationProjection)value).Producer.IsConnected);
        Assert.Equal(10, value.Peek());
        var other = Signal.Create(2f);
        Assert.Throws<InvalidOperationException>(() => value.Select(item => item + other.Value));
        Assert.Throws<InvalidOperationException>(() => value.Select(item => other.Value = item));
        using var surface = new UiSurface(Rectangle(width: value.Select<float?>(item => item)));
        surface.AdvanceAnimations();
        surface.Dispose();
        Assert.False(((IUiAnimationProjection)value).Producer.IsConnected);
        target.Value = 70;
        Assert.Equal(70, value.Peek());
        Assert.False(surface.HasPendingAnimationUpdates);
    }

    /// <summary>Checks a callback can dispose the surface without post-callback tree access.</summary>
    [Fact]
    public void CompletionMayDisposeItsSurface()
    {
        var target = Signal.Create<float?>(10);
        UiSurface? surface = null;
        UiAnimated<float?> value = Animate(target, Duration.Zero, UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), onCompleted: () => surface!.Dispose());
        using (surface = new(Rectangle(width: value)))
        {
            surface.AdvanceAnimations();
            target.Value = 20;
            surface.AdvanceAnimations();
            Assert.False(surface.HasRunningAnimations);
        }
    }

    /// <summary>Checks a completion survives a build triggered by that producer's final output.</summary>
    [Fact]
    public void CompletionSurvivesReconstructionAtTheFinalValue()
    {
        var target = Signal.Create<float?>(10);
        var clock = new ManualClock();
        int completed = 0;
        using var surface = new UiSurface(() => Rectangle(width: Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear, () => completed++)));
        surface.SetAnimationClock(clock);
        surface.AdvanceAnimations();
        target.Value = 30;
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(1));
        surface.AdvanceAnimations();
        surface.AdvanceAnimations();
        Assert.Equal(30, surface.Root.Width);
        Assert.Equal(1, completed);
        Assert.False(surface.HasPendingAnimationUpdates);
    }

    /// <summary>Checks an earlier completion can retarget another producer before its staged notification is delivered.</summary>
    [Fact]
    public void RetargetDuringNotificationDeliverySupersedesTheOldCompletion()
    {
        var firstTarget = Signal.Create<float?>(10);
        var secondTarget = Signal.Create<float?>(10);
        var clock = new ManualClock();
        int completed = 0;
        int beforeRetarget = -1;
        UiAnimated<float?> first = Animate(firstTarget, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear, () =>
        {
            beforeRetarget = completed;
            secondTarget.Value = 50;
        });
        UiAnimated<float?> second = Animate(secondTarget, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear, () => completed++);
        using var surface = new UiSurface(Rectangle(width: first, height: second));
        surface.SetAnimationClock(clock);
        firstTarget.Value = 30;
        secondTarget.Value = 30;
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(1));
        surface.AdvanceAnimations();
        Assert.InRange(beforeRetarget, 0, 1);
        Assert.Equal(beforeRetarget, completed);
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(1));
        surface.AdvanceAnimations();
        Assert.Equal(50, surface.Root.Height);
        Assert.Equal(beforeRetarget + 1, completed);
    }

    /// <summary>Checks full construction dependencies retain animation values before publishing a disconnected declaration.</summary>
    [Fact]
    public void FullBuildDependenciesDoNotResetInterpolationOrContinuouslyRebuild()
    {
        var target = Signal.Create<float?>(10);
        var unrelated = Signal.Create(0);
        var clock = new ManualClock();
        int builds = 0;
        using var surface = new UiSurface(() =>
        {
            builds++;
            _ = unrelated.Value;
            return Rectangle(width: Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear));
        });
        surface.SetAnimationClock(clock);
        surface.AdvanceAnimations();
        target.Value = 50;
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(0.25));
        surface.AdvanceAnimations();
        Assert.Equal(20, surface.Root.Width);
        unrelated.Value++;
        surface.UpdateTree();
        Assert.Equal(20, surface.Root.Width);
        clock.Advance(Duration.FromSeconds(0.25));
        surface.AdvanceAnimations();
        Assert.Equal(30, surface.Root.Width);
        int stableBuilds = builds;
        surface.UpdateTree();
        surface.UpdateTree();
        Assert.Equal(stableBuilds, builds);
        Assert.InRange(builds, 2, 8);
    }

    /// <summary>Checks invalid borrowed services cannot attach a tree or retain an animation watch.</summary>
    [Fact]
    public void ConstructorValidationPrecedesTreeConnection()
    {
        var target = Signal.Create<float?>(10);
        UiAnimated<float?> width = Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float));
        Definitions.Rectangle root = Rectangle(width: width);
        Assert.Equal("animationClock", Assert.Throws<ArgumentNullException>(() => new UiSurface(root, null!, null!)).ParamName);
        Assert.False(((IUiAnimationProjection)width).Producer.IsConnected);
        Assert.Equal("dispatcher", Assert.Throws<ArgumentNullException>(() => new UiSurface(root, null!, new ManualClock())).ParamName);
        Assert.False(((IUiAnimationProjection)width).Producer.IsConnected);
        using var surface = new UiSurface(root);
        surface.UpdateLayout();
        Assert.Equal(10, surface.Root.Width);
    }

    /// <summary>Checks animation preparation preserves the construction override order across inline producer inheritance.</summary>
    [Fact]
    public void WithOverridesRemainEffectiveAcrossAnimationReconstruction()
    {
        var target = Signal.Create<float?>(10);
        var rebuild = Signal.Create(0);
        var clock = new ManualClock();
        using var surface = new UiSurface(() =>
        {
            _ = rebuild.Value;
            return Rectangle(width: Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float)), with: [rectangle => rectangle.Width = 99]);
        });
        UiElement instance = surface.RootElement;
        surface.SetAnimationClock(clock);
        surface.AdvanceAnimations();
        Assert.Equal(99, surface.Root.Width);
        target.Value = 50;
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(0.25));
        surface.AdvanceAnimations();
        rebuild.Value++;
        surface.UpdateTree();
        Assert.Same(instance, surface.RootElement);
        Assert.Equal(99, surface.Root.Width);
        clock.Advance(Duration.FromSeconds(0.25));
        surface.AdvanceAnimations();
        Assert.Equal(99, surface.Root.Width);
    }

    /// <summary>Checks stopped shared producers do not enumerate and allocate per connected element.</summary>
    /// <param name="count">The number of elements sharing one projection.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1000)]
    public void StoppedAnimationQueriesAllocateNothing(int count)
    {
        var target = Signal.Create<float?>(10);
        UiAnimated<float?> animation = Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float));
        using var surface = new UiSurface(Panel()[Enumerable.Range(0, count).Select(_ => Rectangle(width: animation)).ToArray()]);
        var clock = new ManualClock();
        surface.SetAnimationClock(clock);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        target.Value = 20;
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(1));
        surface.AdvanceAnimations();
        Assert.Equal(20, animation.Value);
        for (int warmup = 0; warmup < 20; warmup++)
        {
            surface.AdvanceAnimations();
            _ = surface.HasRunningAnimations;
            _ = surface.HasPendingAnimationUpdates;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool active = false;
        for (int iteration = 0; iteration < 100; iteration++)
        {
            surface.AdvanceAnimations();
            active |= surface.HasRunningAnimations | surface.HasPendingAnimationUpdates;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(active);
        Assert.Equal(0, allocated);
    }

    private sealed class FailingInterpolator : IAnimationInterpolator<float?>
    {
        public float? Interpolate(float? from, float? to, float amount) => throw new ArithmeticException("sample");
    }
}
