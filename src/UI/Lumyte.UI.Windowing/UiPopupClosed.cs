namespace Lumyte.UI.Windowing;

/// <summary>A detached UiPopupClosed value.</summary>
/// <param name="PopupId">The PopupId value.</param>
/// <param name="Reason">The Reason value.</param>
/// <param name="Code">The Code value.</param>
public sealed record UiPopupClosed(Guid PopupId, UiPopupCloseReason Reason, string Code);
