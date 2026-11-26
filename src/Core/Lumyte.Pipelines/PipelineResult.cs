namespace Lumyte.Pipelines;

/// <summary>Owns a prepared output and the lower-level owners required to use it.</summary>
/// <typeparam name="T">The output type.</typeparam>
public sealed class PipelineResult<T> : IDisposable
{
    private T _value;
    private bool _closing;
    private IDisposable? _owner;

    internal PipelineResult(T value, IDisposable owner)
    {
        _value = value;
        _owner = owner;
    }

    /// <summary>Gets the prepared output while its ownership remains active.</summary>
    public T Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(_owner is null || _closing, this);
            return _value;
        }
    }

    /// <summary>Returns ownership; a failed cleanup remains retryable.</summary>
    public void Dispose()
    {
        if (_owner is not null)
        {
            _closing = true;
            _owner.Dispose();
            _owner = null;
            _value = default!;
        }
    }
}
