using System.Runtime.CompilerServices;
using Xunit;

namespace Lumyte.Signals.Tests;

/// <summary>Checks dependency ownership and reference release when evaluation storage is reused.</summary>
public sealed class DependencyLifetimeTests
{
    /// <summary>Checks a failed branch switch retains both dependency sets until a successful retry.</summary>
    [Fact]
    public void FailedComputedRetainsOldAndNewDependenciesUntilRecovery()
    {
        var useNew = Signal.Create(false);
        var oldInput = Signal.Create(1);
        var newInput = Signal.Create(2);
        var fail = Signal.Create(false);
        Computed<int> computed = Signal.Computed(() =>
        {
            int result = useNew.Value ? newInput.Value : oldInput.Value;
            if (fail.Value)
            {
                throw new ArithmeticException();
            }

            return result;
        });
        int seen = 0;
        int runs = 0;
        using SignalEffect effect = Signal.Effect(() =>
        {
            runs++;
            try
            {
                seen = computed.Value;
            }
            catch (ArithmeticException)
            {
                seen = -1;
            }
        });
        fail.Value = true;
        useNew.Value = true;
        int before = runs;
        oldInput.Value = 3;
        Assert.Equal(before + 1, runs);
        before = runs;
        newInput.Value = 4;
        Assert.Equal(before + 1, runs);
        fail.Value = false;
        Assert.Equal(4, seen);
        before = runs;
        oldInput.Value = 5;
        Assert.Equal(before, runs);
    }

    /// <summary>Checks an escaped capture is independent from storage reused by later evaluations.</summary>
    [Fact]
    public void CapturedReadSetsSurviveObserverChurn()
    {
        var capturedSource = Signal.Create(1);
        var otherSource = Signal.Create(2);
        _ = Signal.CaptureReads(() => capturedSource.Value, out SignalReadSet captured);
        for (int index = 3; index < 30; index++)
        {
            using SignalEffect temporary = Signal.Effect(() => _ = otherSource.Value);
            otherSource.Value = index;
        }

        Assert.True(captured.IsCurrent);
        int runs = 0;
        using SignalEffect replay = Signal.Effect(() =>
        {
            captured.Read();
            runs++;
        });
        otherSource.Value = 30;
        Assert.Equal(1, runs);
        capturedSource.Value = 4;
        Assert.Equal(2, runs);
        Assert.False(captured.IsCurrent);
    }

    /// <summary>Checks repeated temporary evaluations cannot borrow another observer's active dependencies.</summary>
    [Fact]
    public void LiveObserversKeepIndependentDependenciesDuringChurn()
    {
        var first = Signal.Create(0);
        var second = Signal.Create(0);
        var temporarySource = Signal.Create(0);
        int firstRuns = 0;
        int secondRuns = 0;
        using SignalEffect firstEffect = Signal.Effect(() =>
        {
            _ = first.Value;
            firstRuns++;
        });
        using SignalEffect secondEffect = Signal.Effect(() =>
        {
            _ = second.Value;
            secondRuns++;
        });
        for (int index = 1; index <= 20; index++)
        {
            using SignalEffect temporary = Signal.Effect(() => _ = temporarySource.Value);
            temporarySource.Value = index;
            first.Value = index;
            Assert.Equal(index + 1, firstRuns);
            Assert.Equal(index, secondRuns);
            second.Value = index;
            Assert.Equal(index + 1, secondRuns);
        }
    }

    /// <summary>Checks a disposed observer and returned storage do not keep its source payload alive.</summary>
    [Fact]
    public void DisposedEffectsReleaseSourceReferences()
    {
        (SignalEffect effect, WeakReference payload) = CreateDisposedEffect();
        Collect();
        Assert.False(payload.IsAlive);
        GC.KeepAlive(effect);
    }

    /// <summary>Checks storage from a failed initial evaluation releases partially collected dependencies.</summary>
    [Fact]
    public void FailedInitialEffectsReleaseSourceReferences()
    {
        WeakReference payload = CreateFailedEffect();
        Collect();
        Assert.False(payload.IsAlive);
    }

    /// <summary>Checks a live graph releases an obsolete source when its dependency set changes.</summary>
    [Fact]
    public void ReplacedDependenciesReleaseDiscardedSourceReferences()
    {
        (SignalEffect effect, WeakReference payload) = CreateSwitchedGraph();
        using (effect)
        {
            Collect();
            Assert.False(payload.IsAlive);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (SignalEffect Effect, WeakReference Payload) CreateDisposedEffect()
    {
        object payload = new();
        var source = Signal.Create(payload);
        SignalEffect effect = Signal.Effect(() => _ = source.Value);
        effect.Dispose();
        return (effect, new WeakReference(payload));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateFailedEffect()
    {
        object payload = new();
        var source = Signal.Create(payload);
        Assert.Throws<ArithmeticException>(() => Signal.Effect(() =>
        {
            _ = source.Value;
            throw new ArithmeticException();
        }));
        return new WeakReference(payload);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (SignalEffect Effect, WeakReference Payload) CreateSwitchedGraph()
    {
        object payload = new();
        var current = Signal.Create(payload);
        var trigger = Signal.Create(0);
        Computed<int> computed = Signal.Computed(() =>
        {
            _ = current.Value;
            return trigger.Value;
        });
        SignalEffect effect = Signal.Effect(() => _ = computed.Value);
        current = Signal.Create(new object());
        trigger.Value = 1;
        return (effect, new WeakReference(payload));
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
