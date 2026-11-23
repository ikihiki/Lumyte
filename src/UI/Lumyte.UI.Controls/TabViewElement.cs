using System.Numerics;
using Lumyte.Input.Abstractions;
using TabViewWidget = Lumyte.UI.Controls.Compose.Definitions.TabView;

namespace Lumyte.UI.Controls;

/// <summary>Owns accepted single selection and roving header focus.</summary>
public class TabViewElement : UiElement<TabViewWidget>
{
    private TabItemElement? _activeHeader;

    /// <summary>Initializes a new instance of the <see cref="TabViewElement"/> class.</summary>
    /// <param name="widget">The initial declaration.</param>
    protected internal TabViewElement(TabViewWidget widget)
        : base(widget)
    {
        SelectedKey = widget.SelectedKey;
    }

    /// <summary>Gets the surface-local accepted key.</summary>
    public string? SelectedKey { get; private set; }

    internal TabItemElement? TabStop => _activeHeader is { } active && Options.Contains(active) && Eligible(active)
        ? active : Options.FirstOrDefault(option => option.IsSelected && Eligible(option)) ?? Options.FirstOrDefault(Eligible);

    /// <inheritdoc/>
    protected override bool CanReceiveFocus => false;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.SelectOption;

    /// <inheritdoc/>
    protected override string? AutomationSelectedKey => SelectedKey;

    private IEnumerable<TabItemElement> Options => GetChildren(Widget.Children).Cast<TabItemElement>();

    internal void FocusHeader(TabItemElement option)
    {
        _activeHeader = option;
        InvalidateInput();
    }

    internal void Select(TabItemElement option)
    {
        _activeHeader = option;
        if (string.Equals(SelectedKey, option.Widget.Key, StringComparison.Ordinal))
        {
            return;
        }

        string key = option.Widget.Key!;
        SelectedKey = key;
        foreach (TabItemElement tab in Options)
        {
            tab.SelectionChanged();
        }

        InvalidateLayout();
        InvalidateDrawing();
        UiSurface? surface = Surface;
        surface?.ContentPresentationChanged(option, surface.FocusRequest);
        Action<string?>? callback = Widget.OnSelectionChanged;
        callback?.Invoke(key);
    }

    internal UiInputResult Navigate(TabItemElement current, in UiInputContext context)
    {
        if (context.Input is not UiKeyInput { IsDown: true, IsRepeat: false, Modifiers: UiModifiers.None } key
            || key.Key is not (Key.ArrowLeft or Key.ArrowRight or Key.ArrowUp or Key.ArrowDown or Key.Home or Key.End))
        {
            return UiInputResult.Unhandled;
        }

        TabItemElement[] options = Options.Where(Eligible).ToArray();
        if (options.Length == 0)
        {
            return UiInputResult.Handled;
        }

        int index = Array.IndexOf(options, current);
        int next = key.Key == Key.Home ? 0 : key.Key == Key.End ? options.Length - 1
            : (index + (key.Key is Key.ArrowLeft or Key.ArrowUp ? -1 : 1) + options.Length) % options.Length;
        TabItemElement target = options[next];
        UiSurface? surface = Surface;
        if (surface is not null && surface.TryFocus(target) && ReferenceEquals(Surface, surface) && ReferenceEquals(target.Surface, surface) && ReferenceEquals(surface.FocusedElement, target))
        {
            Select(target);
        }

        return UiInputResult.Handled;
    }

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(TabViewWidget next, UiElementUpdateContext context)
    {
        string? selection = ReferenceEquals(next, Widget) ? SelectedKey : next.SelectedKey;
        bool exists = context.GetChildren(next.Children).Any(child => string.Equals(child.Widget.Key, selection, StringComparison.Ordinal));
        long request = Surface?.FocusRequest ?? 0;
        bool ownedFocus = Surface?.FocusedElement is { } previousFocus && Options.Any(tab => tab.EnumerateSubtree().Contains(previousFocus));
        return () =>
        {
            SelectedKey = exists ? selection : null;
            foreach (TabItemElement tab in Options)
            {
                tab.SelectionChanged();
            }

            InvalidateLayout();
            InvalidateInput();
            TabItemElement? fallback = Options.FirstOrDefault(option => option.IsSelected && Eligible(option)) ?? TabStop;
            if (ownedFocus && Surface?.FocusedElement is { } focused && !AcceptsInput(focused) && fallback is not null)
            {
                Surface.QueueItemFocus(fallback, request);
            }
        };
    }

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        Vector2 size = Vector2.Zero;
        foreach (TabItemElement tab in Options)
        {
            // Every header position depends on visible sibling membership.
            tab.InvalidateLayoutCache();
            Vector2 measured = LayoutChild(tab, constraints.Loosen());
            SetChildOffset(tab, Vector2.Zero);
            size = Vector2.Max(size, measured);
        }

        return constraints.Constrain(size);
    }

    private static bool AcceptsInput(UiElement target)
    {
        for (UiElement? node = target; node is not null; node = node.Parent)
        {
            if (!UiInputGeometry.AcceptsInput(node))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Eligible(TabItemElement tab) => tab.Widget.Focusable && tab.Widget.IsEnabled && tab.Widget.IsVisible
        && tab.Widget.HitTestVisible && tab.PresenceState == UiPresenceState.Visible;
}
