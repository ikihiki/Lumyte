using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;

namespace Lumyte.UI.Windowing;

/// <summary>A detached UiWindowSnapshot value.</summary>
/// <param name="Id">The Id value.</param>
/// <param name="Revision">The Revision value.</param>
/// <param name="State">The State value.</param>
/// <param name="Owner">The Owner value.</param>
/// <param name="PresentationRoot">The PresentationRoot value.</param>
/// <param name="KeyboardRoot">The KeyboardRoot value.</param>
/// <param name="SourceId">The SourceId value.</param>
/// <param name="InputOrigin">The InputOrigin value.</param>
/// <param name="KeyboardScopeId">The KeyboardScopeId value.</param>
/// <param name="Role">The Role value.</param>
/// <param name="InputMode">The InputMode value.</param>
/// <param name="Presentation">The Presentation value.</param>
/// <param name="ClientSize">The ClientSize value.</param>
/// <param name="Scale">The Scale value.</param>
/// <param name="BoundsInPresentation">The BoundsInPresentation value.</param>
/// <param name="ClipInPresentation">The ClipInPresentation value.</param>
/// <param name="IsVisible">The IsVisible value.</param>
/// <param name="IsActive">The IsActive value.</param>
/// <param name="ShowIssued">The ShowIssued value.</param>
/// <param name="FallbackCode">The FallbackCode value.</param>
/// <param name="FailureCode">The FailureCode value.</param>
public sealed record UiWindowSnapshot(UiWindowId Id, ulong Revision, UiWindowState State, UiWindowId? Owner, UiWindowId PresentationRoot, UiWindowId KeyboardRoot, Guid SourceId, InputWindowId? InputOrigin, Guid KeyboardScopeId, UiWindowRole Role, UiWindowInputMode InputMode, UiWindowPresentation? Presentation, Vector2 ClientSize, Vector2 Scale, Rect2D BoundsInPresentation, Rect2D ClipInPresentation, bool IsVisible, bool IsActive, bool ShowIssued, string FallbackCode, string FailureCode);
