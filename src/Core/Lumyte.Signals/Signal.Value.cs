namespace Lumyte.Signals;

/// <summary>An independent mutable value; shared graph operations are serialized internally.</summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class Signal<T> : IReadSignal<T>
{
    private readonly Source _source;

    internal Signal(T value, IEqualityComparer<T> comparer)
    {
        _source = new(value, comparer);
    }

    /// <summary>Gets or sets the tracked current value; equivalent assignments do not notify.</summary>
    public T Value
    {
        get
        {
            lock (SignalContext.Gate)
            {
                SignalContext.Track(_source);
                return _source.Value;
            }
        }

        set
        {
            if (SetValue(value))
            {
                SignalContext.Current.Flush();
            }
        }
    }

    /// <inheritdoc/>
    public T Peek()
    {
        lock (SignalContext.Gate)
        {
            return _source.Value;
        }
    }

    internal bool SetValue(T value)
    {
        lock (SignalContext.Gate)
        {
            SignalContext.CheckMutation();
            if (_source.EqualsValue(value))
            {
                return false;
            }

            ulong revision = SignalContext.NextRevision();
            _source.Value = value;
            _source.Changed(revision, []);
            return true;
        }
    }

    private sealed class Source(T value, IEqualityComparer<T> comparer) : SignalNode
    {
        internal T Value { get; set; } = value;

        internal bool EqualsValue(T next) => SignalContext.Evaluate(() => comparer.Equals(Value, next), out _);

        internal override void ReadTracked() => SignalContext.Track(this);
    }
}
