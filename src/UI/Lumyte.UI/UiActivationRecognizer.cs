using Lumyte.Input.Abstractions;

namespace Lumyte.UI;

/// <summary>Recognizes one ordinary pointer or Enter/Space activation using surface-owned capture.</summary>
public sealed class UiActivationRecognizer
{
    private long? _pressedPointer;
    private bool _pointerInside;
    private Key? _pressedKey;

    /// <summary>Gets a value indicating whether the instance owns a key press or its pointer is inside.</summary>
    public bool IsPressed { get; private set; }

    /// <summary>Gets a value indicating whether a pointer or key series is owned.</summary>
    public bool HasActivePress => _pressedPointer is not null || _pressedKey is not null;

    /// <summary>Processes a synchronous normal input using prepared pointer disposition.</summary>
    /// <param name="context">The current delivery context.</param>
    /// <param name="pointerDownDisposition">The owner probe result.</param>
    /// <param name="activate">The callback resolving current configuration at recognition.</param>
    /// <returns>The ordinary routing disposition.</returns>
    public UiInputResult HandleInput(in UiInputContext context, UiInputResult pointerDownDisposition, Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        if (context.Input is not UiPointerInput pointer)
        {
            return HandleKeyInput(in context, activate);
        }

        switch (pointer.Phase)
        {
            case UiPointerPhase.Down:
                UiInputResult disposition = pointerDownDisposition;
                if (disposition != UiInputResult.Handled)
                {
                    return disposition;
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
                    activate();
                }

                return UiInputResult.Handled;
            case UiPointerPhase.Cancel when context.IsCaptured:
                return UiInputResult.Handled;
            default:
                return UiInputResult.Unhandled;
        }
    }

    /// <summary>Observes surface-owned pointer capture acquisition, movement or loss.</summary>
    /// <param name="state">The latest capture notification.</param>
    public void PointerCaptureChanged(UiPointerCaptureState state)
    {
        _pressedPointer = state.IsCaptured ? state.PointerId : null;
        _pointerInside = state.IsCaptured && state.IsInside;
        UpdatePressedState();
    }

    private UiInputResult HandleKeyInput(in UiInputContext context, Action activate)
    {
        if (context.Input is UiKeyCancelInput cancel)
        {
            if (context.IsKeyCaptured && _pressedKey == cancel.Key)
            {
                _pressedKey = null;
                UpdatePressedState();
                return UiInputResult.Handled;
            }

            return UiInputResult.Unhandled;
        }

        if (context.Input is UiFocusChangedInput { IsFocused: false })
        {
            _pressedKey = null;
            UpdatePressedState();
            return UiInputResult.Unhandled;
        }

        if (context.Input is not UiKeyInput key || key.Key is not (Lumyte.Input.Abstractions.Key.Enter or Lumyte.Input.Abstractions.Key.Space))
        {
            return UiInputResult.Unhandled;
        }

        if (key.IsDown)
        {
            if (!context.IsFocused || key.Modifiers != UiModifiers.None)
            {
                return UiInputResult.Unhandled;
            }

            if (key.IsRepeat)
            {
                return context.IsKeyCaptured ? UiInputResult.Handled : UiInputResult.Unhandled;
            }

            if (_pressedPointer is not null || _pressedKey is not null)
            {
                return UiInputResult.Handled;
            }

            if (!context.CaptureKey())
            {
                return UiInputResult.Unhandled;
            }

            _pressedKey = key.Key;
            UpdatePressedState();
            return UiInputResult.Handled;
        }

        if (context.IsKeyCaptured && _pressedKey == key.Key)
        {
            _pressedKey = null;
            UpdatePressedState();
            if (context.IsFocused)
            {
                activate();
            }

            return UiInputResult.Handled;
        }

        return UiInputResult.Unhandled;
    }

    private void UpdatePressedState() => IsPressed = _pressedKey is not null || _pointerInside;
}
