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
    protected override bool UsesDefaultRecordingOrder => GetType() == typeof(ProgressBarElement) && GetChildren(Widget.TrackChildren).Count == 0 && GetChildren(Widget.FillChildren).Count == 0;

    /// <inheritdoc/>
    protected override bool SupportsTransitionCapture => GetType() == typeof(ProgressBarElement) && !Widget.HasTrack && !Widget.HasFill;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.None;

    /// <inheritdoc/>
    protected override bool CanReceiveFocus => false;

    /// <inheritdoc/>
    protected override double? AutomationValue => Widget.Value;

    /// <inheritdoc/>
    protected override bool CanReuseTransitionCapture(Lumyte.UI.Compose.Definitions.Widget other) => other is ProgressWidget progress && progress.Value == Widget.Value && progress.Foreground == Widget.Foreground && progress.Background == Widget.Background && !progress.HasTrack && !progress.HasFill && !Widget.HasTrack && !Widget.HasFill;

    /// <inheritdoc/>
    protected override UiInvalidation CompareConfiguration(ProgressWidget previous, ProgressWidget next)
    {
        if (previous.Value != next.Value && (previous.HasFill || next.HasFill))
        {
            return UiInvalidation.Layout | UiInvalidation.Visual;
        }

        return previous.Value == next.Value && previous.Foreground == next.Foreground && previous.Background == next.Background
            && previous.HasTrack == next.HasTrack && previous.HasFill == next.HasFill ? UiInvalidation.None : UiInvalidation.Visual;
    }

    /// <inheritdoc/>
    protected override void OnWidgetUpdated(ProgressWidget previous)
    {
        if (Widget.HasFill)
        {
            InvalidateLayout();
        }
    }

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        Vector2 size = constraints.Constrain(new(120, 4));
        LayoutPart(Widget.TrackChildren, size);
        LayoutPart(Widget.FillChildren, new((float)(size.X * Widget.Value), size.Y));
        return size;
    }

    /// <inheritdoc/>
    protected override IEnumerable<UiElement> GetInputChildren() => Array.Empty<UiElement>();

    /// <inheritdoc/>
    protected override IEnumerable<UiElement> GetAutomationChildren() => Array.Empty<UiElement>();

    /// <inheritdoc/>
    protected override void RecordOverride(DrawingCanvas2D canvas)
    {
        if (GetChildren(Widget.TrackChildren).Count == 0 && GetChildren(Widget.FillChildren).Count == 0)
        {
            RecordSelf(canvas);
            return;
        }

        if (ContentSize.X <= 0 || ContentSize.Y <= 0)
        {
            return;
        }

        if (Widget.HasTrack)
        {
            RecordPart(Widget.TrackChildren, ContentSize, canvas);
        }
        else
        {
            canvas.FillRect(new(0, 0, ContentSize.X, ContentSize.Y), Widget.Background);
        }

        Vector2 fillSize = new((float)(ContentSize.X * Widget.Value), ContentSize.Y);
        if (fillSize.X > 0)
        {
            if (Widget.HasFill)
            {
                RecordPart(Widget.FillChildren, fillSize, canvas);
            }
            else
            {
                canvas.FillRect(new(0, 0, fillSize.X, fillSize.Y), Widget.Foreground);
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        if (ContentSize.X <= 0 || ContentSize.Y <= 0)
        {
            return;
        }

        if (!Widget.HasTrack)
        {
            canvas.FillRect(new(0, 0, ContentSize.X, ContentSize.Y), Widget.Background);
        }

        if (!Widget.HasFill && Widget.Value > 0)
        {
            canvas.FillRect(new(0, 0, (float)(ContentSize.X * Widget.Value), ContentSize.Y), Widget.Foreground);
        }
    }

    private void LayoutPart(WidgetChildren slot, Vector2 size)
    {
        foreach (UiElement child in GetChildren(slot))
        {
            LayoutChild(child, UiBoxConstraints.Tight(size));
            SetChildOffset(child, Vector2.Zero);
        }
    }

    private void RecordPart(WidgetChildren slot, Vector2 size, DrawingCanvas2D canvas)
    {
        canvas.Save();
        try
        {
            canvas.ClipRect(new(0, 0, size.X, size.Y));
            foreach (UiElement child in GetChildren(slot))
            {
                RecordChild(child, canvas);
            }
        }
        finally
        {
            canvas.Restore();
        }
    }
}
