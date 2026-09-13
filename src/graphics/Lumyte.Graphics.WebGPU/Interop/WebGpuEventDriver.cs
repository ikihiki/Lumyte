using System.Collections.Concurrent;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

/// <summary>Drives native futures without blocking public async calls or occupying a worker per operation.</summary>
internal sealed class WebGpuEventDriver : IDisposable
{
    private readonly F.InstanceHandle instance;
    private readonly WebGpuDeviceStatus status;
    private readonly ConcurrentQueue<(N.Future Future, Task Completion)> incoming = new();
    private readonly AutoResetEvent changed = new(false);
    private readonly Thread thread;
    private volatile bool stopping;

    internal WebGpuEventDriver(F.InstanceHandle instance, WebGpuDeviceStatus status)
    {
        this.instance = instance;
        this.status = status;
        thread = new Thread(Run) { IsBackground = true, Name = "Lumyte WebGPU events" };
        try { thread.Start(); }
        catch { changed.Dispose(); throw; }
    }

    internal void Register(N.Future future, Task completion)
    {
        try
        {
            if (completion.IsCompleted) { return; }
            incoming.Enqueue((future, completion));
            changed.Set();
        }
        catch (Exception error)
        {
            status.Lose($"WebGPU event registration failed: {error.Message}");
            throw;
        }
    }

    private unsafe void Run()
    {
        var pending = new List<(N.Future Future, Task Completion)>();
        int next = 0;
        try
        {
            while (!stopping)
            {
                while (incoming.TryDequeue(out var item)) { pending.Add(item); }
                pending.RemoveAll(static item => item.Completion.IsCompleted);
                if (pending.Count == 0)
                {
                    changed.WaitOne();
                    continue;
                }
                next %= pending.Count;
                var info = new N.FutureWaitInfo { Future = pending[next++].Future };
                // One source per wait obeys the C API's mixed CPU/queue future restriction.
                // A bounded native wait permits new registrations and shutdown; idle devices do not poll.
                N.WaitStatus result = F.WebGPU_FFI.InstanceWaitAny(instance, 1, &info, 10_000_000);
                if (result is not (N.WaitStatus.Success or N.WaitStatus.TimedOut))
                {
                    status.Lose($"WebGPU event processing failed ({result}).");
                    return;
                }
            }
        }
        catch (Exception error) { status.Lose($"WebGPU event processing failed: {error.Message}"); }
    }

    public void Dispose()
    {
        stopping = true;
        changed.Set();
        thread.Join();
        changed.Dispose();
    }
}
