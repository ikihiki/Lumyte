using Lumyte.Input;

namespace Lumyte.Platform.SilkNet;

internal static class SilkInputConversions
{
    public static Key ToLumyte(Silk.NET.Input.Key key)
    {
        string name = key.ToString();
        if (Enum.TryParse(name, out Key result))
        {
            return result;
        }

        return name switch
        {
            "Number0" => Key.D0,
            "Number1" => Key.D1,
            "Number2" => Key.D2,
            "Number3" => Key.D3,
            "Number4" => Key.D4,
            "Number5" => Key.D5,
            "Number6" => Key.D6,
            "Number7" => Key.D7,
            "Number8" => Key.D8,
            "Number9" => Key.D9,
            "Keypad0" => Key.NumPad0,
            "Keypad1" => Key.NumPad1,
            "Keypad2" => Key.NumPad2,
            "Keypad3" => Key.NumPad3,
            "Keypad4" => Key.NumPad4,
            "Keypad5" => Key.NumPad5,
            "Keypad6" => Key.NumPad6,
            "Keypad7" => Key.NumPad7,
            "Keypad8" => Key.NumPad8,
            "Keypad9" => Key.NumPad9,
            "SuperLeft" => Key.LeftSuper,
            "SuperRight" => Key.RightSuper,
            "ControlLeft" => Key.LeftControl,
            "ControlRight" => Key.RightControl,
            "ShiftLeft" => Key.LeftShift,
            "ShiftRight" => Key.RightShift,
            "AltLeft" => Key.LeftAlt,
            "AltRight" => Key.RightAlt,
            _ => Key.Unknown,
        };
    }

    public static MouseButton ToLumyte(Silk.NET.Input.MouseButton button) => button.ToString() switch
    {
        "Left" => MouseButton.Left,
        "Middle" => MouseButton.Middle,
        "Right" => MouseButton.Right,
        "Button4" => MouseButton.Button4,
        "Button5" => MouseButton.Button5,
        _ => MouseButton.Left,
    };

    public static GamepadButtons ToLumyte(Silk.NET.Input.ButtonName button) => button switch
    {
        Silk.NET.Input.ButtonName.A => GamepadButtons.South,
        Silk.NET.Input.ButtonName.B => GamepadButtons.East,
        Silk.NET.Input.ButtonName.X => GamepadButtons.West,
        Silk.NET.Input.ButtonName.Y => GamepadButtons.North,
        Silk.NET.Input.ButtonName.LeftBumper => GamepadButtons.LeftShoulder,
        Silk.NET.Input.ButtonName.RightBumper => GamepadButtons.RightShoulder,
        Silk.NET.Input.ButtonName.LeftStick => GamepadButtons.LeftStick,
        Silk.NET.Input.ButtonName.RightStick => GamepadButtons.RightStick,
        Silk.NET.Input.ButtonName.DPadUp => GamepadButtons.DPadUp,
        Silk.NET.Input.ButtonName.DPadDown => GamepadButtons.DPadDown,
        Silk.NET.Input.ButtonName.DPadLeft => GamepadButtons.DPadLeft,
        Silk.NET.Input.ButtonName.DPadRight => GamepadButtons.DPadRight,
        Silk.NET.Input.ButtonName.Back => GamepadButtons.View,
        Silk.NET.Input.ButtonName.Start => GamepadButtons.Menu,
        Silk.NET.Input.ButtonName.Home => GamepadButtons.Guide,
        _ => GamepadButtons.None,
    };
}
