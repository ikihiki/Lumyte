namespace Lumyte.Graphics.Tests;

public sealed class GpuBackendTestGateTests
{
    [Theory]
    [InlineData(null, typeof(ArgumentNullException))]
    [InlineData("", typeof(ArgumentException))]
    [InlineData(" ", typeof(ArgumentException))]
    public void GateRejectsMissingMutexNames(string? mutexName, Type exceptionType)
    {
        var error = (ArgumentException)Assert.Throws(exceptionType, () =>
        {
            using var gate = new GpuBackendTestGate(mutexName!);
        });

        Assert.Equal("mutexName", error.ParamName);
    }

    [Theory]
    [InlineData(null, typeof(ArgumentNullException))]
    [InlineData("", typeof(ArgumentException))]
    [InlineData(" ", typeof(ArgumentException))]
    public void CapabilityProbeRejectsMissingMutexNames(string? mutexName, Type exceptionType)
    {
        var error = (ArgumentException)Assert.Throws(exceptionType,
            () => GpuBackendTestGate.CreateProbe(static () => new object(), mutexName!));

        Assert.Equal("mutexName", error.ParamName);
    }

    [Fact]
    public void GateCanBeDisposedFromAnotherThread()
    {
        using var gate = new GpuBackendTestGate(UniqueMutexName());
        Exception? failure = null;
        Thread disposer = new(() => failure = Record.Exception(gate.Dispose));

        disposer.Start();
        disposer.Join();

        Assert.Null(failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentlyOpenedMutexObservesGateOwnership(bool releaseGate)
    {
        string name = UniqueMutexName();
        using var gate = new GpuBackendTestGate(name);
        using Mutex contender = new(false, name);
        bool acquired = false;
        Thread worker = new(() =>
        {
            acquired = contender.WaitOne(0);
            if (acquired) { contender.ReleaseMutex(); }
        });

        if (releaseGate) { gate.Dispose(); }
        worker.Start();
        worker.Join();

        Assert.Equal(releaseGate, acquired);
    }

    [Fact]
    public void DifferentBackendGatesCanBeHeldAtTheSameTime()
    {
        string firstName = UniqueMutexName();
        string secondName = UniqueMutexName();
        using Mutex firstContender = new(false, firstName);
        using Mutex secondContender = new(false, secondName);

        using var first = new GpuBackendTestGate(firstName);
        using var second = new GpuBackendTestGate(secondName);

        Assert.Equal((false, false), (TryAcquire(firstContender), TryAcquire(secondContender)));
    }

    [Fact]
    public void DisposingOneBackendGateLeavesTheOtherOwned()
    {
        string firstName = UniqueMutexName();
        string secondName = UniqueMutexName();
        using var first = new GpuBackendTestGate(firstName);
        using var second = new GpuBackendTestGate(secondName);
        using Mutex firstContender = new(false, firstName);
        using Mutex secondContender = new(false, secondName);

        first.Dispose();

        Assert.Equal((true, false), (TryAcquire(firstContender), TryAcquire(secondContender)));
    }

    [Fact]
    public void GateAcquiresAnAbandonedMutex()
    {
        string name = UniqueMutexName();
        using Mutex abandoned = new(false, name);
        Thread formerOwner = new(() => abandoned.WaitOne());
        formerOwner.Start();
        formerOwner.Join();

        using var gate = new GpuBackendTestGate(name);
        Exception? failure = Record.Exception(gate.Dispose);

        Assert.Null(failure);
    }

    [Fact]
    public void CapabilityProbeHoldsGateDuringEvaluation()
    {
        string name = UniqueMutexName();
        using Mutex contender = new(false, name);
        Lazy<bool> probe = GpuBackendTestGate.CreateProbe(() => TryAcquire(contender), name);

        bool acquiredDuringProbe = probe.Value;
        bool acquiredAfterProbe = TryAcquire(contender);

        Assert.Equal((false, true), (acquiredDuringProbe, acquiredAfterProbe));
    }

    [Fact]
    public async Task ConcurrentCapabilityRequestsShareOneProbeResult()
    {
        int evaluations = 0;
        object expected = new();
        Lazy<object> probe = GpuBackendTestGate.CreateProbe(() =>
        {
            Interlocked.Increment(ref evaluations);
            return expected;
        }, UniqueMutexName());

        object[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => probe.Value)));

        Assert.Equal(1, evaluations);
        Assert.All(results, result => Assert.Same(expected, result));
    }

    [Fact]
    public void FailedCapabilityProbeReleasesGate()
    {
        string name = UniqueMutexName();
        using Mutex contender = new(false, name);
        InvalidOperationException expected = new("Probe failed.");
        Lazy<int> probe = GpuBackendTestGate.CreateProbe<int>(() => throw expected, name);

        Exception actual = Assert.Throws<InvalidOperationException>(() => _ = probe.Value);

        Assert.Same(expected, actual);
        Assert.True(TryAcquire(contender));
    }

    private static bool TryAcquire(Mutex mutex)
    {
        if (!mutex.WaitOne(0)) { return false; }
        mutex.ReleaseMutex();
        return true;
    }

    private static string UniqueMutexName() => $"Lumyte.Graphics.Tests.GpuBackend.Regression.{Guid.NewGuid():N}";
}
