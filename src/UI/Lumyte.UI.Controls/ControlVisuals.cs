using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

// Each part is an independent content boundary. Fixed declarations may remain on that same
// part, but cannot cross hosts, surfaces, or lower/upper boundaries.
internal sealed class ControlVisuals : Widget
{
    internal ControlVisuals(params VisualPart[] parts)
    {
        Parts = CreateChildren();
        Parts.Initialize(parts);
    }

    internal WidgetChildren Parts { get; }

    /// <summary>Creates a visual part at the existing fixed thumb rectangle.</summary>
    internal static VisualPart ThumbPart(string key, Widget content, double value, double minimum, double maximum, UiOrientation orientation, bool reversed) =>
        new(key, content, size =>
        {
            double fraction = SliderScale.Fraction(value, minimum, maximum);
            if (reversed)
            {
                fraction = 1 - fraction;
            }

            float length = orientation == UiOrientation.Horizontal ? size.X : size.Y;
            float extent = Math.Min(10, length);
            float start = (float)fraction * Math.Max(0, length - extent);
            return orientation == UiOrientation.Horizontal ? (new(extent, size.Y), new(start, 0)) : (new(size.X, extent), new(0, start));
        });

    protected override UiElement CreateElement() => new VisualsElement(this);

    private sealed class VisualsElement(ControlVisuals widget) : UiElement<ControlVisuals>(widget)
    {
        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            Vector2 size = constraints.Constrain(new(constraints.MaxWidth, constraints.MaxHeight));
            foreach (UiElement child in Children)
            {
                var part = (VisualPart)child.Widget;
                (Vector2 extent, Vector2 offset) = part.Geometry(size);
                LayoutChild(child, UiBoxConstraints.Tight(extent));
                SetChildOffset(child, offset);
            }

            return size;
        }
    }
}
