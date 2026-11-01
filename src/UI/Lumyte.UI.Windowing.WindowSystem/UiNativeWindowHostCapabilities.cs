namespace Lumyte.UI.Windowing.WindowSystem;

/// <summary>Host-verified input observations required before enabling native popups.</summary>
/// <param name="CanObserveOutsidePress">Whether outside presses and deactivation are observed.</param>
/// <param name="CanCancelCrossWindowPointers">Whether stable device identities allow cross-window cancellation.</param>
public sealed record UiNativeWindowHostCapabilities(bool CanObserveOutsidePress, bool CanCancelCrossWindowPointers);
