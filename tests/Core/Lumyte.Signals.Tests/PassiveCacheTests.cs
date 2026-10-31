using Xunit;

namespace Lumyte.Signals.Tests;

/// <summary>Checks cached passive validation preserves freshness and retries failed evaluations.</summary>
public sealed class PassiveCacheTests
{
    /// <summary>Checks nested passive values refresh inside batches and after subscription changes.</summary>
    [Fact]
    public void PassiveChainRemainsFreshInsideBatchesAndAfterDeactivation()
    {
        var source = Signal.Create(1);
        IReadSignal<int> chain = source;
        for (int index = 0; index < 32; index++)
        {
            IReadSignal<int> previous = chain;
            chain = Signal.Computed(() => previous.Value + 1);
        }

        Assert.Equal(33, chain.Value);
        Assert.Equal(33, chain.Peek());
        Signal.Batch(() =>
        {
            source.Value = 2;
            Assert.Equal(34, chain.Value);
            source.Value = 3;
            Assert.Equal(35, chain.Peek());
        });
        Assert.Equal(35, chain.Value);
        int seen = 0;
        using (SignalEffect effect = Signal.Effect(() => seen = chain.Value))
        {
            source.Value = 4;
            Assert.Equal(36, seen);
        }

        source.Value = 5;
        Assert.Equal(37, chain.Value);
        Assert.Equal(37, chain.Peek());
    }

    /// <summary>Checks failed reads remain retryable even without an intervening mutation.</summary>
    [Fact]
    public void FailedPassiveReadsRetryWithinTheSameMutationRevision()
    {
        var divisor = Signal.Create(0);
        int attempts = 0;
        Computed<int> quotient = Signal.Computed(() =>
        {
            attempts++;
            return 10 / divisor.Value;
        });
        Assert.Throws<DivideByZeroException>(() => _ = quotient.Value);
        Assert.Throws<DivideByZeroException>(() => _ = quotient.Peek());
        Assert.Equal(2, attempts);
        divisor.Value = 2;
        Assert.Equal(5, quotient.Value);
        Assert.Equal(5, quotient.Peek());
        Assert.Equal(3, attempts);
    }

    /// <summary>Checks a comparer failure cannot mark a pending value as successfully validated.</summary>
    [Fact]
    public void ComparerFailureCanRecoverWithoutAnotherMutation()
    {
        var source = Signal.Create(1);
        var comparer = new RecoverableComparer();
        Computed<int> computed = Signal.Computed(() => source.Value, comparer);
        Assert.Equal(1, computed.Value);
        source.Value = 2;
        comparer.Throw = true;
        Assert.Throws<ArithmeticException>(() => _ = computed.Value);
        comparer.Throw = false;
        Assert.Equal(2, computed.Value);
        source.Value = 3;
        Assert.Equal(3, computed.Value);
    }

    /// <summary>Checks equal computed results still invalidate a read set when their dependencies change.</summary>
    [Fact]
    public void EqualComputedResultsDoNotHideUpstreamRevisionChanges()
    {
        var source = Signal.Create(1);
        Computed<int> parity = Signal.Computed(() => source.Value % 2);
        _ = Signal.CaptureReads(() => parity.Value, out SignalReadSet reads);
        Assert.True(reads.IsCurrent);
        source.Value = 3;
        Assert.Equal(1, parity.Value);
        Assert.False(reads.IsCurrent);
    }

    /// <summary>Checks unrelated mutations validate shared paths without recomputing their values.</summary>
    [Fact]
    public void UnrelatedWritesPreserveCachedSharedValues()
    {
        var source = Signal.Create(1);
        var unrelated = Signal.Create(0);
        int evaluations = 0;
        Computed<int> shared = Signal.Computed(() =>
        {
            evaluations++;
            return source.Value * 2;
        });
        Computed<int> left = Signal.Computed(() => shared.Value + 1);
        Computed<int> right = Signal.Computed(() => shared.Value + 2);
        Computed<int> sum = Signal.Computed(() => left.Value + right.Value);
        Assert.Equal(7, sum.Value);
        unrelated.Value = 1;
        Assert.Equal(7, sum.Value);
        Assert.Equal(1, evaluations);
        source.Value = 2;
        Assert.Equal(11, sum.Value);
        Assert.Equal(2, evaluations);
    }

    private sealed class RecoverableComparer : IEqualityComparer<int>
    {
        internal bool Throw { get; set; }

        public bool Equals(int first, int second) => Throw ? throw new ArithmeticException() : first == second;

        public int GetHashCode(int value) => value;
    }
}
