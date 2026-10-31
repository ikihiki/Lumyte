using System.Runtime.ExceptionServices;

namespace Lumyte.Signals;

/// <summary>An owned synchronous observer with coalesced, revocable delivery.</summary>
public sealed class SignalEffect : IDisposable
{
    private readonly Observer _observer;
    private Action? _effect;
    private ISignalScheduler? _scheduler;
    private Action<Exception>? _onError;
    private bool _disposed;
    private bool _paused;
    private bool _queued;
    private ulong _generation;
    private Exception? _failure;
    private ulong _revision;
    private bool _pending;

    internal SignalEffect(Action effect, ISignalScheduler? scheduler, Action<Exception>? onError)
    {
        _effect = effect;
        _scheduler = scheduler;
        _onError = onError;
        _observer = new(this);
    }

    /// <summary>Gets a subsequent observer or delivery failure.</summary>
    public Exception? Failure
    {
        get
        {
            lock (SignalContext.Gate)
            {
                return _failure;
            }
        }

        private set => _failure = value;
    }

    /// <summary>Gets the globally comparable monotonic upstream mutation stamp, including while paused.</summary>
    public ulong Revision
    {
        get
        {
            lock (SignalContext.Gate)
            {
                return _revision;
            }
        }

        private set => _revision = value;
    }

    /// <summary>Gets a value indicating whether an upstream change remains unapplied.</summary>
    public bool IsPending
    {
        get
        {
            lock (SignalContext.Gate)
            {
                return _pending;
            }
        }

        private set => _pending = value;
    }

    /// <summary>Pauses delivery without stopping revision propagation.</summary>
    public void Pause()
    {
        lock (SignalContext.Gate)
        {
            SignalContext.CheckMutation();
            ObjectDisposedException.ThrowIf(_disposed, this);
            _paused = true;
            _queued = false;
            _generation++;
        }
    }

    /// <summary>Resumes a healthy observer and delivers unapplied changes automatically.</summary>
    public void Resume()
    {
        lock (SignalContext.Gate)
        {
            SignalContext.CheckMutation();
            ObjectDisposedException.ThrowIf(_disposed, this);
            _paused = false;
            Queue();
        }

        SignalContext.Current.Flush();
    }

    /// <summary>Revokes queued work and releases callback and dependency references.</summary>
    public void Dispose()
    {
        lock (SignalContext.Gate)
        {
            SignalContext.CheckMutation();
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _generation++;
            _queued = false;
            IsPending = false;
            _observer.Clear();
            _effect = null;
            _onError = null;
            _scheduler = null;
        }
    }

    internal void Initialize()
    {
        Dictionary<SignalNode, ulong> dependencies = [];
        SignalContext.Evaluate(
            () =>
            {
                _effect!();
                return true;
            },
            out dependencies);
        _observer.Replace(dependencies, true);
        Revision = Math.Max(Revision, dependencies.Count == 0 ? 0 : dependencies.Keys.Max(node => node.Revision));
    }

    internal void Deliver(ulong expectedGeneration)
    {
        ulong generation;
        ISignalScheduler? scheduler;
        lock (SignalContext.Gate)
        {
            if (!_queued || _paused || _disposed || Failure is not null || expectedGeneration != _generation)
            {
                return;
            }

            generation = _generation;
            scheduler = _scheduler;
        }

        if (scheduler is null)
        {
            Run(generation, true);
            return;
        }

        try
        {
            scheduler.Schedule(() => Run(generation, false));
        }
        catch (Exception error)
        {
            lock (SignalContext.Gate)
            {
                if (!_disposed && generation == _generation)
                {
                    Fail(error);
                }
            }
        }
    }

    private void Queue()
    {
        if (IsPending && !_queued && !_paused && !_disposed && Failure is null)
        {
            _queued = true;
            SignalContext.Current.Pending.Enqueue((this, _generation));
        }
    }

    private void Run(ulong generation, bool propagate)
    {
        Exception? failure = null;
        lock (SignalContext.Gate)
        {
            if (_disposed || _paused || generation != _generation || Failure is not null || !IsPending)
            {
                return;
            }

            _queued = false;
            IsPending = false;
            try
            {
                Initialize();
            }
            catch (Exception error)
            {
                failure = Fail(error);
            }
        }

        if (propagate && failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private Exception? Fail(Exception error)
    {
        Failure = error;
        _scheduler = null;
        _generation++;
        _queued = false;
        IsPending = false;
        _observer.Clear();
        _effect = null;
        if (_onError is null)
        {
            return error;
        }

        Action<Exception> notify = _onError;
        _onError = null;
        try
        {
            SignalContext context = SignalContext.Current;
            context.Evaluating++;
            try
            {
                notify(error);
            }
            finally
            {
                context.Evaluating--;
            }

            return null;
        }
        catch (Exception notification)
        {
            Failure = new AggregateException(error, notification);
            return Failure;
        }
    }

    private sealed class Observer(SignalEffect effect) : SignalObserver
    {
        internal override void Invalidate(ulong revision, HashSet<SignalObserver> visited)
        {
            effect.Revision = revision;
            effect.IsPending = true;
            effect.Queue();
        }
    }
}
