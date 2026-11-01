using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Windowing;

/// <summary>A detached UiWindowPresentationView value.</summary>
/// <param name="Window">The Window value.</param>
/// <param name="SourceId">The SourceId value.</param>
/// <param name="Role">The Role value.</param>
/// <param name="BoundsInPresentation">The BoundsInPresentation value.</param>
/// <param name="ClipInPresentation">The ClipInPresentation value.</param>
/// <param name="Scale">The Scale value.</param>
/// <param name="GeometryRevision">The GeometryRevision value.</param>
/// <param name="IsVisible">Whether the binding contributes to presentation.</param>
public sealed record UiWindowPresentationView(UiWindowId Window, Guid SourceId, UiWindowRole Role, Rect2D BoundsInPresentation, Rect2D ClipInPresentation, Vector2 Scale, ulong GeometryRevision, bool IsVisible = false);
