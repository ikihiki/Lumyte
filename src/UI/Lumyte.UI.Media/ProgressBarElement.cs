using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using ProgressWidget = Lumyte.UI.Media.Compose.Definitions.ProgressBar;

namespace Lumyte.UI.Media;

/// <summary>Records read-only progress in body coordinates.</summary>
public class ProgressBarElement : UiElement<ProgressWidget>
{
    /// <summary>Initializes a new instance of the <see cref="ProgressBarElement"/> class.</summary>
    /// <param name="widget">The configuration.</param>
    protected internal ProgressBarElement(ProgressWidget widget)
        : base(widget)
    {
    }

    /// <inheritdoc/>
    protected override bool CacheRetainedDrawing => true;

    /// <inheritdoc/>
    protected override bool SupportsTransitionCapture => GetType() == typeof(ProgressBarElement);

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.None;

    /// <inheritdoc/>
    protected override bool CanReceiveFocus => false;

    /// <inheritdoc/>
    protected override double? AutomationValue => Widget.Value;

    /// <inheritdoc/>
    protected override bool CanReuseTransitionCapture(Lumyte.UI.Compose.Definitions.Widget other) => other is ProgressWidget progress && progress.Value == Widget.Value && progress.Foreground == Widget.Foreground && progress.Background == Widget.Background;

    /// <inheritdoc/>
    protected override UiInvalidation CompareConfiguration(ProgressWidget previous, ProgressWidget next) => previous.Value == next.Value && previous.Foreground == next.Foreground && previous.Background == next.Background ? UiInvalidation.None : UiInvalidation.Visual;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(new(120, 4));

    /// <inheritdoc/>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        if (ContentSize.X <= 0 || ContentSize.Y <= 0)
        {
            return;
        }

        canvas.FillRect(new(0, 0, ContentSize.X, ContentSize.Y), Widget.Background);
        if (Widget.Value > 0)
        {
            canvas.FillRect(new(0, 0, (float)(ContentSize.X * Widget.Value), ContentSize.Y), Widget.Foreground);
        }
    }
}
