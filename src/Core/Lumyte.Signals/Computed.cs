using System.Runtime.ExceptionServices;

namespace Lumyte.Signals;

/// <summary>A lazy derived value that releases active upstream subscriptions when unobserved.</summary>
/// <typeparam name="T">The derived value type.</typeparam>
public sealed class Computed<T> : IReadSignal<T>
{
    private readonly Source _source;

    internal Computed(Func<T> compute, IEqualityComparer<T> comparer)
    {
        _source = new(compute, comparer);
    }

    /// <inheritdoc/>
    public T Value
    {
        get
        {
            lock (SignalContext.Gate)
            {
                try
                {
                    return _source.Read();
                }
                finally
                {
                    SignalContext.Track(_source);
                }
            }
        }
    }

    /// <inheritdoc/>
    public T Peek()
    {
        lock (SignalContext.Gate)
        {
            return _source.Read();
        }
    }

    private sealed class Source(Func<T> compute, IEqualityComparer<T> comparer) : SignalNode
    {
        private readonly Observer _observer = new();
        private bool _initialized;
        private bool _evaluating;
        private bool _dirty = true;
        private T _value = default!;
        private Exception? _failure;

        internal T Read()
        {
            if (_evaluating)
            {
                throw new InvalidOperationException("Computed dependency cycle.");
            }

            _evaluating = true;
            try
            {
                if (!Observed && _initialized && !_dirty)
                {
                    foreach ((SignalNode node, ulong revision) in _observer.Dependencies)
                    {
                        Signal.Untracked(() =>
                        {
                            node.ReadTracked();
                            return true;
                        });
                        if (revision != node.Revision)
                        {
                            _dirty = true;
                            break;
                        }
                    }
                }

                if (_dirty || !_initialized)
                {
                    Dictionary<SignalNode, ulong> dependencies = [];
                    try
                    {
                        T next = SignalContext.Evaluate(compute, out dependencies);
                        if (!_initialized || !SignalContext.Evaluate(() => comparer.Equals(_value, next), out _))
                        {
                            _value = next;
                        }

                        _observer.Replace(dependencies, Observed);
                        Revision = Math.Max(Revision, dependencies.Count == 0 ? 0 : dependencies.Keys.Max(node => node.Revision));
                        _initialized = true;
                        _failure = null;
                        _dirty = false;
                    }
                    catch (Exception error)
                    {
                        foreach ((SignalNode node, ulong revision) in _observer.Dependencies)
                        {
                            dependencies.TryAdd(node, revision);
                        }

                        _observer.Replace(dependencies, Observed);
                        _failure = error;
                        _dirty = true;
                        throw;
                    }
                }

                if (_failure is not null)
                {
                    ExceptionDispatchInfo.Capture(_failure).Throw();
                }

                return _value;
            }
            finally
            {
                _evaluating = false;
            }
        }

        internal override void ReadTracked()
        {
            try
            {
                _ = Read();
            }
            finally
            {
                SignalContext.Track(this);
            }
        }

        protected override void Activate()
        {
            _observer.Node = this;
            _observer.Subscribe();
        }

        protected override void Deactivate() => _observer.Unsubscribe();

        private sealed class Observer : SignalObserver
        {
            internal Source? Node { get; set; }

            internal override void Invalidate(ulong revision, HashSet<SignalObserver> visited)
            {
                Node!._dirty = true;
                Node.Changed(revision, visited);
            }
        }
    }
}
