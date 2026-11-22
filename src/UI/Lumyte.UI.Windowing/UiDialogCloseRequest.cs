namespace Lumyte.UI.Windowing;

/// <summary>A controlled close request.</summary>
/// <param name="DialogId">The DialogId value.</param>
/// <param name="Reason">The Reason value.</param>
public sealed record UiDialogCloseRequest(Guid DialogId, UiDialogCloseRequestReason Reason);
