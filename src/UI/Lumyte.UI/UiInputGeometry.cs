using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

internal static class UiInputGeometry
{
    internal static Vector2 Origin(Widget node, Vector2 parentOrigin) => new(
        UiLayoutMath.FiniteFloat((double)parentOrigin.X + node.Bounds.X),
        UiLayoutMath.FiniteFloat((double)parentOrigin.Y + node.Bounds.Y));

    internal static bool Contains(Vector2 origin, Vector2 size, Vector2 point) =>
        size.X > 0 && size.Y > 0 && point.X >= origin.X && point.Y >= origin.Y
        && point.X < (double)origin.X + size.X && point.Y < (double)origin.Y + size.Y;

    internal static bool AcceptsInput(Widget node) => node.IsVisible && node.IsEnabled && node.HitTestVisible;
}
