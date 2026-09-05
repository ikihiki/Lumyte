using System.Numerics;

using Lumyte.Input;

namespace Lumyte.Interaction;

public static class InputControls
{
    public static InputControl<bool> Key(Key key) => new("keyboard", key.ToString());

    public static InputControl<bool> MouseButton(MouseButton button) => new("mouse", button.ToString());

    public static InputControl<Vector2> MouseDelta { get; } = new("mouse", "Delta");

    public static InputControl<Vector2> MouseRawDelta { get; } = new("mouse", "RawDelta");

    public static InputControl<Vector2> MouseWheel { get; } = new("mouse", "Wheel");

    public static InputControl<bool> GamepadButton(GamepadButtons button, int? player = null) =>
        new(GamepadDevice(player), button.ToString());

    public static InputControl<Vector2> GamepadLeftStick(int? player = null) =>
        new(GamepadDevice(player), "LeftStick");

    public static InputControl<Vector2> GamepadRightStick(int? player = null) =>
        new(GamepadDevice(player), "RightStick");

    public static InputControl<float> GamepadLeftTrigger(int? player = null) =>
        new(GamepadDevice(player), "LeftTrigger");

    public static InputControl<float> GamepadRightTrigger(int? player = null) =>
        new(GamepadDevice(player), "RightTrigger");

    private static string GamepadDevice(int? player) =>
        player is int index ? $"gamepad:{index}" : "gamepad";
}
