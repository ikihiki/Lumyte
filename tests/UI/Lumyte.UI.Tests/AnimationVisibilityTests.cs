using System.Runtime.CompilerServices;
using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Tests;

/// <summary>Checks hidden independent producers and producers shared across visible branches.</summary>
[Collection(nameof(AnimationAllocationCollection))]
public sealed class AnimationVisibilityTests
{
    /// <summary>Checks hidden pending and running producers retain their targets without allocating on state queries.</summary>
    /// <param name="running">Whether the producers started before their parent was hidden.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenIndependentProducersKeepTheirWorkUntilVisible(bool running)
    {
        var visible = Signal.Create(true);
        Signal<float?>[] targets = Enumerable.Range(0, 1000).Select(_ => Signal.Create<float?>(10)).ToArray();
        UiAnimated<float?>[] animations = targets.Select(target => Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear)).ToArray();
        using var surface = new UiSurface(Panel(isVisible: visible)[animations.Select(animation => Rectangle(width: animation)).ToArray()]);
        var clock = new ManualClock();
        surface.SetAnimationClock(clock);
        foreach (Signal<float?> target in targets)
        {
            target.Value = 20;
        }

        if (running)
        {
            surface.AdvanceAnimations();
            Assert.True(surface.HasRunningAnimations);
        }

        visible.Value = false;
        surface.UpdateTree();
        for (int warmup = 0; warmup < 20; warmup++)
        {
            Assert.False(surface.HasPendingAnimationUpdates);
            Assert.False(surface.HasRunningAnimations);
        }

        bool work = false;
        long allocated;
        using (new AllocationRegion())
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 100; iteration++)
            {
                work |= surface.HasPendingAnimationUpdates | surface.HasRunningAnimations;
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.False(work);
        Assert.Equal(0, allocated);
        visible.Value = true;
        surface.UpdateTree();
        Assert.True(running ? surface.HasRunningAnimations : surface.HasPendingAnimationUpdates);
        surface.AdvanceAnimations();
        clock.Advance(Duration.FromSeconds(1));
        surface.AdvanceAnimations();
        Assert.All(animations, animation => Assert.Equal(20, animation.Value));
    }

    /// <summary>Checks idle presence entries do not box an enumerator on either state query.</summary>
    /// <param name="present">Whether the presence entry is visible or hidden.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdlePresenceStateQueriesDoNotAllocate(bool present)
    {
        using var surface = new UiSurface(Rectangle(isPresent: present, transitions: new UiElementTransitions { AnimateInitial = false }));
        surface.UpdateLayout();
        Assert.Single(surface.VisualEntries);
        for (int warmup = 0; warmup < 20; warmup++)
        {
            Assert.False(surface.HasPendingAnimationUpdates);
            Assert.False(surface.HasRunningAnimations);
        }

        bool work = false;
        long allocated;
        using (new AllocationRegion())
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 100; iteration++)
            {
                work |= surface.HasPendingAnimationUpdates | surface.HasRunningAnimations;
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.False(work);
        Assert.Equal(0, allocated);
    }

    /// <summary>Checks a hidden occurrence does not hide another occurrence of the same producer.</summary>
    [Fact]
    public void SharedProducerFindsVisibleOccurrenceAfterHiddenOccurrence()
    {
        var target = Signal.Create<float?>(10);
        UiAnimated<float?> animation = Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float));
        using var surface = new UiSurface(Panel()[Panel(isVisible: false)[Rectangle(width: animation)], Rectangle(width: animation)]);
        target.Value = 20;
        Assert.True(surface.HasPendingAnimationUpdates);
        surface.AdvanceAnimations();
        Assert.True(surface.HasRunningAnimations);
    }

    /// <summary>Checks the isolated measurement still detects an actual managed allocation.</summary>
    [Fact]
    public void AllocationRegionCountsManagedAllocations()
    {
        long allocated;
        using (new AllocationRegion())
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            byte[] control = AllocateControl();
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(control);
        }

        Assert.True(allocated > 0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] AllocateControl() => new byte[32];

    private sealed class AllocationRegion : IDisposable
    {
        public AllocationRegion()
        {
            // Background GC can discard an unused allocation-context tail and inflate the thread counter.
            // Reserve outside the measured interval; the nonparallel collection keeps other tests out of this budget.
            Assert.True(GC.TryStartNoGCRegion(1024 * 1024));
        }

        public void Dispose() => GC.EndNoGCRegion();
    }
}
