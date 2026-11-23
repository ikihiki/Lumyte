using System.Numerics;
using Lumyte.Input.Abstractions;
using MenuWidget = Lumyte.UI.Controls.Compose.Definitions.Menu;

namespace Lumyte.UI.Controls;

/// <summary>Owns menu membership, layout and roving item focus.</summary>
public class MenuElement : UiElement<MenuWidget>
{
    private MenuItemElement? _active;

    /// <summary>Initializes a new instance of the <see cref="MenuElement"/> class.</summary>
    /// <param name="widget">The passive declaration.</param>
    protected internal MenuElement(MenuWidget widget)
        : base(widget)
    {
    }

    internal MenuItemElement? TabStop => _active is { } active && Items.Contains(active) && Eligible(active)
        ? active : Items.FirstOrDefault(Eligible);

    internal IMenuPopupHost? PopupHost => Widget.PopupHost;

    /// <inheritdoc/>
    protected override bool CanReceiveFocus => false;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.None;

    private IEnumerable<MenuItemElement> Items => Children.OfType<MenuItemElement>();

    internal void FocusItem(MenuItemElement item)
    {
        _active = item;
        InvalidateInput();
    }

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(MenuWidget next, UiElementUpdateContext context)
    {
        if (!ReferenceEquals(next, Widget))
        {
            context.CancelInput();
        }

        return null;
    }

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (context.Input is UiKeyCancelInput)
        {
            return context.IsKeyCaptured ? UiInputResult.Handled : UiInputResult.Unhandled;
        }

        if (context.Input is not UiKeyInput key || key.Key is not (Key.ArrowUp or Key.ArrowDown or Key.Home or Key.End))
        {
            return UiInputResult.Unhandled;
        }

        if (context.IsKeyCaptured)
        {
            return UiInputResult.Handled;
        }

        if (!key.IsDown || key.Modifiers != UiModifiers.None || key.IsRepeat || !context.CaptureKey())
        {
            return UiInputResult.Unhandled;
        }

        MenuItemElement[] items = Items.Where(Eligible).ToArray();
        if (items.Length != 0)
        {
            int current = Array.IndexOf(items, Surface?.FocusedElement);
            int next = key.Key == Key.Home ? 0 : key.Key == Key.End ? items.Length - 1
                : current < 0 ? key.Key == Key.ArrowUp ? items.Length - 1 : 0
                : (current + (key.Key == Key.ArrowUp ? -1 : 1) + items.Length) % items.Length;
            _active = items[next];
            InvalidateInput();
            Surface?.TryFocus(_active);
        }

        return UiInputResult.Handled;
    }

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        Vector2 size = Vector2.Zero;
        foreach (UiElement item in Children)
        {
            Vector2 measured = LayoutChild(item, new(0, constraints.MaxWidth, 0, Math.Max(0, constraints.MaxHeight - size.Y)));
            SetChildOffset(item, new(0, size.Y));
            size = new(Math.Max(size.X, measured.X), size.Y + measured.Y);
        }

        return constraints.Constrain(size);
    }

    private static bool Eligible(MenuItemElement item) => item.Widget.IsVisible && item.Widget.IsEnabled
        && item.Widget.Focusable && item.Widget.HitTestVisible && item.PresenceState == UiPresenceState.Visible;
}
