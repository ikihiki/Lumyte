namespace Lumyte.Pipelines;

/// <summary>Owns a prepared output and the lower-level owners required to use it.</summary>
/// <typeparam name="T">The output type.</typeparam>
public sealed class PipelineResult<T> : IDisposable
{
    private T _value;
    private Func<T>? _valueAccessor;
    private bool _closing;
    private IDisposable? _owner;

    internal PipelineResult(T value, IDisposable owner, object? platformState = null, Func<T>? valueAccessor = null)
    {
        _value = value;
        _owner = owner;
        PlatformState = platformState;
        _valueAccessor = valueAccessor;
    }

    /// <summary>Gets the prepared output while its ownership remains active.</summary>
    public T Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(_owner is null || _closing, this);
            return _valueAccessor is null ? _value : _valueAccessor();
        }
    }

    internal object? PlatformState { get; private set; }

    internal bool IsDisposed => _owner is null || _closing;

    /// <summary>Returns ownership; a failed cleanup remains retryable.</summary>
    public void Dispose()
    {
        if (_owner is not null)
        {
            if (_owner is IPipelineResultOwner validation)
            {
                validation.ValidateReturn();
            }

            _closing = true;
            _owner.Dispose();
            _owner = null;
            _value = default!;
            _valueAccessor = null;
            PlatformState = null;
        }
    }
}
