using System.Numerics;

namespace Lumyte.UI.Windowing;

/// <summary>A detached UiWindowHit value.</summary>
/// <param name="Window">The Window value.</param>
/// <param name="LocalPosition">The LocalPosition value.</param>
/// <param name="GeometryRevision">The GeometryRevision value.</param>
/// <param name="InputMode">The InputMode value.</param>
public readonly record struct UiWindowHit(UiWindowId Window, Vector2 LocalPosition, ulong GeometryRevision, UiWindowInputMode InputMode);
