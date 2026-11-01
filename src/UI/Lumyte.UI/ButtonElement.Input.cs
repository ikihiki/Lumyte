namespace Lumyte.UI;

/// <summary>Implements persistent button behavior independently of replaceable declarations.</summary>
public partial class ButtonElement
{
    private long? _pressedPointer;
    private bool _pointerInside;

    /// <summary>Gets a value indicating whether the instance owns a key press or its pointer is inside.</summary>
    public bool IsPressed { get; private set; }

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (context.Input is not UiPointerInput pointer)
        {
            return HandleKeyInput(in context);
        }

        switch (pointer.Phase)
        {
            case UiPointerPhase.Down:
                if (!context.IsInside)
                {
                    return UiInputResult.StopPropagation;
                }

                if (_pressedPointer is not null || _pressedKey is not null)
                {
                    return UiInputResult.Handled;
                }

                if (!context.CapturePointer())
                {
                    return UiInputResult.StopPropagation;
                }

                context.RequestFocus();
                return UiInputResult.Handled;
            case UiPointerPhase.Move when context.IsCaptured:
                _pointerInside = context.IsInside;
                UpdatePressedState();
                return UiInputResult.Handled;
            case UiPointerPhase.Up when context.IsCaptured:
                if (context.IsInside)
                {
                    Action? callback = Widget.OnClick;
                    callback?.Invoke();
                }

                return UiInputResult.Handled;
            case UiPointerPhase.Cancel when context.IsCaptured:
                return UiInputResult.Handled;
            default:
                return UiInputResult.Unhandled;
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureChanged(UiPointerCaptureState state)
    {
        _pressedPointer = state.IsCaptured ? state.PointerId : null;
        _pointerInside = state.IsCaptured && state.IsInside;
        UpdatePressedState();
    }

    /// <inheritdoc/>
    protected override IEnumerable<UiElement> GetInputChildren() => GetChildren(Widget.Children);
}
