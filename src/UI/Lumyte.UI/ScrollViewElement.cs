using System.Numerics;
using ScrollWidget = Lumyte.UI.Compose.Definitions.ScrollView;

namespace Lumyte.UI;

/// <summary>Owns viewport layout and scrolling independently of its declaration.</summary>
/// <param name="widget">The passive declaration.</param>
public sealed class ScrollViewElement(ScrollWidget widget) : UiElement<ScrollWidget>(widget)
{
    private readonly ScrollController _local = new();
    private ScrollController? _bound;

    /// <summary>Gets the active borrowed or element-local controller.</summary>
    public ScrollController Controller => _bound ?? _local;

    /// <inheritdoc/>
    protected override bool SupportsTransitionCapture => true;

    internal void FinalizeBinding(ScrollController? controller)
    {
        _local.Attach(this);
        _bound?.Detach(this);
        _bound = controller is { IsDisposed: false } ? controller : null;
        _bound?.Attach(this);
    }

    internal void OffsetChanged()
    {
        InvalidateLayout();
        InvalidateInput();
        InvalidateDrawing();
    }

    internal void ControllerDisposed()
    {
        _bound = null;
        _local.ResetGeometry();
        OffsetChanged();
    }

    internal float ApplyDelta(float delta) => Controller.ApplyDelta(delta);

    /// <inheritdoc/>
    protected override bool CanReuseTransitionCapture(Compose.Definitions.Widget other) => false;

    /// <inheritdoc/>
    protected override UiInvalidation CompareConfiguration(ScrollWidget previous, ScrollWidget next) => previous.Orientation != next.Orientation || !ReferenceEquals(previous.Controller, next.Controller) ? UiInvalidation.All : UiInvalidation.None;

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(ScrollWidget next, UiElementUpdateContext context)
    {
        next.Controller?.ValidateBinding(this);
        if (next.Orientation != Widget.Orientation || !ReferenceEquals(next.Controller, Widget.Controller))
        {
            context.CancelInput();
        }

        return null;
    }

    /// <inheritdoc/>
    protected override void OnUnmounted()
    {
        _bound?.Detach(this);
        _local.Detach(this);
        _bound = null;
    }

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        if (!constraints.HasBoundedWidth || !constraints.HasBoundedHeight)
        {
            throw new InvalidOperationException("ScrollView requires finite viewport maxima on both axes.");
        }

        Vector2 viewport = new(constraints.MaxWidth, constraints.MaxHeight);
        bool vertical = Widget.Orientation == UiOrientation.Vertical;
        UiElementChildren children = GetChildren(Widget.Children);
        Vector2 content = Vector2.Zero;
        if (children.Count != 0)
        {
            content = LayoutChild(children[0], vertical ? new(0, viewport.X, 0, float.PositiveInfinity) : new(0, float.PositiveInfinity, 0, viewport.Y));
        }

        Controller.Geometry(vertical ? content.Y : content.X, vertical ? viewport.Y : viewport.X);
        if (children.Count != 0)
        {
            SetChildOffset(children[0], vertical ? new(0, -Controller.Offset) : new(-Controller.Offset, 0));
        }

        return viewport;
    }
}
