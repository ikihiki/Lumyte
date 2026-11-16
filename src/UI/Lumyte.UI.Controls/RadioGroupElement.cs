using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;
using GroupWidget = Lumyte.UI.Controls.Compose.Definitions.RadioGroup;

namespace Lumyte.UI.Controls;

/// <summary>Owns one ordinal selection for its direct radio options.</summary>
public class RadioGroupElement : UiElement<GroupWidget>
{
    /// <summary>Initializes a new instance of the <see cref="RadioGroupElement"/> class.</summary>
    /// <param name="widget">The passive group declaration.</param>
    protected internal RadioGroupElement(GroupWidget widget)
        : base(widget)
    {
        SelectedKey = widget.SelectedKey;
    }

    /// <summary>Gets the accepted selected key, or null.</summary>
    public string? SelectedKey { get; private set; }

    internal RadioButtonElement? TabStop => Options.FirstOrDefault(option => option.IsSelected && Eligible(option)) ?? Options.FirstOrDefault(Eligible);

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.SelectOption;

    /// <inheritdoc/>
    protected override bool CanReceiveFocus => false;

    /// <inheritdoc/>
    protected override string? AutomationSelectedKey => SelectedKey;

    private IEnumerable<RadioButtonElement> Options => GetChildren(Widget.Children).OfType<RadioButtonElement>();

    internal void Select(RadioButtonElement option)
    {
        string key = option.Widget.Key!;
        if (string.Equals(SelectedKey, key, StringComparison.Ordinal))
        {
            return;
        }

        SelectedKey = key;
        RefreshOptions();
        Action<string?>? callback = Widget.OnChanged;
        callback?.Invoke(key);
    }

    internal UiInputResult Navigate(RadioButtonElement current, in UiInputContext context)
    {
        if (context.Input is not UiKeyInput { IsDown: true, IsRepeat: false, Modifiers: UiModifiers.None } key
            || key.Key is not (Key.ArrowLeft or Key.ArrowRight or Key.ArrowUp or Key.ArrowDown))
        {
            return UiInputResult.Unhandled;
        }

        RadioButtonElement[] options = Options.Where(Eligible).ToArray();
        if (options.Length == 0)
        {
            return UiInputResult.Handled;
        }

        int index = Array.IndexOf(options, current);
        int direction = key.Key is Key.ArrowLeft or Key.ArrowUp ? -1 : 1;
        RadioButtonElement next = options[(index + direction + options.Length) % options.Length];
        if (Surface!.TryFocus(next))
        {
            Select(next);
        }

        return UiInputResult.Handled;
    }

    /// <inheritdoc/>
    protected override IEnumerable<UiElement> GetInputChildren() => GetChildren(Widget.Children);

    /// <inheritdoc/>
    protected override IEnumerable<UiElement> GetAutomationChildren() => GetChildren(Widget.Children);

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(GroupWidget next, UiElementUpdateContext context)
    {
        string? selection = ReferenceEquals(next, Widget) ? SelectedKey : next.SelectedKey;
        bool exists = context.GetChildren(next.Children).Any(option => option is RadioButtonElement && string.Equals(option.Widget.Key, selection, StringComparison.Ordinal));
        return () =>
        {
            SelectedKey = exists ? selection : null;
            RefreshOptions();
        };
    }

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        Vector2 size = Vector2.Zero;
        foreach (UiElement child in GetChildren(Widget.Children))
        {
            Vector2 childSize = LayoutChild(child, constraints.Loosen());
            SetChildOffset(child, new(0, size.Y));
            size = new(Math.Max(size.X, childSize.X), size.Y + childSize.Y);
        }

        size = constraints.Constrain(size);
        foreach (UiElement child in GetChildren(Widget.BackgroundChildren))
        {
            LayoutChild(child, UiBoxConstraints.Tight(size));
            SetChildOffset(child, Vector2.Zero);
        }

        return size;
    }

    /// <inheritdoc/>
    protected override void RecordOverride(DrawingCanvas2D canvas)
    {
        foreach (UiElement child in GetChildren(Widget.BackgroundChildren))
        {
            RecordChild(child, canvas);
        }

        foreach (UiElement child in GetChildren(Widget.Children))
        {
            RecordChild(child, canvas);
        }
    }

    private static bool Eligible(RadioButtonElement option) => option.Widget.Focusable && option.Widget.IsEnabled && option.Widget.IsVisible
        && option.Widget.HitTestVisible && option.PresenceState == UiPresenceState.Visible;

    private void RefreshOptions()
    {
        foreach (RadioButtonElement option in Options)
        {
            option.SelectionChanged();
        }

        InvalidateInput();
    }
}
