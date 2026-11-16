using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;
using RadioWidget = Lumyte.UI.Controls.Compose.Definitions.RadioButton;

namespace Lumyte.UI.Controls;

/// <summary>Derives selection from the owning group without an independent bool.</summary>
public class RadioButtonElement : SelectionControlElement<RadioWidget>
{
    /// <summary>Initializes a new instance of the <see cref="RadioButtonElement"/> class.</summary>
    /// <param name="widget">The passive option declaration.</param>
    protected internal RadioButtonElement(RadioWidget widget)
        : base(widget)
    {
    }

    /// <summary>Gets a value indicating whether the group currently selects this ordinal key.</summary>
    public bool IsSelected => Group is { } group && string.Equals(group.SelectedKey, Widget.Key, StringComparison.Ordinal);

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
    protected override bool? CheckedValue => IsSelected;

    /// <inheritdoc/>
    protected override bool? AutomationChecked => null;

    /// <inheritdoc/>
    protected override bool? AutomationSelected => IsSelected;

    /// <inheritdoc/>
    protected override UiAutomationId? AutomationSelectionGroup => Group?.AutomationId;

    /// <inheritdoc/>
    protected override string? AutomationOptionKey => Widget.Key;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.Click | UiAutomationActions.Focus;

    /// <inheritdoc/>
    protected override bool CanReceiveFocus => Widget.Focusable;

    /// <inheritdoc/>
    protected override bool IsTabStop => ReferenceEquals(Group?.TabStop, this);

    private RadioGroupElement? Group => Parent as RadioGroupElement;

    internal void SelectionChanged() => InvalidateDrawing();

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        UiInputResult navigation = Group?.Navigate(this, in context) ?? UiInputResult.Unhandled;
        return navigation == UiInputResult.Unhandled ? base.OnInput(in context) : navigation;
    }

    /// <inheritdoc/>
    protected override void Activate() => Group?.Select(this);

    /// <inheritdoc/>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        float size = Math.Min(Widget.MarkSize, Math.Min(ContentSize.X, ContentSize.Y));
        if (size <= 0)
        {
            return;
        }

        canvas.FillRect(new(size * 0.15f, size * 0.15f, size * 0.7f, size * 0.7f), Widget.TrackColor);
        if (IsSelected)
        {
            canvas.FillRect(new(size * 0.35f, size * 0.35f, size * 0.3f, size * 0.3f), Widget.Foreground);
        }
    }

    /// <inheritdoc/>
    protected override void CommitChecked(bool value)
    {
        if (value)
        {
            Activate();
        }
    }
}
