using System.Numerics;

namespace Lumyte.UI;

/// <summary>Describes acquisition, geometry refresh or loss of a widget's pointer capture.</summary>
/// <param name="PointerId">The logical pointer identity.</param>
/// <param name="IsCaptured">Whether the widget still owns this capture.</param>
/// <param name="Position">The last known surface-local position.</param>
/// <param name="IsInside">Whether the position is inside the widget's prepared input region.</param>
public readonly record struct UiPointerCaptureState(long PointerId, bool IsCaptured, Vector2 Position, bool IsInside);
