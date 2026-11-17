using Lumyte.Input.Abstractions;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

/// <summary>Owns accepted selection and active-item focus separately.</summary>
/// <typeparam name="T">The item type.</typeparam>
public class ListBoxElement<T> : ItemsRepeaterElement<T>
    where T : notnull
{
    private string? _activeKey;
    private string? _selectedKey;

    /// <summary>Initializes a new instance of the <see cref="ListBoxElement{T}"/> class.</summary>
    /// <param name="widget">The declaration.</param>
    protected internal ListBoxElement(Compose.Definitions.ListBox<T> widget)
        : base(widget)
    {
    }

    /// <summary>Gets the accepted selection.</summary>
    public string? SelectedKey
    {
        get => ((Compose.Definitions.ListBox<T>)Widget).SelectionValue is { } shared ? shared() : _selectedKey;
        private set => _selectedKey = value;
    }

    /// <summary>Gets the active navigation key, independently of selection.</summary>
    public string? ActiveKey => _activeKey;

    internal IEnumerable<CollectionItemElement> Options => Children.SelectMany(child => child.Children).SelectMany(child => child.Children).OfType<CollectionItemElement>();

    /// <inheritdoc/>
    protected override string? AutomationSelectedKey => SelectedKey;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.SelectOption;

    internal bool IsItemTabStop(CollectionItemElement item) => ReferenceEquals(Options.FirstOrDefault(option => option.Widget.Key == _activeKey && Eligible(option)) ?? Options.FirstOrDefault(Eligible), item);

    internal void Activate(CollectionItemElement item)
    {
        if (!Eligible(item))
        {
            return;
        }

        string key = item.Widget.Key!;
        _activeKey = key;
        var declaration = (Compose.Definitions.ListBox<T>)Widget;
        if (SelectedKey == key)
        {
            declaration.ItemInvoked?.Invoke(key);
            return;
        }

        SelectedKey = key;
        InvalidateDrawing();
        InvalidateInput();
        Action<string?>? callback = ((Compose.Definitions.ListBox<T>)Widget).OnSelectionChanged;
        if (declaration.ItemInvoked is { } invoked)
        {
            invoked(key);
        }
        else
        {
            callback?.Invoke(key);
        }
    }

    internal UiInputResult Navigate(CollectionItemElement current, in UiInputContext context)
    {
        if (context.Input is not UiKeyInput { IsDown: true, IsRepeat: false, Modifiers: UiModifiers.None } key
            || key.Key is not (Key.ArrowUp or Key.ArrowDown or Key.ArrowLeft or Key.ArrowRight or Key.Home or Key.End or Key.PageUp or Key.PageDown))
        {
            return UiInputResult.Unhandled;
        }

        if (!context.IsFocused)
        {
            return UiInputResult.Unhandled;
        }

        CollectionItemElement[] options = Options.Where(Eligible).ToArray();
        if (options.Length != 0)
        {
            int index = Array.IndexOf(options, current);
            int target = key.Key switch
            {
                Key.Home => 0,
                Key.End => options.Length - 1,
                Key.PageUp => index - 5,
                Key.PageDown => index + 5,
                Key.ArrowUp or Key.ArrowLeft => index - 1,
                _ => index + 1,
            };
            CollectionItemElement next = options[Math.Clamp(target, 0, options.Length - 1)];
            _activeKey = next.Widget.Key;
            InvalidateInput();
            Surface!.TryFocus(next);
        }

        return UiInputResult.Handled;
    }

    /// <inheritdoc/>
    protected override Widget WrapContent(Compose.Definitions.ItemsRepeater<T> declaration, Widget content)
    {
        var scroll = new Lumyte.UI.Compose.Definitions.ScrollView { Orientation = declaration.Orientation };
        scroll.Children.Initialize(content);
        return scroll;
    }

    /// <inheritdoc/>
    protected override Widget WrapItem(Compose.Definitions.ItemsRepeater<T> declaration, T item, string key, Widget content)
    {
        var list = (Compose.Definitions.ListBox<T>)declaration;
        var option = new CollectionItem
        {
            Key = key,
            Orientation = declaration.Orientation,
            IsEnabled = list.ItemEnabled?.Invoke(item) ?? true,
            IsVisible = list.ItemVisible?.Invoke(item) ?? true,
            Selected = () => SelectedKey == key,
            SelectionOwner = () => AutomationId,
            TabStop = IsItemTabStop,
            Activate = Activate,
            Navigate = Navigate,
        };
        option.Content.Initialize(content);
        return option;
    }

    /// <inheritdoc/>
    protected override Action? PrepareItems(Compose.Definitions.ItemsRepeater<T> declaration, T[] items, string[] keys)
    {
        string? selection = ((Compose.Definitions.ListBox<T>)declaration).SelectedKey;
        string? anchor = null;
        float anchorDelta = 0;
        if (Children.FirstOrDefault() is ScrollViewElement oldScroll)
        {
            bool vertical = Widget.Orientation == UiOrientation.Vertical;
            CollectionItemElement? first = Options.FirstOrDefault(option => (vertical ? option.Bounds.Y + option.Size.Y : option.Bounds.X + option.Size.X) > oldScroll.Controller.Offset);
            if (first is not null && keys.Contains(first.Widget.Key, StringComparer.Ordinal))
            {
                anchor = first.Widget.Key;
                anchorDelta = oldScroll.Controller.Offset - (vertical ? first.Bounds.Y : first.Bounds.X);
            }
        }

        if (!keys.Contains(selection, StringComparer.Ordinal))
        {
            selection = null;
        }

        long focusRequest = Surface?.FocusRequest ?? 0;
        string? focusedKey = Surface?.FocusedElement is CollectionItemElement focused && Options.Contains(focused) ? focused.Widget.Key : null;
        string? active = keys.Contains(_activeKey, StringComparer.Ordinal) ? _activeKey : selection;
        return () =>
        {
            SelectedKey = selection;
            _activeKey = active;
            if (focusedKey is not null)
            {
                CollectionItemElement? restore = Options.FirstOrDefault(option => option.Widget.Key == focusedKey && option.Widget.IsEnabled && option.Widget.IsVisible && option.Widget.HitTestVisible);
                if (restore is not null)
                {
                    Surface!.QueueItemFocus(restore, focusRequest);
                }
            }

            if (Children[0] is ScrollViewElement scroll)
            {
                scroll.Controller.ItemAnchorResolver = anchor is null ? null : () =>
                {
                    CollectionItemElement? item = Options.FirstOrDefault(option => option.Widget.Key == anchor);
                    return item is null ? null : (Widget.Orientation == UiOrientation.Vertical ? item.Bounds.Y : item.Bounds.X) + anchorDelta;
                };
            }
        };
    }

    private static bool Eligible(CollectionItemElement item) => item.Widget.IsEnabled && item.Widget.IsVisible && item.Widget.HitTestVisible && item.Widget.Focusable && item.PresenceState == UiPresenceState.Visible;
}
