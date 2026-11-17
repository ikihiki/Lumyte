using System.Numerics;
using Lumyte.UI.Collections;
using Lumyte.UI.Windowing;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls.Windowing;

/// <summary>Adapts collection popup requests to the existing popup manager.</summary>
public sealed class CollectionPopupHost : ICollectionPopupHost
{
    private readonly UiPopupManager _manager;
    private readonly Func<UiPopupOwner> _owner;
    private readonly UiWindowPolicy _policy;

    /// <summary>Initializes a new instance of the <see cref="CollectionPopupHost"/> class.</summary>
    /// <param name="manager">The borrowed manager.</param>
    /// <param name="owner">The borrowed owner registration.</param>
    /// <param name="policy">The explicit presentation policy.</param>
    public CollectionPopupHost(UiPopupManager manager, UiPopupOwner owner, UiWindowPolicy policy = UiWindowPolicy.HostedOnly)
        : this(manager, () => owner, policy)
    {
        ArgumentNullException.ThrowIfNull(owner);
    }

    /// <summary>Initializes a new instance of the <see cref="CollectionPopupHost"/> class.</summary>
    /// <param name="manager">The borrowed manager.</param>
    /// <param name="owner">Resolves the application-owned registration when opening, after initial view creation.</param>
    /// <param name="policy">The explicit presentation policy.</param>
    public CollectionPopupHost(UiPopupManager manager, Func<UiPopupOwner> owner, UiWindowPolicy policy = UiWindowPolicy.HostedOnly)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(owner);
        _manager = manager;
        _owner = owner;
        _policy = policy;
    }

    /// <inheritdoc/>
    public IDisposable Open(UiElement anchor, Vector2 size, Func<Widget> createContent, Action closed)
    {
        UiPopupHandle handle = _manager.RequestOpen(_owner(), new UiPopupRequest(anchor, size, createContent, _policy, onClosed: _ => closed()));
        return new CloseRequest(handle);
    }

    private sealed class CloseRequest(UiPopupHandle handle) : IDisposable
    {
        public void Dispose() => handle.Close();
    }
}
