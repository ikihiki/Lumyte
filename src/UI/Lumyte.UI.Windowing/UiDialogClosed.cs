namespace Lumyte.UI.Windowing;

/// <summary>A terminal notification after complete cleanup, independent of business results.</summary>
/// <param name="DialogId">The nonreused identity.</param>
/// <param name="Reason">The terminal lifetime reason.</param>
/// <param name="Code">The fixed failure or closure code.</param>
/// <param name="Presentation">The actual presentation, or null if no dialog was presented.</param>
public sealed record UiDialogClosed(Guid DialogId, UiDialogCloseReason Reason, string Code, UiWindowPresentation? Presentation = null);
