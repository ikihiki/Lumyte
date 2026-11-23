using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Shares ordinary activation and retained, parent-gated panel content.</summary>
/// <typeparam name="TWidget">The passive declaration.</typeparam>
/// <param name="widget">The initial declaration.</param>
public abstract class HeaderPanelElement<TWidget>(TWidget widget) : UiElement<TWidget>(widget)
    where TWidget : Widget
{
    private readonly UiActivationRecognizer _activation = new();
    private Vector2 _headerSize;
    private Vector2 _headerOffset;

    /// <summary>Gets the header declaration slot.</summary>
    protected abstract WidgetChildren HeaderSlot { get; }

    /// <summary>Gets the retained content declaration slot.</summary>
    protected abstract WidgetChildren ContentSlot { get; }

    /// <summary>Gets a value indicating whether content is presented.</summary>
    protected abstract bool ContentActive { get; }

    /// <inheritdoc/>
    protected override bool IsAutomationPressed => _activation.IsPressed;

    /// <inheritdoc/>
    protected override bool HasAutomationActivePress => _activation.HasActivePress;

    /// <inheritdoc/>
    protected override Lumyte.Graphics.Drawing2D.Rect2D AutomationActionRegion => new(_headerOffset.X, _headerOffset.Y, _headerSize.X, _headerSize.Y);

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.Click | (Widget.Focusable ? UiAutomationActions.Focus : 0);

    internal void SetHeaderGeometry(Vector2 size, Vector2 offset)
    {
        _headerSize = size;
        _headerOffset = offset;
    }

    /// <inheritdoc/>
    protected override bool IsChildActive(UiElement child) => !GetChildren(ContentSlot).Contains(child) || ContentActive;

    /// <inheritdoc/>
    protected override UiInputResult OnProbePointerDown(Vector2 localPosition) => localPosition.X >= _headerOffset.X && localPosition.Y >= _headerOffset.Y
        && localPosition.X < _headerOffset.X + _headerSize.X && localPosition.Y < _headerOffset.Y + _headerSize.Y && IsInsideInputBody(localPosition)
        ? UiInputResult.Handled : UiInputResult.Unhandled;

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (context.Input is UiKeyInput && !context.IsFocused && !context.IsKeyCaptured)
        {
            return UiInputResult.Unhandled;
        }

        UiInputResult disposition = context.Input is UiPointerInput { Phase: UiPointerPhase.Down } && context.PointerPosition is { } position ? OnProbePointerDown(position) : UiInputResult.Unhandled;
        bool? inside = context.PointerPosition is { } at ? OnProbePointerDown(at) == UiInputResult.Handled : null;
        return _activation.HandleInput(in context, disposition, Activate, inside);
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureChanged(UiPointerCaptureState state) => _activation.PointerCaptureChanged(state with { IsInside = state.IsInside && OnProbePointerDown(state.Position - InputOrigin) == UiInputResult.Handled });

    /// <summary>Accepts one recognized header activation.</summary>
    protected abstract void Activate();

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        _headerSize = new(Math.Min(24, constraints.MaxWidth), Math.Min(24, constraints.MaxHeight));
        foreach (UiElement header in GetChildren(HeaderSlot))
        {
            _headerSize = LayoutChild(header, constraints.Loosen());
            SetChildOffset(header, Vector2.Zero);
        }

        Vector2 size = _headerSize;
        if (ContentActive)
        {
            foreach (UiElement content in GetChildren(ContentSlot))
            {
                Vector2 measured = LayoutChild(content, new(0, constraints.MaxWidth, 0, Math.Max(0, constraints.MaxHeight - _headerSize.Y)));
                SetChildOffset(content, new(0, _headerSize.Y));
                size = new(Math.Max(size.X, measured.X), size.Y + measured.Y);
            }
        }

        return constraints.Constrain(size);
    }
}
