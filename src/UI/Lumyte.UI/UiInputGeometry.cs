using System.Numerics;

namespace Lumyte.UI;

internal static class UiInputGeometry
{
    internal static Vector2 Origin(UiElement node, Vector2 parentOrigin) => UiLayoutMath.Add(parentOrigin, new(node.Bounds.X, node.Bounds.Y));

    internal static bool Contains(Vector2 origin, Vector2 size, Vector2 point) =>
        UiInputBounds.From(origin, size).Contains(point);

    internal static bool AcceptsInput(UiElement node) => !node.IsTransitioning && node.LayoutVisible && node.Widget.IsEnabled && node.Widget.HitTestVisible;
}
