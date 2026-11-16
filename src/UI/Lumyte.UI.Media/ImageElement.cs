using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using ImageWidget = Lumyte.UI.Media.Compose.Definitions.Image;

namespace Lumyte.UI.Media;

/// <summary>Records a borrowed prepared snapshot using persistent body geometry.</summary>
public class ImageElement : UiElement<ImageWidget>
{
    /// <summary>Initializes a new instance of the <see cref="ImageElement"/> class.</summary>
    /// <param name="widget">The passive configuration.</param>
    protected internal ImageElement(ImageWidget widget)
        : base(widget)
    {
    }

    /// <inheritdoc/>
    protected override bool CacheRetainedDrawing => true;

    /// <inheritdoc/>
    protected override bool SupportsTransitionCapture => GetType() == typeof(ImageElement);

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.None;

    /// <inheritdoc/>
    protected override bool CanReceiveFocus => false;

    /// <inheritdoc/>
    /// <remarks>Placement depends on the final body size, which may differ between shared-flight endpoints.</remarks>
    protected override bool CanReuseTransitionCapture(Lumyte.UI.Compose.Definitions.Widget other) => false;

    /// <inheritdoc/>
    protected override UiInvalidation CompareConfiguration(ImageWidget previous, ImageWidget next) =>
        !ReferenceEquals(previous.Source, next.Source) || previous.PlaceholderSize != next.PlaceholderSize ? UiInvalidation.Layout | UiInvalidation.Visual : previous.Stretch != next.Stretch ? UiInvalidation.Visual : UiInvalidation.None;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => Widget.Source is { } source
        ? MediaGeometry.Measure(source.IntrinsicSize, constraints) : constraints.Constrain(Widget.PlaceholderSize);

    /// <inheritdoc/>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        if (Widget.Source is { } source && ContentSize.X > 0 && ContentSize.Y > 0)
        {
            canvas.DrawImage(source.Image, source.Source, MediaGeometry.Place(source.IntrinsicSize, ContentSize, Widget.Stretch));
        }
    }
}
