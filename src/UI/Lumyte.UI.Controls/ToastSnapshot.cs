namespace Lumyte.UI.Controls;

/// <summary>A payload-free snapshot of one accepted notification.</summary>
/// <param name="Id">The identity.</param>
/// <param name="IsVisible">Whether this is the admitted head.</param>
public sealed record ToastSnapshot(ToastId Id, bool IsVisible);
