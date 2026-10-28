using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree to the creating thread without owning external resources.</summary>
public sealed class UiSurface : IDisposable
{
    private readonly Thread _ownerThread = Thread.CurrentThread;
    private readonly Widget _root;
    private UiSurfacePhase _phase;

    /// <summary>Initializes a new instance of the <see cref="UiSurface"/> class.</summary>
    /// <param name="root">A parentless widget tree that is not connected to another surface.</param>
    public UiSurface(Widget root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.Owner is not null || root.Surface is not null)
        {
            throw new ArgumentException("The root must have neither a parent nor a surface.", nameof(root));
        }

        using Widget.MutationScope mutation = root.BeginMutation();
        List<Widget> nodes = root.CollectSubtree();
        _root = root;
        foreach (Widget node in nodes)
        {
            node.SetSurface(this);
        }
    }

    /// <summary>Gets the connected root on the owner thread while the surface is alive.</summary>
    public Widget Root
    {
        get
        {
            VerifyAccess();
            return _root;
        }
    }

    /// <summary>Disconnects the complete tree, preserving its structure and external resources.</summary>
    public void Dispose()
    {
        VerifyOwner();
        if (_phase == UiSurfacePhase.Disposed)
        {
            return;
        }

        if (_phase != UiSurfacePhase.Idle)
        {
            throw new InvalidOperationException("A surface can only be disposed while idle.");
        }

        using Widget.MutationScope mutation = _root.BeginMutation();
        List<Widget> nodes = _root.CollectSubtree();
        foreach (Widget node in nodes)
        {
            node.SetSurface(null);
        }

        _phase = UiSurfacePhase.Disposed;
    }

    internal void VerifyAccess()
    {
        VerifyOwner();
        ObjectDisposedException.ThrowIf(_phase == UiSurfacePhase.Disposed, this);
    }

    internal void VerifyMutationAllowed()
    {
        VerifyAccess();
        if (_phase is not (UiSurfacePhase.Idle or UiSurfacePhase.Dispatch))
        {
            throw new InvalidOperationException("Widget changes are not allowed during layout or recording.");
        }
    }

    private void VerifyOwner()
    {
        if (!ReferenceEquals(Thread.CurrentThread, _ownerThread))
        {
            throw new InvalidOperationException("The surface and its connected widgets belong to another thread.");
        }
    }
}
