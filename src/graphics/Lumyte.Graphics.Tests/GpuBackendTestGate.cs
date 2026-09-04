namespace Lumyte.Graphics.Tests;

public sealed class GpuBackendTestGate : IDisposable
{
    private const string MutexName = "Lumyte.Graphics.Tests.GpuBackend";
    private readonly Mutex mutex = new(false, MutexName);
    private bool acquired;

    public GpuBackendTestGate()
    {
        try
        {
            acquired = mutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }
    }

    public void Dispose()
    {
        if (acquired)
        {
            mutex.ReleaseMutex();
            acquired = false;
        }
        mutex.Dispose();
    }
}
