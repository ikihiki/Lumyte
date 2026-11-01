namespace Lumyte.UI.Windowing;

internal static class UiWindowStacking
{
    internal static int Layer(UiWindowRole role) => role switch
    {
        UiWindowRole.Popup or UiWindowRole.ContextMenu => 1,
        UiWindowRole.ImeCandidates => 2,
        _ => 0,
    };

    internal static (int Layer, ulong Order) Key(UiWindowRole role, UiWindowId window, ulong stackingOrder)
        => (Layer(role), stackingOrder == 0 ? window.Value : stackingOrder);
}
