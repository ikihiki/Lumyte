using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
/// <remarks>Run layout and disposal sequentially; keep declarations unchanged during a pass.</remarks>
public sealed partial class UiSurface : IDisposable
{
    private Widget _root;
    private bool _isDisposed;
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
            return;
        }

        var pass = new UiLayoutPass();
        _layoutPass = pass;
        try
        {
            pass.Compute(_root, UiBoxConstraints.Tight(_viewport));
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

    /// <summary>Disconnects the complete tree, preserving its structure and external resources.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        List<Widget> nodes = _root.CollectSubtree();
        foreach (Widget node in nodes)
        {
            node.SetSurface(null);
        }

        _isDisposed = true;
        _pendingRebuild = null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);
}
