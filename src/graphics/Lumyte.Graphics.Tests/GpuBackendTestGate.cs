namespace Lumyte.Graphics.Tests;

public class GpuBackendTestGate : IDisposable
{
    private readonly ManualResetEventSlim release = new();
    private readonly Task lifetime;
    private int disposed;

    public static Lazy<T> CreateProbe<T>(Func<T> probe, string mutexName)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        // Discovery runs before collection fixtures exist. Cache only the capability
        // result, and use the same backend-specific exclusion as execution while probing.
        return new Lazy<T>(() =>
        {
            using var gate = new GpuBackendTestGate(mutexName);
            return probe();
        });
    }

    public GpuBackendTestGate(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        TaskCompletionSource acquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // A collection fixture can be disposed on a different thread after an async test.
        // Keep native mutex ownership on one dedicated thread for the entire lease.
        lifetime = Task.Factory.StartNew(() =>
        {
            try
            {
                using Mutex mutex = new(false, mutexName);
                try { mutex.WaitOne(); }
                catch (AbandonedMutexException) { }
                try
                {
                    acquired.SetResult();
                    release.Wait();
                }
                finally { mutex.ReleaseMutex(); }
            }
            catch (Exception error)
            {
                acquired.TrySetException(error);
                throw;
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        try { acquired.Task.GetAwaiter().GetResult(); }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) { return; }
        release.Set();
        try { lifetime.GetAwaiter().GetResult(); }
        finally { release.Dispose(); }
    }
}
