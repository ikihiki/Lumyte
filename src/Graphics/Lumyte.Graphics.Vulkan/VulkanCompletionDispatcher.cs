using System.Collections.Concurrent;

namespace Lumyte.Graphics.Vulkan;

// One sleeping native waiter per device, not a timer or a blocked thread-pool
// worker per submission. Register every submission in native queue order.
internal sealed class VulkanCompletionDispatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly BlockingCollection<(Func<Action> Wait, Func<bool> Ready, bool CheckRetirement)> _work = new();
    private readonly List<Func<bool>> _pending = [];
    private Thread? _thread;
    private bool _disposed;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_pending.Any(ready => !ready()))
            {
                throw new InvalidOperationException("Complete native submission waits before disposing the device.");
            }

            _disposed = true;
            _work.CompleteAdding();
        }

        _thread?.Join();
        _work.Dispose();
    }

    internal void Enqueue(Func<Action> wait, Func<bool>? ready = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            bool checkRetirement = ready is not null;
            ready ??= static () => false;
            _pending.Add(ready);
            _work.Add((wait, ready, checkRetirement));
            if (_thread is null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "Lumyte Vulkan completion" };
                _thread.Start();
            }
        }
    }

    private void Run()
    {
        foreach ((Func<Action> wait, Func<bool> ready, bool checkRetirement) in _work.GetConsumingEnumerable())
        {
            Action publish = wait();
            lock (_gate)
            {
                // A failed native wait (for example OOM) is not evidence that
                // GPU use ended. Keep its readiness guard until explicit cleanup.
                if (!checkRetirement || ready())
                {
                    _pending.Remove(ready);
                }
            }

            publish();
        }
    }
}
