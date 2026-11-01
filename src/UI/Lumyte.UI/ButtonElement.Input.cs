namespace Lumyte.UI;

/// <summary>Implements persistent button behavior independently of replaceable declarations.</summary>
public partial class ButtonElement
{
    private long? _pressedPointer;

    /// <summary>Gets a value indicating whether an owned pointer is inside the prepared input region.</summary>
    public bool IsPressed { get; private set; }

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (context.Input is not UiPointerInput pointer)
        {
            return UiInputResult.Unhandled;
        }

        switch (pointer.Phase)
        {
            case UiPointerPhase.Down:
                if (!context.IsInside)
                {
                    return UiInputResult.StopPropagation;
                }

                if (_pressedPointer is not null)
                {
                    return UiInputResult.Handled;
                }

                return context.CapturePointer() ? UiInputResult.Handled : UiInputResult.StopPropagation;
            case UiPointerPhase.Move when context.IsCaptured:
                IsPressed = context.IsInside;
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
        IsPressed = state.IsCaptured && state.IsInside;
    }

    /// <inheritdoc/>
    protected override IEnumerable<UiElement> GetInputChildren() => GetChildren(Widget.Children);
}
