namespace Lumyte.UI.Windowing;

/// <summary>A notification after actual presentation.</summary>
/// <param name="DialogId">The DialogId value.</param>
/// <param name="Window">The Window value.</param>
/// <param name="Presentation">The Presentation value.</param>
/// <param name="FallbackCode">The FallbackCode value.</param>
public sealed record UiDialogOpened(Guid DialogId, UiWindowId Window, UiWindowPresentation Presentation, string FallbackCode);
