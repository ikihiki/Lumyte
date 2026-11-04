using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using SeparatorWidget = Lumyte.UI.Compose.Definitions.Separator;

namespace Lumyte.UI;

internal sealed class SeparatorElement(SeparatorWidget widget) : UiElement<SeparatorWidget>(widget)
{
    protected override bool SupportsTransitionCapture => Widget.GetType() == typeof(SeparatorWidget);

    protected override bool CanReuseTransitionCapture(Compose.Definitions.Widget other) => other.GetType() == typeof(SeparatorWidget)
        && other is SeparatorWidget separator && separator.Color == Widget.Color
        && separator.Thickness == Widget.Thickness && separator.Orientation == Widget.Orientation;

    protected override UiInvalidation CompareConfiguration(SeparatorWidget previous, SeparatorWidget next) =>
        previous.Orientation != next.Orientation || previous.Thickness != next.Thickness ? UiInvalidation.All : previous.Color != next.Color ? UiInvalidation.Visual : UiInvalidation.None;

    protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(Widget.Orientation == UiOrientation.Horizontal
        ? new(constraints.HasBoundedWidth ? constraints.MaxWidth : constraints.MinWidth, Widget.Thickness)
        : new(Widget.Thickness, constraints.HasBoundedHeight ? constraints.MaxHeight : constraints.MinHeight));

    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        if (Widget.Color.A == 0 || Widget.Thickness == 0)
        {
            return;
        }

        bool horizontal = Widget.Orientation == UiOrientation.Horizontal;
        float thickness = Math.Min(Widget.Thickness, horizontal ? ContentSize.Y : ContentSize.X);
        Rect2D rectangle = horizontal
            ? new(0, (ContentSize.Y - thickness) / 2, ContentSize.X, thickness)
            : new((ContentSize.X - thickness) / 2, 0, thickness, ContentSize.Y);
        canvas.FillRect(rectangle, Widget.Color);
    }
}
