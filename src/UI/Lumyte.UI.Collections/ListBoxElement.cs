using Lumyte.Input.Abstractions;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

/// <summary>Owns accepted selection and active-item focus separately.</summary>
/// <typeparam name="T">The item type.</typeparam>
public class ListBoxElement<T> : ItemsRepeaterElement<T>, IUiItemViewport
    where T : notnull
{
    private (int Start, int End) _realizedRange;
    private bool _preparingRange;
    private long _itemsGeneration;
    private int _ownerThread;
    private Dictionary<string, int> _keyIndices = new(StringComparer.Ordinal);
    private (string Key, long Request)? _pendingNavigation;
    private UiElement? _pinnedFocus;
    private RangePreparation? _rangePreparation;
    private string? _bringKey;
    private long _bringGeneration;
    private long _bringRequest;
    private long _preparedBringRequest;
    private ScrollController? _positionController;

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
    protected override bool UseAdoptedItems => _preparingRange;

    /// <inheritdoc/>
    protected override string? AutomationSelectedKey => SelectedKey;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.SelectOption;

    /// <summary>Reveals an adopted ordinal key without constructing preceding items.</summary>
    /// <param name="key">The nonempty model key.</param>
    /// <returns>Whether the key exists and can be revealed.</returns>
    public bool BringIntoView(string key) => QueueBringIntoView(key, null);

    /// <summary>Queues key reveal and rejects work from a replaced model generation.</summary>
    /// <param name="dispatcher">The borrowed owner dispatcher.</param>
    /// <param name="key">The adopted ordinal key.</param>
    /// <param name="cancellationToken">Cancels before owner execution.</param>
    /// <returns>The operation completion.</returns>
    public ValueTask BringIntoViewAsync(IUiDispatcher dispatcher, string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentException.ThrowIfNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();
        long generation = Volatile.Read(ref _itemsGeneration);
        ScrollController? controller = Volatile.Read(ref _positionController);
        long request = controller?.ReservePositionRequest() ?? 0;
        return dispatcher.InvokeAsync(
            () =>
            {
                if (generation != _itemsGeneration || controller is null || !ReferenceEquals(controller, _positionController)
                    || request != controller.PositionRequestVersion || !QueueBringIntoView(key, request))
                {
                    throw new InvalidOperationException("The item model or position request changed, or the key is unavailable.");
                }
            },
            cancellationToken);
    }

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

        if (((Compose.Definitions.ListBox<T>)Widget).Virtualization is not null)
        {
            NavigateVirtual(current, key.Key);
            return UiInputResult.Handled;
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

    internal override bool PrepareViewportContent()
    {
        _rangePreparation = PreparationSurface?.PreparedViewportContent(this)?.PreparationState as RangePreparation;
        var list = (Compose.Definitions.ListBox<T>)PreparationWidget;
        UiElement? expectedPin = _rangePreparation is { } prepared ? prepared.PinnedFocus : _pinnedFocus;
        if (list.Virtualization is not { } options || PreparationChildren.FirstOrDefault() is not ScrollViewElement scroll)
        {
            _rangePreparation = null;
            return false;
        }

        bool needsFocusPin = _rangePreparation is { Adopt: not null, RestoreKey: { } restoreKey, PinKey: null }
            && !PreparationChildren.SelectMany(child => child.PreparationChildren).SelectMany(child => child.PreparationChildren).Any(item => item.Widget.Key == restoreKey);
        bool moved = false;
        if (_bringKey is { } key && _bringGeneration == _itemsGeneration && _rangePreparation?.Adopt is null)
        {
            if (_bringRequest != scroll.Controller.PositionRequestVersion)
            {
                _bringKey = null;
            }
            else if (scroll.Controller.ProposedViewport > 0 && _keyIndices.TryGetValue(key, out int index))
            {
                float start = (float)((double)index * options.ItemExtent);
                float offset = scroll.Controller.ProposedOffset;
                float viewport = scroll.Controller.ProposedViewport;
                float target = start < offset ? start : (double)start + options.ItemExtent > (double)offset + viewport
                    ? (float)Math.Min(start, (double)start + options.ItemExtent - viewport) : offset;
                moved = scroll.Controller.StagePositionRequest(target);
                _preparedBringRequest = _bringRequest;
            }
        }

        if (!moved && !needsFocusPin && (options.Range(_rangePreparation?.Items.Length ?? Items.Count, scroll.Controller.ProposedOffset, (list.Orientation == UiOrientation.Vertical ? scroll.PreparedSize.X : scroll.PreparedSize.Y) > 0 ? scroll.Controller.ProposedViewport : 0) == (_rangePreparation?.Range ?? _realizedRange)
                && (expectedPin is null || ReferenceEquals(expectedPin, Surface?.FocusedElement) || expectedPin.HasPendingInputLifetime)))
        {
            _rangePreparation = null;
            return false;
        }

        // Force content preparation on the same declaration without changing public properties.
        _preparingRange = true;
        try
        {
            PreparationSurface!.RefreshElementContent(this);
        }
        finally
        {
            _preparingRange = false;
            _rangePreparation = null;
        }

        return true;
    }

    internal override void AcceptViewportPreparation()
    {
        if (_bringKey is not null && _bringGeneration == _itemsGeneration && _preparedBringRequest == _bringRequest)
        {
            _bringKey = null;
        }

        _preparedBringRequest = 0;
    }

    /// <inheritdoc/>
    protected override UiElementContent? BuildContent(Widget next, UiElementBuildContext context) => _preparingRange && _rangePreparation is { } prepared
        ? PrepareRealization((Compose.Definitions.ItemsRepeater<T>)next, prepared.Items, prepared.Keys) : base.BuildContent(next, context);

    /// <inheritdoc/>
    protected override Widget WrapContent(Compose.Definitions.ItemsRepeater<T> declaration, Widget content)
    {
        var scroll = new Lumyte.UI.Compose.Definitions.ScrollView { Orientation = declaration.Orientation, DeferredPosition = ((Compose.Definitions.ListBox<T>)declaration).Virtualization is not null };
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
            CollectionItemElement? first = Options.OrderBy(option => vertical ? option.Bounds.Y : option.Bounds.X).FirstOrDefault(option => (vertical ? option.Bounds.Y + option.Size.Y : option.Bounds.X + option.Size.X) > oldScroll.Controller.Offset);
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
        UiElement? focusedElement = Surface?.FocusedElement ?? Surface?.DeferredViewportFocus;
        if (((Compose.Definitions.ListBox<T>)declaration).Virtualization is not null)
        {
            while (focusedElement is not null && focusedElement is not CollectionItemElement)
            {
                focusedElement = focusedElement.Parent;
            }
        }

        string? focusedKey = focusedElement is CollectionItemElement focused && Options.Contains(focused) ? focused.Widget.Key : null;
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
                    var prepared = Surface?.PreparedViewportContent(this)?.PreparationState as RangePreparation;
                    int index = Array.IndexOf(keys, focusedKey);
                    bool reveal = prepared is null || (index >= prepared.Range.Start && index < prepared.Range.End);
                    Surface!.QueueItemFocus(restore, focusRequest, reveal);
                }
            }

            if (Children[0] is ScrollViewElement scroll)
            {
                if (((Compose.Definitions.ListBox<T>)declaration).Virtualization is not null)
                {
                    scroll.Controller.ItemAnchorResolver = null;
                    return;
                }

                scroll.Controller.ItemAnchorResolver = anchor is null ? null : () =>
                {
                    CollectionItemElement? item = Options.FirstOrDefault(option => option.Widget.Key == anchor);
                    return item is null ? null : (Widget.Orientation == UiOrientation.Vertical ? item.Bounds.Y : item.Bounds.X) + anchorDelta;
                };
            }
        };
    }

    /// <inheritdoc/>
    protected override UiElementContent PrepareRealization(Compose.Definitions.ItemsRepeater<T> declaration, T[] items, string[] keys)
    {
        var list = (Compose.Definitions.ListBox<T>)declaration;
        if (list.Virtualization is not { } options)
        {
            return base.PrepareRealization(declaration, items, keys);
        }

        float extent = options.Extent(items.Length);
        var scroll = PreparationChildren.FirstOrDefault() as ScrollViewElement;

        // A new model must not instantiate rows for the previous viewport on a simultaneous resize.
        float viewport = _preparingRange ? scroll?.Controller.ProposedViewport ?? 0 : 0;
        if (scroll is not null && (declaration.Orientation == UiOrientation.Vertical ? scroll.PreparedSize.X : scroll.PreparedSize.Y) <= 0)
        {
            viewport = 0;
        }

        float offset = Math.Clamp(scroll?.Controller.ProposedOffset ?? 0, 0, Math.Max(0, extent - viewport));
        Action? prepareGeometry = _preparingRange ? _rangePreparation?.PrepareGeometry : null;
        if (!_preparingRange && scroll is not null && Widget is Compose.Definitions.ListBox<T> { Virtualization: { } oldOptions } && Keys.Count != 0 && scroll.Controller.Viewport > 0)
        {
            int oldIndex = (int)Math.Clamp(Math.Floor((double)scroll.Controller.Offset / oldOptions.ItemExtent), 0, Keys.Count - 1);
            int newIndex = Array.IndexOf(keys, Keys[oldIndex]);
            if (newIndex >= 0)
            {
                double delta = scroll.Controller.Offset - ((double)oldIndex * oldOptions.ItemExtent);
                offset = (float)Math.Clamp(((double)newIndex * options.ItemExtent) + delta, 0, Math.Max(0, extent - viewport));
            }

            float anchorOffset = offset;
            prepareGeometry = () => scroll.Controller.StageItemAnchor(anchorOffset);
        }

        (int Start, int End) range = options.Range(items.Length, offset, viewport);
        Dictionary<string, Widget> previous = _preparingRange ? PreparationChildren.SelectMany(child => child.PreparationChildren).SelectMany(child => child.PreparationChildren).OfType<CollectionItemElement>().ToDictionary(item => item.Widget.Key!, item => (Widget)item.Widget, StringComparer.Ordinal) : new Dictionary<string, Widget>(StringComparer.Ordinal);
        var roots = new List<Widget>();
        var indices = new List<int>();
        var seen = new HashSet<Widget>(ReferenceEqualityComparer.Instance);
        for (int index = range.Start; index < range.End; index++)
        {
            Widget root;
            if (!previous.TryGetValue(keys[index], out root!))
            {
                Widget content = declaration.ItemTemplate!(items[index]) ?? throw new InvalidOperationException("An item template returned null.");
                content.ValidateFreshItemRoot();
                if (!seen.Add(content))
                {
                    throw new InvalidOperationException("Item templates must return disjoint roots.");
                }

                root = WrapItem(declaration, items[index], keys[index], content);
            }

            roots.Add(root);
            indices.Add(index);
        }

        string? restoreFocusKey = _preparingRange ? _rangePreparation?.RestoreKey : FocusItemKey(Surface?.FocusedElement ?? Surface?.DeferredViewportFocus);
        int restoreIndex = restoreFocusKey is null ? -1 : Array.IndexOf(keys, restoreFocusKey);
        if (restoreIndex < 0 || !list.IsVisible || !list.IsPresent || !IsContentActive
            || (list.ItemVisible?.Invoke(items[restoreIndex]) == false) || (list.ItemEnabled?.Invoke(items[restoreIndex]) == false))
        {
            restoreFocusKey = null;
        }

        string? freshPinKey = _preparingRange && _rangePreparation?.Adopt is not null ? _rangePreparation.PinKey : null;
        UiElement? pinnedFocus = null;

        // Keep at most the single focused item alive outside the viewport. Its normal
        // clipped geometry excludes pointer hits while editor preedit remains attached.
        UiElement? inputPin = _pinnedFocus?.HasPendingInputLifetime == true ? _pinnedFocus
            : Options.SelectMany(UiSurface.CollectElements).FirstOrDefault(element => element.HasPendingInputLifetime) ?? Surface?.FocusedElement;
        if (_preparingRange && inputPin is { } focused)
        {
            UiElement? owner = focused;
            while (owner is not null && owner is not CollectionItemElement)
            {
                owner = owner.Parent;
            }

            if (owner is CollectionItemElement pinned && previous.TryGetValue(pinned.Widget.Key!, out Widget? pinnedRoot) && !roots.Contains(pinnedRoot))
            {
                if (_keyIndices.TryGetValue(pinned.Widget.Key!, out int pinnedIndex))
                {
                    pinnedFocus = focused;
                    roots.Add(pinnedRoot);
                    indices.Add(pinnedIndex);
                }
            }
        }

        if (_preparingRange && _rangePreparation?.Adopt is not null && restoreFocusKey is not null && !roots.Any(root => root.Key == restoreFocusKey))
        {
            Widget root = declaration.ItemTemplate!(items[restoreIndex]) ?? throw new InvalidOperationException("An item template returned null.");
            root.ValidateFreshItemRoot();
            if (!seen.Add(root))
            {
                throw new InvalidOperationException("Item templates must return disjoint roots.");
            }

            roots.Add(WrapItem(declaration, items[restoreIndex], restoreFocusKey, root));
            indices.Add(restoreIndex);
            freshPinKey = restoreFocusKey;
            pinnedFocus = Surface?.FocusedElement ?? Surface?.DeferredViewportFocus;
        }

        if (restoreIndex >= range.Start && restoreIndex < range.End)
        {
            freshPinKey = null;
        }

        var panel = new VirtualItemPanel
        {
            Key = _preparingRange ? scroll?.PreparationChildren[0].Widget.Key : Guid.NewGuid().ToString("N"),
            Orientation = declaration.Orientation,
            Extent = extent,
            ItemExtent = options.ItemExtent,
            Indices = indices.ToArray(),
        };
        panel.Content.Initialize(roots.ToArray());
        Widget contentRoot = WrapContent(declaration, panel);
        if (_preparingRange)
        {
            Action? adopt = _rangePreparation?.Adopt;
            var result = new UiElementContent(contentRoot, () =>
            {
                adopt?.Invoke();
                _realizedRange = range;
                _pinnedFocus = freshPinKey is null ? pinnedFocus : Options.FirstOrDefault(item => item.Widget.Key == freshPinKey);
                if (_pendingNavigation is { } navigation)
                {
                    _pendingNavigation = null;
                    CollectionItemElement? target = Options.FirstOrDefault(item => item.Widget.Key == navigation.Key);
                    if (target is not null)
                    {
                        Surface!.QueueItemFocus(target, navigation.Request);
                    }
                }
            });
            result.FreshRealizations = true;
            result.PrepareLayout = prepareGeometry;
            result.PreparationState = new RangePreparation(items, keys, range, adopt, prepareGeometry, pinnedFocus, restoreFocusKey, freshPinKey);
            return result.WithItemOwnership(roots.ToArray());
        }

        var keyIndices = keys.Select((key, index) => (key, index)).ToDictionary(pair => pair.key, pair => pair.index, StringComparer.Ordinal);
        UiElementContent adoption = AdoptContent(declaration, items, keys, contentRoot);
        Action acceptModel = () =>
        {
            adoption.Commit?.Invoke();
            _realizedRange = range;
            _pinnedFocus = null;
            _pendingNavigation = null;
            Volatile.Write(ref _positionController, (Children[0] as ScrollViewElement)?.Controller);
            Volatile.Write(ref _itemsGeneration, _itemsGeneration + 1);
            _bringKey = null;
            _keyIndices = keyIndices;
            _ownerThread = Environment.CurrentManagedThreadId;
        };
        return new UiElementContent(contentRoot, acceptModel) { FreshRealizations = true, PrepareLayout = prepareGeometry, PreparationState = new RangePreparation(items, keys, range, acceptModel, prepareGeometry, null, restoreFocusKey, null) }.WithItemOwnership(roots.ToArray());
    }

    private static bool Eligible(CollectionItemElement item) => item.Widget.IsEnabled && item.Widget.IsVisible && item.Widget.HitTestVisible && item.Widget.Focusable && item.PresenceState == UiPresenceState.Visible;

    private bool QueueBringIntoView(string key, long? positionRequest)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (_ownerThread != 0 && _ownerThread != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Item operations require the mounted owner thread.");
        }

        if (Surface is null || ((Compose.Definitions.ListBox<T>)Widget).Virtualization is null
            || Children.FirstOrDefault() is not ScrollViewElement scroll)
        {
            return false;
        }

        if (!_keyIndices.TryGetValue(key, out int index))
        {
            return false;
        }

        if (((Compose.Definitions.ListBox<T>)Widget).ItemVisible?.Invoke(Items[index]) == false)
        {
            return false;
        }

        long request = positionRequest ?? scroll.Controller.ReservePositionRequest();
        if (request != scroll.Controller.PositionRequestVersion)
        {
            return false;
        }

        _bringKey = key;
        _bringGeneration = _itemsGeneration;
        _bringRequest = request;
        InvalidateLayout();
        InvalidateInput();
        return true;
    }

    private string? FocusItemKey(UiElement? focused)
    {
        while (focused is not null && focused is not CollectionItemElement)
        {
            focused = focused.Parent;
        }

        return focused is CollectionItemElement item && Options.Contains(item) ? item.Widget.Key : null;
    }

    private void NavigateVirtual(CollectionItemElement current, Lumyte.Input.Abstractions.Key key)
    {
        var declaration = (Compose.Definitions.ListBox<T>)Widget;
        int index = _keyIndices.GetValueOrDefault(current.Widget.Key!, -1);

        bool backwards = key is Lumyte.Input.Abstractions.Key.Home or Lumyte.Input.Abstractions.Key.ArrowUp or Lumyte.Input.Abstractions.Key.ArrowLeft or Lumyte.Input.Abstractions.Key.PageUp;
        int step = backwards ? -1 : 1;
        int remaining = key is Lumyte.Input.Abstractions.Key.PageUp or Lumyte.Input.Abstractions.Key.PageDown ? 5 : 1;
        if (key == Lumyte.Input.Abstractions.Key.Home)
        {
            index = -1;
            step = 1;
        }
        else if (key == Lumyte.Input.Abstractions.Key.End)
        {
            index = Items.Count;
            step = -1;
        }

        int target = -1;
        for (int candidate = index + step; candidate >= 0 && candidate < Items.Count; candidate += step)
        {
            if ((declaration.ItemEnabled?.Invoke(Items[candidate]) ?? true) && (declaration.ItemVisible?.Invoke(Items[candidate]) ?? true))
            {
                target = candidate;
                if (--remaining == 0)
                {
                    break;
                }
            }
        }

        if (target < 0)
        {
            return;
        }

        string keyToFocus = Keys[target];
        CollectionItemElement? realized = Options.FirstOrDefault(item => item.Widget.Key == keyToFocus);
        if (realized is not null)
        {
            _activeKey = keyToFocus;
            Surface!.TryFocus(realized);
            InvalidateInput();
            return;
        }

        if (BringIntoView(keyToFocus))
        {
            _activeKey = keyToFocus;
            long request = Surface!.ReserveItemFocusRequest();
            _pendingNavigation = (keyToFocus, request);
            InvalidateLayout();
            InvalidateInput();
        }
    }

    private sealed record RangePreparation(T[] Items, string[] Keys, (int Start, int End) Range, Action? Adopt, Action? PrepareGeometry, UiElement? PinnedFocus, string? RestoreKey, string? PinKey);
}
