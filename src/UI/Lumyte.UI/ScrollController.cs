namespace Lumyte.UI;

/// <summary>A borrowed, single-viewport scroll position. Operate sequentially on its binding thread.</summary>
public sealed class ScrollController : IDisposable
{
    private ScrollViewElement? _owner;
    private ScrollViewElement? _reservation;
    private int _thread;
    private bool _disposed;

    /// <summary>Gets the accepted logical offset.</summary>
    public float Offset { get; private set; }

    /// <summary>Gets the content extent.</summary>
    public float Extent { get; private set; }

    /// <summary>Gets the viewport extent.</summary>
    public float Viewport { get; private set; }

    /// <summary>Gets the maximum offset.</summary>
    public float Maximum => Math.Max(0, Extent - Viewport);

    /// <summary>Gets a value indicating whether this controller has been disposed.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Gets a value indicating whether a viewport is attached.</summary>
    public bool IsAttached => _owner is not null;

    /// <summary>Gets or sets the synchronous, latest position-change callback.</summary>
    public Action<float>? OnChanged { get; set; }

    /// <summary>Sets a finite offset, clamped to the current range; does not mutate declarations.</summary>
    /// <param name="offset">The finite requested offset.</param>
    public void ScrollTo(float offset)
    {
        ValidateAlive();
        if (!float.IsFinite(offset))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        SetOffset(Math.Clamp(offset, 0, Maximum));
    }

    /// <summary>Queues a finite offset change through an explicitly supplied owner dispatcher.</summary>
    /// <param name="dispatcher">The borrowed viewport owner dispatcher.</param>
    /// <param name="offset">The finite logical offset.</param>
    /// <param name="cancellationToken">Cancels before owner execution.</param>
    /// <returns>The owner operation completion.</returns>
    public ValueTask ScrollToAsync(IUiDispatcher dispatcher, float offset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (!float.IsFinite(offset))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return dispatcher.InvokeAsync(() => ScrollTo(offset), cancellationToken);
    }

    /// <summary>Detaches the borrowed controller without disposing its surface.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CheckThread();
        _disposed = true;
        _reservation = null;
        ScrollViewElement? owner = _owner;
        _owner = null;
        owner?.ControllerDisposed();
        OnChanged = null;
    }

    internal void ValidateAlive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CheckThread();
    }

    internal void ValidateBinding(ScrollViewElement owner)
    {
        ValidateAlive();
        if ((_owner is not null && !ReferenceEquals(_owner, owner))
            || (_reservation is not null && !ReferenceEquals(_reservation, owner)))
        {
            throw new InvalidOperationException("The borrowed controller already has a viewport.");
        }
    }

    internal void Reserve(ScrollViewElement owner)
    {
        ValidateBinding(owner);
        _reservation = owner;
    }

    internal void ReleaseReservation(ScrollViewElement owner)
    {
        if (ReferenceEquals(_reservation, owner))
        {
            _reservation = null;
        }
    }

    internal void Attach(ScrollViewElement owner)
    {
        if (_disposed)
        {
            return;
        }

        ReleaseReservation(owner);
        _thread = Environment.CurrentManagedThreadId;
        _owner = owner;
    }

    internal void Detach(ScrollViewElement owner)
    {
        if (ReferenceEquals(_owner, owner))
        {
            _owner = null;
            _thread = 0;
        }
    }

    internal void ResetGeometry()
    {
        Extent = 0;
        Viewport = 0;
        Offset = 0;
    }

    internal void Geometry(float extent, float viewport)
    {
        Extent = extent;
        Viewport = viewport;
        Offset = Math.Clamp(Offset, 0, Maximum);
    }

    internal float ApplyDelta(float delta)
    {
        ValidateAlive();
        float before = Offset;
        float accepted = (float)Math.Clamp((double)before + delta, 0, Maximum);
        float consumed = accepted - before;
        SetOffset(accepted);
        return delta >= 0 ? Math.Clamp(delta - consumed, 0, delta) : Math.Clamp(delta - consumed, delta, 0);
    }

    private void SetOffset(float value)
    {
        if (value == Offset)
        {
            return;
        }

        Offset = value;
        _owner?.OffsetChanged();
        Action<float>? callback = OnChanged;
        callback?.Invoke(value);
    }

    private void CheckThread()
    {
        if (_thread != 0 && _thread != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Use the controller on its viewport owner thread.");
        }
    }
}
