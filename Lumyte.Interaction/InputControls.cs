using System.Numerics;

using Lumyte.Input;

namespace Lumyte.Interaction;

public static class InputControls
{
    public static InputControl<bool> Key(Key key) => new("keyboard", key.ToString());

    public static InputControl<bool> MouseButton(MouseButton button) => new("mouse", button.ToString());

    public static InputControl<bool> GamepadButton(GamepadButtons button) => new("gamepad", button.ToString());

    public static InputControl<Vector2> GamepadStick(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new("gamepad", name);
    }
}
