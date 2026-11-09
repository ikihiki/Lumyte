using System.Numerics;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;
using BusyWidget = Lumyte.UI.Media.Compose.Definitions.BusyIndicator;

namespace Lumyte.UI.Media;

/// <summary>Owns per-mount busy phase without timers, threads or external subscriptions.</summary>
public class BusyIndicatorElement : UiElement<BusyWidget>
{
    private TimePoint? _lastTime;
    private long _activeTicks;

    /// <summary>Initializes a new instance of the <see cref="BusyIndicatorElement"/> class.</summary>
    /// <param name="widget">The passive configuration.</param>
    protected internal BusyIndicatorElement(BusyWidget widget)
        : base(widget)
    {
    }

    /// <summary>Gets the last applied normalized phase; reading never samples time.</summary>
    public double Phase => (_activeTicks % TimeSpan.TicksPerSecond) / (double)TimeSpan.TicksPerSecond;

    /// <inheritdoc/>
    protected override bool HasFrameAnimation => true;

    /// <inheritdoc/>
    protected override bool CacheRetainedDrawing => true;

    /// <inheritdoc/>
    protected override bool SupportsTransitionCapture => GetType() == typeof(BusyIndicatorElement);

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.None;

    /// <inheritdoc/>
    protected override bool CanReceiveFocus => false;

    /// <inheritdoc/>
    protected override bool IsAutomationIndeterminate => true;

    /// <inheritdoc/>
    protected override bool CanReuseTransitionCapture(Lumyte.UI.Compose.Definitions.Widget other) => false;

    /// <inheritdoc/>
    protected override UiInvalidation CompareConfiguration(BusyWidget previous, BusyWidget next) => previous.Color == next.Color ? UiInvalidation.None : UiInvalidation.Visual;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(new(24));

    /// <inheritdoc/>
    protected override void OnAnimationFrame(TimePoint? now)
    {
        if (now is { } current && _lastTime is { } previous)
        {
            if (current < previous)
            {
                throw new InvalidOperationException("The borrowed animation clock moved backwards.");
            }

            long elapsed = (current - previous).Ticks;
            if (elapsed != 0)
            {
                _activeTicks = checked(_activeTicks + elapsed);
                InvalidateDrawing();
            }
        }

        _lastTime = now;
    }

    /// <inheritdoc/>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        if (ContentSize.X <= 0 || ContentSize.Y <= 0)
        {
            return;
        }

        float side = MathF.Min(ContentSize.X, ContentSize.Y);
        float dot = side / 6;
        Vector2 center = ContentSize / 2;
        for (int index = 0; index < 8; index++)
        {
            double angle = (Phase * Math.Tau) + (index * Math.Tau / 8);
            Vector2 position = center + (new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * (side / 3));
            Color2D color = Widget.Color;
            canvas.FillRect(new(position.X - (dot / 2), position.Y - (dot / 2), dot, dot), new Color2D(color.R, color.G, color.B, color.A * ((index + 1) / 8f)));
        }
    }
}
