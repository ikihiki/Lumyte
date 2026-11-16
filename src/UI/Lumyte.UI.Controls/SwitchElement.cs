using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;
using ControlWidget = Lumyte.UI.Controls.Compose.Definitions.Switch;

namespace Lumyte.UI.Controls;

/// <summary>Owns an independent accepted Switch value.</summary>
public class SwitchElement : SelectionControlElement<ControlWidget>
{
    /// <summary>Initializes a new instance of the <see cref="SwitchElement"/> class.</summary>
    /// <param name="widget">The initial passive declaration.</param>
    protected internal SwitchElement(ControlWidget widget)
        : base(widget)
    {
        IsOn = widget.IsOn;
    }

    /// <summary>Gets a value indicating whether the accepted instance is checked.</summary>
    public bool IsOn { get; private set; }

    /// <inheritdoc/>
    protected override WidgetChildren ContentSlot => Widget.Children;

    /// <inheritdoc/>
    protected override WidgetChildren BackgroundSlot => Widget.BackgroundChildren;

    /// <inheritdoc/>
    protected override float MarkSize => Widget.MarkSize;

    /// <inheritdoc/>
    protected override float Gap => Widget.Gap;

    /// <inheritdoc/>
    protected override SRgba<float> Foreground => Widget.Foreground;

    /// <inheritdoc/>
    protected override SRgba<float> Background => Widget.TrackColor;

    /// <inheritdoc/>
    protected override bool? CheckedValue => IsOn;

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(ControlWidget next, UiElementUpdateContext context)
    {
        if (ReferenceEquals(next, Widget))
        {
            return null;
        }

        return () =>
        {
            IsOn = next.IsOn;
            InvalidateDrawing();
        };
    }

    /// <inheritdoc/>
    protected override void Activate() => Commit(!IsOn);

    /// <inheritdoc/>
    protected override void CommitChecked(bool value) => Commit(value);

    /// <inheritdoc/>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        float size = Math.Min(Widget.MarkSize, Math.Min(ContentSize.X, ContentSize.Y));
        if (size <= 0)
        {
            return;
        }

        canvas.FillRect(new(0, size * 0.25f, size, size * 0.5f), Widget.TrackColor);
        canvas.FillRect(new(IsOn ? size * 0.5f : 0, size * 0.15f, size * 0.5f, size * 0.7f), Widget.Foreground);
    }

    private void Commit(bool value)
    {
        if (IsOn == value)
        {
            return;
        }

        IsOn = value;
        InvalidateDrawing();
        Action<bool>? callback = Widget.OnChanged;
        callback?.Invoke(value);
    }
}
