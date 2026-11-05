using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using IconWidget = Lumyte.UI.Media.Compose.Definitions.Icon;

namespace Lumyte.UI.Media;

/// <summary>Displays a sized representation without introducing Button behavior.</summary>
public class IconElement : UiElement<IconWidget>
{
    /// <summary>Initializes a new instance of the <see cref="IconElement"/> class.</summary>
    /// <param name="widget">The configuration.</param>
    protected internal IconElement(IconWidget widget)
        : base(widget)
    {
    }

    /// <inheritdoc/>
    protected override bool CacheRetainedDrawing => true;

    /// <inheritdoc/>
    protected override bool SupportsTransitionCapture => GetType() == typeof(IconElement);

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.None;

    /// <inheritdoc/>
    protected override bool CanReceiveFocus => false;

    /// <inheritdoc/>
    /// <remarks>Placement depends on the final body size, which may differ between shared-flight endpoints.</remarks>
    protected override bool CanReuseTransitionCapture(Lumyte.UI.Compose.Definitions.Widget other) => false;

    /// <inheritdoc/>
    protected override UiInvalidation CompareConfiguration(IconWidget previous, IconWidget next) => previous.Size != next.Size ? UiInvalidation.Layout : !ReferenceEquals(previous.Source, next.Source) ? UiInvalidation.Visual : UiInvalidation.None;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(new(Widget.Size));

    /// <inheritdoc/>
    protected override void OnRecord(DrawingCanvas2D canvas) => Widget.Source?.Record(canvas, ContentSize);
}
