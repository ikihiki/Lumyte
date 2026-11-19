using System.Numerics;
using Lumyte.Input.Abstractions;
using Lumyte.Signals;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

/// <summary>Owns a non-editable selector and one borrowed popup request.</summary>
/// <typeparam name="T">The item type.</typeparam>
public class ComboBoxElement<T> : ItemsRepeaterElement<T>
    where T : notnull
{
    private readonly UiActivationRecognizer _activation = new();
    private readonly Signal<long> _displayRevision = Signal.Create(0L);
    private IDisposable? _popup;
    private long _popupGeneration;

    /// <summary>Initializes a new instance of the <see cref="ComboBoxElement{T}"/> class.</summary>
    /// <param name="widget">The passive declaration.</param>
    protected internal ComboBoxElement(Compose.Definitions.ComboBox<T> widget)
        : base(widget)
    {
    }

    /// <summary>Gets the accepted selection.</summary>
    public string? SelectedKey { get; private set; }

    /// <summary>Gets a value indicating whether a popup request is active.</summary>
    public bool IsExpanded => _popup is not null;

    /// <inheritdoc/>
    protected override string? AutomationSelectedKey => SelectedKey;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.Click | UiAutomationActions.Focus;

    /// <inheritdoc/>
    protected override UiInputResult OnProbePointerDown(Vector2 localPosition) => IsInsideInputBody(localPosition) ? UiInputResult.Handled : UiInputResult.StopPropagation;

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (context.IsFocused && context.Input is UiKeyInput { IsDown: true, IsRepeat: false, Modifiers: UiModifiers.None, Key: Key.ArrowDown or Key.ArrowUp })
        {
            Open();
            return UiInputResult.Handled;
        }

        return _activation.HandleInput(in context, context.Input is UiPointerInput { Phase: UiPointerPhase.Down } && context.PointerPosition is { } point ? OnProbePointerDown(point) : UiInputResult.Unhandled, Open);
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureChanged(UiPointerCaptureState state) => _activation.PointerCaptureChanged(state);

    /// <inheritdoc/>
    protected override UiElementContent PrepareRealization(Compose.Definitions.ItemsRepeater<T> declaration, T[] items, string[] keys)
    {
        var combo = (Compose.Definitions.ComboBox<T>)declaration;
        ArgumentNullException.ThrowIfNull(combo.PopupHost);
        if (!combo.HasSelectedItemTemplate)
        {
            ArgumentNullException.ThrowIfNull(combo.SelectedItemTemplate);
        }

        if (!combo.HasEmptyTemplate)
        {
            ArgumentNullException.ThrowIfNull(combo.EmptyTemplate);
        }

        if (!float.IsFinite(combo.PopupSize.X) || !float.IsFinite(combo.PopupSize.Y) || combo.PopupSize.X <= 0 || combo.PopupSize.Y <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(declaration));
        }

        int index = Array.IndexOf(keys, combo.SelectedKey);
        string? initial = index >= 0 ? keys[index] : null;
        long preparedRevision = _displayRevision.Peek();
        Widget display = new ComboDisplay(() =>
        {
            long revision = _displayRevision.Value;
            string? selection = revision == preparedRevision ? initial : SelectedKey;
            int selected = Array.IndexOf(keys, selection);
            Widget result = selected >= 0 ? combo.RealizeSelectedItem(items[selected]) : combo.RealizeEmpty();
            result.ValidateFreshItemRoot();
            return result;
        });
        return AdoptContent(declaration, items, keys, display).WithItemOwnership(display);
    }

    /// <inheritdoc/>
    protected override Action? PrepareItems(Compose.Definitions.ItemsRepeater<T> declaration, T[] items, string[] keys)
    {
        string? selection = ((Compose.Definitions.ComboBox<T>)declaration).SelectedKey;
        if (!keys.Contains(selection, StringComparer.Ordinal))
        {
            selection = null;
        }

        return () =>
        {
            Close();
            SelectedKey = selection;
        };
    }

    /// <inheritdoc/>
    protected override void OnUnmounted() => Close();

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(Compose.Definitions.ItemsRepeater<T> next, UiElementUpdateContext context)
    {
        context.CancelInput();
        return null;
    }

    private void Open()
    {
        if (!IsMounted || IsExpanded)
        {
            return;
        }

        var declaration = (Compose.Definitions.ComboBox<T>)Widget;
        long generation = ++_popupGeneration;
        _popup = declaration.PopupHost!.Open(
            this,
            declaration.PopupSize,
            () =>
        {
            var list = new Compose.Definitions.ListBox<T>
            {
                Items = Items,
                KeySelector = declaration.KeySelector,

                // The popup list is an implementation detail, not the slot owner.
                ItemTemplate = declaration.ItemSlot ?? declaration.ItemTemplate,
                Virtualization = declaration.Virtualization,
                ItemEnabled = declaration.ItemEnabled,
                ItemVisible = declaration.ItemVisible,
                SelectedKey = SelectedKey,
                Height = declaration.PopupSize.Y,
                ItemInvoked = key => Commit(generation, key),
                SelectionValue = () => SelectedKey,
            };
            return list;
        },
            () =>
        {
            if (generation == _popupGeneration)
            {
                _popup = null;
            }
        });
    }

    private void Commit(long generation, string? key)
    {
        if (!IsMounted || generation != _popupGeneration || !Keys.Contains(key, StringComparer.Ordinal))
        {
            return;
        }

        bool changed = SelectedKey != key;
        SelectedKey = key;
        Close();
        InvalidateDrawing();
        try
        {
            _displayRevision.Value++;
        }
        finally
        {
            if (changed && IsMounted)
            {
                Action<string?>? callback = ((Compose.Definitions.ComboBox<T>)Widget).OnSelectionChanged;
                callback?.Invoke(key);
            }
        }
    }

    private void Close()
    {
        _popupGeneration++;
        IDisposable? popup = _popup;
        _popup = null;
        popup?.Dispose();
    }
}
