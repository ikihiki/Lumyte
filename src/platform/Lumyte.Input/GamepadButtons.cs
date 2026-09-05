namespace Lumyte.Input;

[Flags]
public enum GamepadButtons
{
    None = 0,
    South = 1 << 0,
    East = 1 << 1,
    West = 1 << 2,
    North = 1 << 3,
    LeftShoulder = 1 << 4,
    RightShoulder = 1 << 5,
    LeftStick = 1 << 6,
    RightStick = 1 << 7,
    DPadUp = 1 << 8,
    DPadDown = 1 << 9,
    DPadLeft = 1 << 10,
    DPadRight = 1 << 11,
    View = 1 << 12,
    Menu = 1 << 13,
    Guide = 1 << 14,
}
