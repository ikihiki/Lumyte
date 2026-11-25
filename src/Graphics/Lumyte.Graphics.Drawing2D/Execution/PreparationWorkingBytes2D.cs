namespace Lumyte.Graphics.Drawing2D;

// Forks share this preparation-local indirection, not copies of its frame closure.
// Invalidating it releases captured owners while retaining an already-read scalar.
internal sealed class PreparationWorkingBytes2D(Func<long> source) : IDisposable
{
    private Func<long>? _source = source;
    private long _value;

    internal bool HasSource => _source is not null;

    internal long Value
    {
        get
        {
            if (_source is { } read)
            {
                long value = read();
                _value = value;
                _source = null;
            }

            return _value;
        }
    }

    public void Dispose() => _source = null;
}
