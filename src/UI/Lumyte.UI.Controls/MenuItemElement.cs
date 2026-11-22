using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;
using ItemWidget = Lumyte.UI.Controls.Compose.Definitions.MenuItem;

namespace Lumyte.UI.Controls;

/// <summary>Owns accepted checking and one generation-safe submenu request.</summary>
public class MenuItemElement : UiElement<ItemWidget>
{
    private readonly UiActivationRecognizer _activation = new();
    private readonly PopupReservation _popup = new();

    /// <summary>Initializes a new instance of the <see cref="MenuItemElement"/> class.</summary>
    /// <param name="widget">The passive declaration.</param>
    protected internal MenuItemElement(ItemWidget widget)
        : base(widget)
    {
        IsChecked = widget.IsChecked;
    }

    /// <summary>Gets a value indicating whether this surface-local item is checked.</summary>
    public bool IsChecked { get; private set; }

    /// <summary>Gets a value indicating whether a submenu is requested or open.</summary>
    public bool IsExpanded => _popup.IsOpen;

    /// <inheritdoc/>
    protected override bool IsTabStop => ReferenceEquals(Menu?.TabStop, this);

    /// <inheritdoc/>
    protected override bool IsAutomationPressed => _activation.IsPressed;

    /// <inheritdoc/>
    protected override bool HasAutomationActivePress => _activation.HasActivePress;

    /// <inheritdoc/>
    protected override bool? AutomationChecked => Widget.IsCheckable ? IsChecked : null;

    /// <inheritdoc/>
    protected override bool? AutomationExpanded => Widget.CreateSubmenu is not null ? IsExpanded : null;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => base.AutomationActions | UiAutomationActions.Click
        | (Widget.IsCheckable ? UiAutomationActions.Check : 0)
        | (Widget.CreateSubmenu is not null ? UiAutomationActions.ExpandCollapse : 0);

    private MenuElement? Menu => Parent as MenuElement;

    /// <inheritdoc/>
    protected override UiInputResult OnProbePointerDown(Vector2 position) => IsInsideInputBody(position) ? UiInputResult.Handled : UiInputResult.Unhandled;

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (context.Input is UiFocusChangedInput { IsFocused: true })
        {
            Menu?.FocusItem(this);
        }

        if (context.Input is UiCheckInput check && Widget.IsCheckable)
        {
            if (context.RequestFocus() && Surface is not null)
            {
                CommitCheck(check.IsChecked);
            }

            return UiInputResult.Handled;
        }

        if (context.Input is UiExpandInput expand && Widget.CreateSubmenu is not null)
        {
            if (!context.RequestFocus() || Surface is null)
            {
                return UiInputResult.StopPropagation;
            }

            if (expand.IsExpanded)
            {
                OpenSubmenu();
            }
            else
            {
                _popup.Close();
            }

            return UiInputResult.Handled;
        }

        if (context.Input is UiKeyInput { Key: Key.ArrowRight } key && Widget.CreateSubmenu is not null)
        {
            if (context.IsKeyCaptured)
            {
                return UiInputResult.Handled;
            }

            if (context.IsFocused && key.IsDown && !key.IsRepeat && key.Modifiers == UiModifiers.None && context.CaptureKey())
            {
                OpenSubmenu();
                return UiInputResult.Handled;
            }
        }

        UiInputResult disposition = context.Input is UiPointerInput { Phase: UiPointerPhase.Down } && context.PointerPosition is { } point ? OnProbePointerDown(point) : UiInputResult.Unhandled;
        return _activation.HandleInput(in context, disposition, Activate);
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureChanged(UiPointerCaptureState state) => _activation.PointerCaptureChanged(state);

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(ItemWidget next, UiElementUpdateContext context)
    {
        if (ReferenceEquals(next, Widget))
        {
            return null;
        }

        context.CancelInput();
        return () => IsChecked = next.IsChecked;
    }

    /// <inheritdoc/>
    protected override void OnWidgetUpdated(ItemWidget previous) => _popup.Close();

    /// <inheritdoc/>
    protected override void OnUnmounted() => _popup.Close();

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        float leading = Widget.IsCheckable || Widget.CreateSubmenu is not null ? 16 : 0;
        Vector2 size = new(Math.Min(24, constraints.MaxWidth), Math.Min(24, constraints.MaxHeight));
        foreach (UiElement child in Children)
        {
            Vector2 measured = LayoutChild(child, new(0, Math.Max(0, constraints.MaxWidth - leading), 0, constraints.MaxHeight));
            size = new(measured.X + leading, Math.Max(16, measured.Y));
            SetChildOffset(child, new(leading, 0));
        }

        return constraints.Constrain(size);
    }

    /// <inheritdoc/>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        if ((Widget.IsCheckable && IsChecked) || Widget.CreateSubmenu is not null)
        {
            float size = Math.Min(8, Math.Min(ContentSize.X, ContentSize.Y));
            canvas.FillRect(new(0, Math.Max(0, (ContentSize.Y - size) / 2), size, size), Widget.Foreground);
        }
    }

    private void Activate()
    {
        if (Widget.CreateSubmenu is not null)
        {
            OpenSubmenu();
            return;
        }

        bool changed = Widget.IsCheckable;
        if (changed)
        {
            IsChecked = !IsChecked;
            InvalidateDrawing();
        }

        // Queue cleanup before either callback: reentry/disposal/throw cannot skip it.
        Menu?.PopupHost?.ItemInvoked(this);
        Action? invoke = Widget.OnInvoke;
        Action<bool>? change = changed ? Widget.OnChanged : null;
        bool value = IsChecked;
        try
        {
            change?.Invoke(value);
        }
        finally
        {
            invoke?.Invoke();
        }
    }

    private void CommitCheck(bool value)
    {
        if (IsChecked == value)
        {
            return;
        }

        IsChecked = value;
        InvalidateDrawing();
        Menu?.PopupHost?.ItemInvoked(this);
        Widget.OnChanged?.Invoke(value);
    }

    private void OpenSubmenu()
    {
        if (Menu?.PopupHost is { } host && Widget.CreateSubmenu is { } create)
        {
            _popup.Open(host, this, Widget.PopupSize, create, false);
        }
    }
}
