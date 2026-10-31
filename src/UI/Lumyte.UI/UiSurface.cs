using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface : IDisposable
{
    private Widget _root;
    private bool _isDisposed;

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
