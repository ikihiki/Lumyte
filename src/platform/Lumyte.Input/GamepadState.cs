using System.Numerics;

namespace Lumyte.Input;

public readonly record struct GamepadState(
    GamepadButtons Buttons,
    Vector2 LeftStick,
    Vector2 RightStick,
    float LeftTrigger,
    float RightTrigger)
{
    public bool IsPressed(GamepadButtons button) => (Buttons & button) == button;
}
