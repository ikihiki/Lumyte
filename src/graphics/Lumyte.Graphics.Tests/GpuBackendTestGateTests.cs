namespace Lumyte.Graphics.Tests;

public sealed class GpuBackendTestGateTests
{
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

    private static string UniqueMutexName() => $"Lumyte.Graphics.Tests.GpuBackend.Regression.{Guid.NewGuid():N}";
}
