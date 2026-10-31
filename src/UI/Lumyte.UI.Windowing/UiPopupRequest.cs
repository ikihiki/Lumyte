using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Windowing;

/// <summary>Fixed content construction and callback parameters for one popup request.</summary>
public sealed class UiPopupRequest
{
    /// <summary>Initializes a new instance of the <see cref="UiPopupRequest"/> class.</summary>
    /// <param name="anchor">A visible attached input-content anchor.</param>
    /// <param name="clientSize">Positive finite requested client size.</param>
    /// <param name="createContent">Constructs an independent content tree exactly once.</param>
    /// <param name="policy">Explicit native/hosted policy.</param>
    /// <param name="role">Popup or ContextMenu.</param>
    /// <param name="side">Preferred placement side.</param>
    /// <param name="gap">Nonnegative logical gap.</param>
    /// <param name="inputMode">Menu scope or passive tooltip.</param>
    /// <param name="onOpened">One callback after the actual show commit.</param>
    /// <param name="onClosed">One callback after all cleanup, including failed opening.</param>
    public UiPopupRequest(Widget anchor, Vector2 clientSize, Func<Widget> createContent, UiWindowPolicy policy = UiWindowPolicy.HostedOnly, UiWindowRole role = UiWindowRole.Popup, UiWindowPlacementSide side = UiWindowPlacementSide.Below, float gap = 0, UiPopupInputMode inputMode = UiPopupInputMode.Menu, Action<UiPopupOpened>? onOpened = null, Action<UiPopupClosed>? onClosed = null)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(createContent);
        if (!float.IsFinite(clientSize.X) || !float.IsFinite(clientSize.Y) || clientSize.X <= 0 || clientSize.Y <= 0 || !float.IsFinite(gap) || gap < 0 || role is not (UiWindowRole.Popup or UiWindowRole.ContextMenu) || policy is < UiWindowPolicy.HostedOnly or > UiWindowPolicy.RequireNative || side is < UiWindowPlacementSide.Below or > UiWindowPlacementSide.Left || inputMode is not (UiPopupInputMode.Menu or UiPopupInputMode.Passive))
        {
            throw new ArgumentException("Invalid popup request.");
        }

        Anchor = anchor;
        ClientSize = clientSize;
        CreateContent = createContent;
        Policy = policy;
        Role = role;
        Side = side;
        Gap = gap;
        InputMode = inputMode;
        OnOpened = onOpened;
        OnClosed = onClosed;
    }

    internal Widget Anchor { get; }

    internal Vector2 ClientSize { get; }

    internal Func<Widget> CreateContent { get; }

    internal UiWindowPolicy Policy { get; }

    internal UiWindowRole Role { get; }

    internal UiWindowPlacementSide Side { get; }

    internal float Gap { get; }

    internal UiPopupInputMode InputMode { get; }

    internal Action<UiPopupOpened>? OnOpened { get; }

    internal Action<UiPopupClosed>? OnClosed { get; }
}
