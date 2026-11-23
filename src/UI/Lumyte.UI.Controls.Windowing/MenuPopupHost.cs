using System.Numerics;
using Lumyte.UI.Windowing;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls.Windowing;

/// <summary>Adapts borrowed Controls popup requests to the existing popup manager.</summary>
public sealed class MenuPopupHost : IMenuPopupHost
{
    private readonly UiPopupManager _manager;
    private readonly Func<UiPopupOwner> _owner;
    private readonly UiWindowPolicy _policy;

    /// <summary>Initializes a new instance of the <see cref="MenuPopupHost"/> class.</summary>
    /// <param name="manager">The borrowed popup manager.</param>
    /// <param name="owner">The borrowed registered owner.</param>
    /// <param name="policy">Explicit existing presentation policy.</param>
    public MenuPopupHost(UiPopupManager manager, UiPopupOwner owner, UiWindowPolicy policy = UiWindowPolicy.HostedOnly)
        : this(manager, () => owner, policy)
    {
        ArgumentNullException.ThrowIfNull(owner);
    }

    /// <summary>Initializes a new instance of the <see cref="MenuPopupHost"/> class.</summary>
    /// <param name="manager">The borrowed popup manager.</param>
    /// <param name="owner">Resolves the application's live owner registration.</param>
    /// <param name="policy">Explicit existing presentation policy.</param>
    public MenuPopupHost(UiPopupManager manager, Func<UiPopupOwner> owner, UiWindowPolicy policy = UiWindowPolicy.HostedOnly)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(owner);
        _manager = manager;
        _owner = owner;
        _policy = policy;
    }

    /// <inheritdoc/>
    public IDisposable Open(UiElement anchor, Vector2 size, Func<Widget> createContent, bool passive, Action closed)
    {
        ArgumentNullException.ThrowIfNull(createContent);
        ArgumentNullException.ThrowIfNull(closed);
        var request = new UiPopupRequest(anchor, size, createContent, _policy, side: passive ? UiWindowPlacementSide.Below : UiWindowPlacementSide.Right, inputMode: passive ? UiPopupInputMode.Passive : UiPopupInputMode.Menu, onClosed: _ => closed());
        return _manager.QueueOpenAt(_owner, request);
    }

    /// <inheritdoc/>
    public void ItemInvoked(UiElement anchor) => _manager.RequestItemInvoked(anchor);
}
