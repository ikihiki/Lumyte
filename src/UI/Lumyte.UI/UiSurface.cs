using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed class UiSurface : IDisposable
{
    private readonly Widget _root;
    private UiSurfacePhase _phase;
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

        List<Widget> nodes = root.CollectSubtree();
        _root = root;
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
            ThrowIfDisposed();
            return _root;
        }
    }

    internal UiLayoutPass? LayoutPass => _layoutPass;

    /// <summary>Sets the finite nonnegative logical viewport and invalidates layout when it changes.</summary>
    /// <param name="logicalSize">The size passed to the root as tight constraints.</param>
    public void SetViewport(Vector2 logicalSize)
    {
        ThrowIfDisposed();
        VerifyMutationAllowed();
        UiLayoutMath.ValidateSize(logicalSize, nameof(logicalSize));
        if (_viewport != logicalSize)
        {
            _viewport = logicalSize;
            _root.MarkLayoutDirty();
        }
    }

    /// <summary>Commits a dirty tree's geometry only after the complete layout pass succeeds.</summary>
    public void UpdateLayout()
    {
        ThrowIfDisposed();
        if (_phase != UiSurfacePhase.Idle)
        {
            throw new InvalidOperationException("Layout can only start while the surface is idle.");
        }

        VerifyMutationAllowed();
        if (!_root.IsLayoutDirty)
        {
            return;
        }

        var pass = new UiLayoutPass();
        _phase = UiSurfacePhase.Layout;
        _layoutPass = pass;
        try
        {
            pass.Compute(_root, UiBoxConstraints.Tight(_viewport));
            pass.Publish();
        }
        finally
        {
            _layoutPass = null;
            _phase = UiSurfacePhase.Idle;
        }
    }

    /// <summary>Updates layout and records every visible widget into an active borrowed canvas.</summary>
    /// <param name="canvas">A begun canvas whose transform and clip are restored, including on callback failure.</param>
    /// <remarks>Does not clear or submit the target. After a failure, cancel the caller's entire recording.</remarks>
    public void Record(DrawingCanvas2D canvas)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(canvas);
        if (_phase != UiSurfacePhase.Idle)
        {
            throw new InvalidOperationException("Recording can only start while the surface is idle.");
        }

        // Save also validates Begin before layout can invoke user code.
        canvas.Save();
        try
        {
            UpdateLayout();
            _phase = UiSurfacePhase.Record;
            if (_viewport.X > 0 && _viewport.Y > 0)
            {
                canvas.ClipRect(new(0, 0, _viewport.X, _viewport.Y));
                RecordWidget(_root, canvas);
            }
        }
        finally
        {
            _phase = UiSurfacePhase.Idle;
            canvas.Restore();
        }
    }

    /// <summary>Disconnects the complete tree, preserving its structure and external resources.</summary>
    public void Dispose()
    {
        if (_phase == UiSurfacePhase.Disposed)
        {
            return;
        }

        if (_phase != UiSurfacePhase.Idle)
        {
            throw new InvalidOperationException("A surface can only be disposed while idle.");
        }

        VerifyMutationAllowed();
        List<Widget> nodes = _root.CollectSubtree();
        foreach (Widget node in nodes)
        {
            node.SetSurface(null);
        }

        _phase = UiSurfacePhase.Disposed;
    }

    internal void VerifyMutationAllowed()
    {
        ThrowIfDisposed();
        if (_phase is not (UiSurfacePhase.Idle or UiSurfacePhase.Dispatch))
        {
            throw new InvalidOperationException("Widget changes are not allowed during layout or recording.");
        }
    }

    private static void RecordWidget(Widget widget, DrawingCanvas2D canvas)
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

            RecordChildren(widget.BackgroundCollection);

            // Custom drawing state must not leak into this widget's contents.
            canvas.Save();
            try
            {
                widget.InvokeRecord(canvas);
            }
            finally
            {
                canvas.Restore();
            }

            RecordChildren(widget.ContentCollection);
        }
        finally
        {
            canvas.Restore();
        }

        void RecordChildren(WidgetChildren collection)
        {
            if (collection is not null)
            {
                foreach (Widget child in collection.Items)
                {
                    RecordWidget(child, canvas);
                }
            }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_phase == UiSurfacePhase.Disposed, this);
}
