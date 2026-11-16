using Lumyte.Mathematics.Colors;
using ControlWidget = Lumyte.UI.Controls.Compose.Definitions.ToggleButton;

namespace Lumyte.UI.Controls;

/// <summary>Owns an independent accepted ToggleButton value.</summary>
public class ToggleButtonElement : SelectionControlElement<ControlWidget>
{
    /// <summary>Initializes a new instance of the <see cref="ToggleButtonElement"/> class.</summary>
    /// <param name="widget">The initial passive declaration.</param>
    protected internal ToggleButtonElement(ControlWidget widget)
        : base(widget)
    {
        IsChecked = widget.IsChecked;
    }

    /// <summary>Gets a value indicating whether the accepted instance is checked.</summary>
    public bool IsChecked { get; private set; }

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
    protected override bool? CheckedValue => IsChecked;

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(ControlWidget next, UiElementUpdateContext context)
    {
        if (ReferenceEquals(next, Widget))
        {
            return null;
        }

        return () =>
        {
            IsChecked = next.IsChecked;
            InvalidateDrawing();
        };
    }

    /// <inheritdoc/>
    protected override void Activate() => Commit(!IsChecked);

    /// <inheritdoc/>
    protected override void CommitChecked(bool value) => Commit(value);

    private void Commit(bool value)
    {
        if (IsChecked == value)
        {
            return;
        }

        IsChecked = value;
        InvalidateDrawing();
        Action<bool>? callback = Widget.OnChanged;
        callback?.Invoke(value);
    }
}
