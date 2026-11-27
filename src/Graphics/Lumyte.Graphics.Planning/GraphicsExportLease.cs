namespace Lumyte.Graphics.Planning;

/// <summary>Declares graphics export lease.</summary>
/// <typeparam name = "T">The typed shader or resource contract.</typeparam>
public sealed class GraphicsExportLease<T> : IDisposable
    where T : class
{
    private readonly PreparedGraphicsExecution _owner;
    private readonly T _value;
    private bool _disposed;

    internal GraphicsExportLease(PreparedGraphicsExecution owner, T value)
    {
        _owner = owner;
        _value = value;
        owner.RetainExport();
    }

    /// <summary>Gets value.</summary>
    public T Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_owner.ContentsReady)
            {
                throw new InvalidOperationException("Export contents require successful GPU completion and command retirement.");
            }

            return _value;
        }
    }

    /// <summary>Releases this export on its fixed native owner without blocking.</summary>
    /// <returns>The owner cleanup completion.</returns>
    public async ValueTask RetireAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _owner.ReleaseExportAsync().ConfigureAwait(false);
        _disposed = true;
    }

    /// <summary>Releases this export only from its fixed native owner.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _owner.ReleaseExport();
        _disposed = true;
    }
}
