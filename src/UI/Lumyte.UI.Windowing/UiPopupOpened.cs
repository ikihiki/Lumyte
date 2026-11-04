namespace Lumyte.UI.Windowing;

/// <summary>A detached UiPopupOpened value.</summary>
/// <param name="PopupId">The PopupId value.</param>
/// <param name="Presentation">The Presentation value.</param>
/// <param name="FallbackCode">The FallbackCode value.</param>
public sealed record UiPopupOpened(Guid PopupId, UiWindowPresentation Presentation, string FallbackCode);
