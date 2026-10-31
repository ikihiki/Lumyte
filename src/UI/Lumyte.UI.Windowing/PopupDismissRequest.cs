namespace Lumyte.UI.Windowing;

/// <summary>A controlled popup dismissal request, separate from actual closure.</summary>
/// <param name="Reason">The requested model update reason.</param>
public sealed record PopupDismissRequest(PopupDismissReason Reason);
