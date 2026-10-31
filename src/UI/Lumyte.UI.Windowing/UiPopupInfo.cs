namespace Lumyte.UI.Windowing;

/// <summary>A detached UiPopupInfo value.</summary>
/// <param name="Id">The Id value.</param>
/// <param name="OwnerId">The OwnerId value.</param>
/// <param name="ParentPopupId">The ParentPopupId value.</param>
/// <param name="SurfaceId">The SurfaceId value.</param>
/// <param name="State">The State value.</param>
/// <param name="Presentation">The Presentation value.</param>
/// <param name="InputMode">The InputMode value.</param>
/// <param name="FallbackCode">The FallbackCode value.</param>
public sealed record UiPopupInfo(Guid Id, Guid OwnerId, Guid? ParentPopupId, Guid SurfaceId, UiPopupState State, UiWindowPresentation? Presentation, UiPopupInputMode InputMode, string FallbackCode);
