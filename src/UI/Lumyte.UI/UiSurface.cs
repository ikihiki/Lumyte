using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
/// <remarks>Run layout, recording and disposal sequentially; keep declarations unchanged during a pass.</remarks>
public sealed partial class UiSurface : IDisposable
{
    private Widget _root;
    private volatile bool _isDisposed;
    private Vector2 _viewport;
    private UiLayoutPass? _layoutPass;

    /// <summary>Initializes a new instance of the <see cref="UiSurface"/> class.</summary>
    /// <param name="root">A parentless widget tree that is not connected to another surface.</param>
    public UiSurface(Widget root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.Owner is not null || root.Surface is not null)
        {
            throw new ArgumentException("The root must have neither a parent nor a surface.", nameof(root));
        }

        ValidateRebuildKeys(root);
        List<Widget> nodes = root.CollectSubtree();
        _root = root;
        _signals = PrepareSignals(root);
        _signalConnections++;
        foreach (Widget node in nodes)
        {
            node.SetSurface(this);
        }

        root.MarkLayoutDirty();
    }

    /// <summary>Gets the connected root while the surface is alive.</summary>
    public Widget Root
    {
        get
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            return _root;
        }
    }

    internal UiLayoutPass? LayoutPass => _layoutPass;

    /// <summary>Sets the finite nonnegative logical viewport and invalidates layout when it changes.</summary>
    /// <param name="logicalSize">The size passed to the root as tight constraints.</param>
    public void SetViewport(Vector2 logicalSize)
    {
        ThrowIfDisposed();
        UiLayoutMath.ValidateSize(logicalSize, nameof(logicalSize));
        if (_viewport != logicalSize)
        {
            _viewport = logicalSize;
            _root.MarkLayoutDirty();
        }
    }

    /// <summary>Updates dirty geometry in place; exceptions abort the update without rolling back earlier nodes.</summary>
    public void UpdateLayout()
    {
        UpdateTree();
        if (_isDisposed)
        {
            return;
        }

        if (!_root.IsLayoutDirty)
        {
            UpdateInput();
            return;
        }

        UiSignalRevision signalRevision = SignalRevision;
        var pass = new UiLayoutPass();
        _layoutPass = pass;
        try
        {
            pass.Compute(_root, UiBoxConstraints.Tight(_viewport));
            if (SignalRevision != signalRevision)
            {
                throw new UiStateUpdateException(new InvalidOperationException("Signal values changed during layout."));
            }

            InvalidateInput();
            UpdateInput();
        }
        catch
        {
            _root.MarkLayoutDirty();
            throw;
        }
        finally
        {
            _layoutPass = null;
        }
    }

    /// <summary>Updates layout and records every visible widget into an active borrowed canvas.</summary>
    /// <param name="canvas">A begun canvas whose transform and clip are restored, including on callback failure.</param>
    /// <remarks>Does not clear or submit the target. After a failure, cancel the caller's entire recording.</remarks>
    public void Record(DrawingCanvas2D canvas)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(canvas);

        // Save also validates Begin before layout can invoke user code.
        canvas.Save();
        try
        {
            UpdateLayout();
            if (!_isDisposed && _viewport.X > 0 && _viewport.Y > 0)
            {
                canvas.ClipRect(new(0, 0, _viewport.X, _viewport.Y));
                UiSignalRevision revision = SignalRevision;
                _recordDepth++;
                try
                {
                    RecordWidget(_root, canvas);
                }
                finally
                {
                    _recordDepth--;
                }

                if (SignalRevision != revision)
                {
                    throw new UiStateUpdateException(new InvalidOperationException("Signal values changed during recording; discard the frame."));
                }
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    /// <summary>Disconnects the complete tree, preserving its structure and external resources.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        List<Widget> nodes = _root.CollectSubtree();
        _isDisposed = true;
        ReleaseSignals();
        try
        {
            CancelAllInput();
        }
        finally
        {
            foreach (Widget node in nodes)
            {
                node.SetSurface(null);
            }
        }
    }

    internal static void RecordWidget(Widget widget, DrawingCanvas2D canvas)
    {
        if (!widget.IsVisible)
        {
            return;
        }

        canvas.Save();
        try
        {
            canvas.Translate(new(widget.Bounds.X, widget.Bounds.Y));
            if (widget.ClipToBounds)
            {
                canvas.ClipRect(new(0, 0, widget.Size.X, widget.Size.Y));
            }

            widget.InvokeRecord(canvas);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ThrowSignalFailure();
    }
}
