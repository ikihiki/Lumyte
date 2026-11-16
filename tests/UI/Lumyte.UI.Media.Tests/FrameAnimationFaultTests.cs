using System.Reflection;
using System.Runtime.ExceptionServices;
using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Media.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Media.Tests;

/// <summary>Checks custom frame hooks obey the existing animation fault and lifetime contract.</summary>
public sealed class FrameAnimationFaultTests
{
    /// <summary>Checks every freeze entry point latches a custom hook failure.</summary>
    /// <param name="boundary">The owner freeze boundary.</param>
    [Theory]
    [InlineData("suspend")]
    [InlineData("hidden")]
    [InlineData("navigation")]
    public void NullFrameFailureLatchesAnimationAndStateFailure(string boundary)
    {
        using var fixture = new Fixture();
        Exception? error = Record.Exception(() => fixture.Freeze(boundary));
        Assert.Same(fixture.Failure, error);
        Assert.False(fixture.Surface.HasRunningAnimations);
        Assert.False(fixture.Surface.HasPendingAnimationUpdates);
        Assert.Same(fixture.Failure, Record.Exception(fixture.Surface.AdvanceAnimations));
        UiStateUpdateException stateFailure = Assert.Throws<UiStateUpdateException>(fixture.Surface.UpdateTree);
        Assert.Same(fixture.Failure, stateFailure.InnerException);
    }

    /// <summary>Checks a throwing earlier hook cannot skip later frame participants or ordinary producers.</summary>
    /// <param name="boundary">The owner freeze boundary.</param>
    [Theory]
    [InlineData("suspend")]
    [InlineData("hidden")]
    [InlineData("navigation")]
    public void NullFrameFailureStillFreezesAllParticipants(string boundary)
    {
        using var fixture = new Fixture();
        Assert.All(fixture.Producers, producer => Assert.True(ReadField<bool>(producer, "_wasEnabled")));
        Assert.NotNull(ReadField<TimePoint?>(fixture.Busy, "_lastTime"));
        Assert.Same(fixture.Failure, Record.Exception(() => fixture.Freeze(boundary)));
        Assert.Null(ReadField<TimePoint?>(fixture.Busy, "_lastTime"));
        Assert.Equal(1, fixture.Later.Runtime.NullFrames);
        Assert.All(fixture.Producers, producer => Assert.False(ReadField<bool>(producer, "_wasEnabled")));
        Assert.Equal(0.25, fixture.Busy.Phase);
    }

    /// <summary>Checks null notifications cannot recursively start an animation phase.</summary>
    /// <param name="boundary">The owner freeze boundary.</param>
    [Theory]
    [InlineData("suspend")]
    [InlineData("hidden")]
    [InlineData("navigation")]
    public void NullFrameHookRejectsAnimationReentrancy(string boundary)
    {
        using var fixture = new Fixture();
        Exception? nested = null;
        fixture.First.OnNull = () => nested = Record.Exception(fixture.Surface.AdvanceAnimations);
        Assert.Null(Record.Exception(() => fixture.Freeze(boundary)));
        Assert.IsType<InvalidOperationException>(nested);
        Assert.Equal(1, fixture.First.Runtime.NullFrames);
    }

    /// <summary>Checks null notifications may remove or dispose the tree without visiting stale elements.</summary>
    /// <param name="boundary">The owner freeze boundary.</param>
    /// <param name="dispose">Whether the hook disposes rather than unmounts the surface.</param>
    [Theory]
    [InlineData("suspend", false)]
    [InlineData("suspend", true)]
    [InlineData("hidden", false)]
    [InlineData("hidden", true)]
    [InlineData("navigation", false)]
    [InlineData("navigation", true)]
    public void NullFrameHookCanUnmountOrDisposeWithoutStaleNotification(string boundary, bool dispose)
    {
        using var fixture = new Fixture();
        fixture.First.OnNull = () =>
        {
            if (dispose)
            {
                fixture.Surface.Dispose();
            }
            else
            {
                fixture.Surface.Rebuild(Panel());
                fixture.Surface.UpdateTree();
            }
        };
        Assert.Null(Record.Exception(() => fixture.Freeze(boundary)));
        if (!dispose)
        {
            // A hook during publish queues removal until the next owner boundary.
            fixture.Surface.UpdateTree();
        }

        Assert.False(fixture.First.Runtime.IsMounted);
        Assert.False(fixture.Busy.IsMounted);
        Assert.False(fixture.Later.Runtime.IsMounted);
        Assert.Equal(boundary == "hidden" && !dispose ? 1 : 0, fixture.Later.Runtime.NullFrames);
        Assert.False(fixture.Surface.HasRunningAnimations);
    }

    /// <summary>Checks hiding only the failing participant also freezes its still-visible peers.</summary>
    [Fact]
    public void HiddenOnlyNullFrameFailureFreezesVisibleParticipantsAndProducers()
    {
        using var fixture = new Fixture();
        fixture.Surface.Rebuild(fixture.BuildWithHiddenFirst());

        Assert.Same(fixture.Failure, Record.Exception(fixture.Surface.UpdateTree));

        Assert.True(fixture.Busy.Widget.IsVisible);
        Assert.True(fixture.Later.IsVisible);
        Assert.Null(ReadField<TimePoint?>(fixture.Busy, "_lastTime"));
        Assert.Equal(1, fixture.Later.Runtime.NullFrames);
        Assert.All(fixture.Producers, producer => Assert.False(ReadField<bool>(producer, "_wasEnabled")));
        Assert.Equal(0.25, fixture.Busy.Phase);
        Assert.False(fixture.Surface.HasRunningAnimations);
        Assert.False(fixture.Surface.HasPendingAnimationUpdates);
        Assert.Same(fixture.Failure, Record.Exception(fixture.Surface.AdvanceAnimations));
    }

    /// <summary>Checks the ordinary frame loop cleans up a hidden hook failure, not just explicit freeze boundaries.</summary>
    [Fact]
    public void AdvanceAnimationsNullFrameFailureFreezesAllParticipantsAndProducers()
    {
        using var fixture = new Fixture();
        int laterFrames = fixture.Later.Runtime.Frames;

        // Change only passive visibility so AdvanceAnimations itself reaches the hidden-hook path.
        fixture.First.IsVisible = false;
        fixture.AdvanceClock();

        Assert.Same(fixture.Failure, Record.Exception(fixture.Surface.AdvanceAnimations));

        Assert.Equal(1, fixture.First.Runtime.NullFrames);
        Assert.Equal(1, fixture.Later.Runtime.NullFrames);
        Assert.Equal(laterFrames, fixture.Later.Runtime.Frames);
        Assert.Null(ReadField<TimePoint?>(fixture.Busy, "_lastTime"));
        Assert.Equal(0.25, fixture.Busy.Phase);
        Assert.All(fixture.Producers, producer => Assert.False(ReadField<bool>(producer, "_wasEnabled")));
        Assert.False(fixture.Surface.HasRunningAnimations);
        Assert.False(fixture.Surface.HasPendingAnimationUpdates);
        Assert.Same(fixture.Failure, Record.Exception(fixture.Surface.AdvanceAnimations));
        UiStateUpdateException stateFailure = Assert.Throws<UiStateUpdateException>(fixture.Surface.UpdateTree);
        Assert.Same(fixture.Failure, stateFailure.InnerException);
    }

    /// <summary>Checks catching a nested suspension failure cannot let the outer frame advance later work.</summary>
    [Fact]
    public void RunningFrameThatCatchesSuspendFailureDoesNotAdvanceLaterWork()
    {
        using var fixture = new Fixture();
        int firstFrames = fixture.First.Runtime.Frames;
        int laterFrames = fixture.Later.Runtime.Frames;
        TimePoint[] producerTimes = fixture.Producers.Select(producer => ReadField<TimePoint>(producer, "_lastWall")).ToArray();
        Exception? caught = null;
        fixture.First.OnFrame = () => caught = Record.Exception(() => fixture.Surface.SetAnimationUpdatesEnabled(false));
        fixture.AdvanceClock();

        Assert.Same(fixture.Failure, Record.Exception(fixture.Surface.AdvanceAnimations));

        Assert.Same(fixture.Failure, caught);
        Assert.Equal(firstFrames + 1, fixture.First.Runtime.Frames);
        Assert.Equal(laterFrames, fixture.Later.Runtime.Frames);
        Assert.Equal(1, fixture.First.Runtime.NullFrames);
        Assert.Equal(1, fixture.Later.Runtime.NullFrames);
        Assert.Null(ReadField<TimePoint?>(fixture.Busy, "_lastTime"));
        Assert.Equal(0.25, fixture.Busy.Phase);
        Assert.Equal(producerTimes, fixture.Producers.Select(producer => ReadField<TimePoint>(producer, "_lastWall")).ToArray());
        Assert.All(fixture.Producers, producer => Assert.False(ReadField<bool>(producer, "_wasEnabled")));
        Assert.False(fixture.Surface.HasRunningAnimations);
        Assert.False(fixture.Surface.HasPendingAnimationUpdates);
    }

    private static T ReadField<T>(object instance, string name)
    {
        for (Type? type = instance.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) is { } field)
            {
                return (T)field.GetValue(instance)!;
            }
        }

        throw new InvalidOperationException($"Missing field {name}.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ManualClock _clock = new();
        private readonly UiAnimated<float?>[] _animations;

        internal Fixture()
        {
            var target = Signal.Create<float?>(10);
            _animations = Enumerable.Range(0, 2).Select(_ => Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear)).ToArray();
            Surface = new UiSurface(Build(true), new Dispatcher(), _clock);
            Busy = Assert.IsType<BusyIndicatorElement>(Surface.RootElement.Children[1]);
            Surface.AdvanceAnimations();
            target.Value = 50;
            Surface.AdvanceAnimations();
            _clock.Advance(Duration.FromSeconds(0.25));
            Surface.AdvanceAnimations();
            Assert.Equal(0.25, Busy.Phase);
            First.OnNull = () => throw Failure;
        }

        internal UiSurface Surface { get; }

        internal Probe First { get; } = new();

        internal Probe Later { get; } = new();

        internal BusyIndicatorElement Busy { get; }

        internal Exception Failure { get; } = new ArithmeticException("custom null frame failure");

        internal object[] Producers => _animations.Select(animation => ReadField<object>(animation, "_producer")).ToArray();

        public void Dispose() => Surface.Dispose();

        internal void Freeze(string boundary)
        {
            switch (boundary)
            {
                case "suspend":
                    Surface.SetAnimationUpdatesEnabled(false);
                    break;
                case "hidden":
                    Surface.Rebuild(Build(false));
                    Surface.UpdateTree();
                    break;
                case "navigation":
                    try
                    {
                        typeof(UiSurface).GetMethod("FreezeValueAnimations", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Surface, null);
                    }
                    catch (TargetInvocationException error) when (error.InnerException is not null)
                    {
                        ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(boundary));
            }
        }

        internal void AdvanceClock() => _clock.Advance(Duration.FromSeconds(0.25));

        internal Widget BuildWithHiddenFirst()
        {
            First.IsVisible = false;
            return Build(true);
        }

        private Widget Build(bool visible) => Panel(isVisible: visible)[First, BusyIndicator(), Later, Rectangle(width: _animations[0]), Rectangle(width: _animations[1])];
    }

    private sealed class Probe : Widget
    {
        internal Action? OnNull { get; set; }

        internal Action? OnFrame { get; set; }

        internal ProbeElement Runtime { get; private set; } = null!;

        protected override UiElement CreateElement() => Runtime = new(this);
    }

    private sealed class ProbeElement(Probe widget) : UiElement<Probe>(widget)
    {
        internal int NullFrames { get; private set; }

        internal int Frames { get; private set; }

        protected override bool HasFrameAnimation => true;

        protected override void OnAnimationFrame(TimePoint? now)
        {
            Assert.True(IsMounted);
            if (now is null)
            {
                NullFrames++;
                Widget.OnNull?.Invoke();
            }
            else
            {
                Frames++;
                Widget.OnFrame?.Invoke();
            }
        }
    }

    private sealed class Dispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            action();
            return ValueTask.CompletedTask;
        }

        public ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default) => ValueTask.FromResult(action());
    }
}
