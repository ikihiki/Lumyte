namespace Lumyte.UI.Windowing;

/// <summary>Preferred LTR alignment relative to the anchor, with flipping and clamping.</summary>
public enum PopupPlacement
{
    /// <summary>Below, aligned to the left edge.</summary>
    BottomStart,

    /// <summary>Below, aligned to the right edge.</summary>
    BottomEnd,

    /// <summary>Above, aligned to the left edge.</summary>
    TopStart,

    /// <summary>Above, aligned to the right edge.</summary>
    TopEnd,

    /// <summary>Left, aligned to the top edge.</summary>
    LeftStart,

    /// <summary>Right, aligned to the top edge.</summary>
    RightStart,
}
