using Lumyte.Input.Abstractions;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A container with one content child and an ordered background slot.</summary>
        public partial class Button
        {
            private Key? _pressedKey;

            private UiInputResult HandleKeyInput(in UiInputContext context)
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
                        Action? callback = OnClick;
                        callback?.Invoke();
                    }

                    return UiInputResult.Handled;
                }

                return UiInputResult.Unhandled;
            }

            private void UpdatePressedState() => IsPressed = _pressedKey is not null || _pointerInside;
        }
    }
}
