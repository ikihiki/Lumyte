namespace Lumyte.UI;

/// <summary>A borrowed, single-viewport scroll position. Operate sequentially on its binding thread.</summary>
public sealed class ScrollController : IDisposable
{
    private ScrollViewElement? _owner;
    private ScrollViewElement? _reservation;
    private int _thread;
    private bool _disposed;
    private float? _requestedOffset;
    private (float Extent, float Viewport, float Offset)? _proposedGeometry;
    private bool _requestedNotification;
    private float? _stagedAnchorOffset;
    private long _positionRequestVersion;

    /// <summary>Gets the accepted logical offset.</summary>
    public float Offset { get; private set; }

    /// <summary>Gets the content extent.</summary>
    public float Extent { get; private set; }

    /// <summary>Gets the viewport extent.</summary>
    public float Viewport { get; private set; }

    /// <summary>Gets the maximum offset.</summary>
    public float Maximum => DefersPosition ? VirtualMaximum(Extent, Viewport) : Math.Max(0, Extent - Viewport);

    /// <summary>Gets a value indicating whether this controller has been disposed.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Gets a value indicating whether a viewport is attached.</summary>
    public bool IsAttached => _owner is not null;

    /// <summary>Gets or sets the synchronous, latest position-change callback.</summary>
    public Action<float>? OnChanged { get; set; }

    internal Func<float?>? ItemAnchorResolver { get; set; }

    internal float ProposedOffset => _proposedGeometry?.Offset ?? _requestedOffset ?? Offset;

    internal float ProposedViewport => _proposedGeometry?.Viewport ?? Viewport;

    internal long PositionRequestVersion => Volatile.Read(ref _positionRequestVersion);

    private bool DefersPosition => _owner?.Widget.DeferredPosition == true;

    /// <summary>Requests a finite offset clamped to the current range, without mutating declarations.</summary>
    /// <remarks>Ordinary viewports adopt immediately. Virtual item owners adopt position and notify only after viewport content preparation succeeds.</remarks>
    /// <param name="offset">The finite requested offset.</param>
    public void ScrollTo(float offset)
    {
        ValidateAlive();
        if (!float.IsFinite(offset))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        Interlocked.Increment(ref _positionRequestVersion);
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

    internal void RestoreItemAnchor(float offset)
    {
        if (_proposedGeometry is { } geometry)
        {
            _proposedGeometry = (geometry.Extent, geometry.Viewport, Math.Clamp(offset, 0, Math.Max(0, geometry.Extent - geometry.Viewport)));
            return;
        }

        float restored = Math.Clamp(offset, 0, Maximum);
        if (Offset != restored)
        {
            Offset = restored;
            _owner?.OffsetChanged();
        }
    }

    internal void Geometry(float extent, float viewport, bool prepare = false)
    {
        if (prepare || DefersPosition || _owner?.Surface?.IsPreparingViewportLayout == true)
        {
            _proposedGeometry = (extent, viewport, Math.Clamp(_stagedAnchorOffset ?? _requestedOffset ?? Offset, 0, DefersPosition ? VirtualMaximum(extent, viewport) : Math.Max(0, extent - viewport)));
            return;
        }

        _proposedGeometry = null;
        _requestedOffset = null;
        _requestedNotification = false;
        Extent = extent;
        Viewport = viewport;
        Offset = Math.Clamp(Offset, 0, Maximum);
    }

    internal float ApplyDelta(float delta)
    {
        ValidateAlive();
        if (delta != 0)
        {
            Interlocked.Increment(ref _positionRequestVersion);
        }

        float before = DefersPosition ? ProposedOffset : Offset;
        float accepted = (float)Math.Clamp((double)before + delta, 0, Maximum);
        float consumed = accepted - before;
        SetOffset(accepted);
        return delta >= 0 ? Math.Clamp(delta - consumed, 0, delta) : Math.Clamp(delta - consumed, delta, 0);
    }

    internal long ReservePositionRequest() => Interlocked.Increment(ref _positionRequestVersion);

    internal bool StagePositionRequest(float offset)
    {
        float maximum = _proposedGeometry is { } geometry ? VirtualMaximum(geometry.Extent, geometry.Viewport) : Maximum;
        float accepted = Math.Clamp(offset, 0, maximum);
        if (accepted == ProposedOffset)
        {
            return false;
        }

        _requestedOffset = accepted;
        _requestedNotification = true;
        if (_proposedGeometry is { } proposed)
        {
            _proposedGeometry = (proposed.Extent, proposed.Viewport, accepted);
        }

        return true;
    }

    internal void StageItemAnchor(float offset) => _stagedAnchorOffset = offset;

    internal void ClearStagedItemAnchor() => _stagedAnchorOffset = null;

    internal void DiscardProposedGeometry()
    {
        _proposedGeometry = null;
        _stagedAnchorOffset = null;
    }

    internal Action? CommitProposedGeometry()
    {
        if (_proposedGeometry is not { } geometry)
        {
            return null;
        }

        float previous = Offset;
        Extent = geometry.Extent;
        Viewport = geometry.Viewport;
        Offset = geometry.Offset;
        bool notify = _requestedNotification && !_stagedAnchorOffset.HasValue && previous != Offset;
        _requestedOffset = null;
        _proposedGeometry = null;
        _requestedNotification = false;
        ItemAnchorResolver = null;
        _stagedAnchorOffset = null;
        float accepted = Offset;
        return notify ? () => OnChanged?.Invoke(accepted) : null;
    }

    private static float VirtualMaximum(float extent, float viewport)
    {
        double available = Math.Max(0, (double)extent - viewport);
        float maximum = (float)available;
        if (viewport > 0 && extent > viewport && maximum >= extent)
        {
            return MathF.BitDecrement(extent);
        }

        return maximum > available ? MathF.BitDecrement(maximum) : maximum;
    }

    private void SetOffset(float value)
    {
        if (DefersPosition)
        {
            if (value != ProposedOffset)
            {
                _requestedOffset = value;
                _proposedGeometry = null;
                _requestedNotification = true;
                _owner?.OffsetChanged();
            }

            return;
        }

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
