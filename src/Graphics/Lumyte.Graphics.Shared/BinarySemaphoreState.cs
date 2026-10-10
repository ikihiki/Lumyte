namespace Lumyte.Graphics.Shared;

/// <summary>Validates issued binary signals and waits without waiting, locking or adding GPU operations.</summary>
public sealed class BinarySemaphoreState
{
    private readonly List<Func<bool>> _uses = [];
    private bool _signaled;
    private bool _disposed;

    /// <summary>Rejects signaling a disposed semaphore or an unconsumed signal.</summary>
    public void ValidateSignal()
    {
        ValidateAlive();
        if (_signaled)
        {
            throw new InvalidOperationException("Consume the binary semaphore's previous signal before signaling again.");
        }
    }

    /// <summary>Rejects waiting on a disposed semaphore or a signal that has not been issued.</summary>
    public void ValidateWait()
    {
        ValidateAlive();
        if (!_signaled)
        {
            throw new InvalidOperationException("Issue a signal before its consuming binary semaphore wait.");
        }
    }

    /// <summary>Records a successful signal without waiting for its completion.</summary>
    /// <param name="isComplete">Queries when this issued native use has ended.</param>
    public void MarkSignal(Func<bool> isComplete)
    {
        _signaled = true;
        _uses.RemoveAll(completed => completed());
        _uses.Add(isComplete);
    }

    /// <summary>Records a successfully issued consuming wait.</summary>
    /// <param name="isComplete">Queries when this issued native use has ended.</param>
    public void MarkWait(Func<bool> isComplete)
    {
        _signaled = false;
        _uses.RemoveAll(completed => completed());
        _uses.Add(isComplete);
    }

    /// <summary>Rejects new operations after disposal.</summary>
    public void ValidateAlive() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>Rejects disposal while acquisition, submission or presentation still uses the native semaphore.</summary>
    public void ValidateDispose()
    {
        _uses.RemoveAll(isComplete => isComplete());
        if (_uses.Count != 0)
        {
            throw new InvalidOperationException("Complete every issued semaphore use before disposal.");
        }
    }

    /// <summary>Records native disposal after all issued uses have completed.</summary>
    public void MarkDisposed() => _disposed = true;
}
