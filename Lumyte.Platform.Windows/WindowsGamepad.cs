using System.Numerics;

using Lumyte.Input;
using Windows.Win32;
using Windows.Win32.UI.Input.XboxController;

namespace Lumyte.Platform.Windows;

public sealed class WindowsGamepad(uint userIndex) : IGamepad
{
    public event EventHandler<GamepadStateChangedEventArgs>? StateChanged;

    public string Name { get; } = $"XInput Gamepad {userIndex + 1}";

    public GamepadState State { get; private set; }

    public bool SupportsVibration => true;

    public void SetVibration(GamepadVibration vibration)
    {
        GamepadVibration clamped = vibration.Clamp();
        var nativeVibration = new XINPUT_VIBRATION
        {
            wLeftMotorSpeed = (ushort)MathF.Round(clamped.LowFrequency * ushort.MaxValue),
            wRightMotorSpeed = (ushort)MathF.Round(clamped.HighFrequency * ushort.MaxValue),
        };

        uint result = PInvoke.XInputSetState(userIndex, in nativeVibration);
        if (result != 0)
        {
            throw new InvalidOperationException($"Could not set vibration for {Name}.");
        }
    }

    internal void Update(GamepadState state)
    {
        if (State == state)
        {
            return;
        }

        GamepadState previous = State;
        State = state;
        StateChanged?.Invoke(this, new(previous, state));
    }

    internal static GamepadState Convert(XINPUT_STATE nativeState)
    {
        XINPUT_GAMEPAD gamepad = nativeState.Gamepad;
        return new(
            ConvertButtons(gamepad.wButtons),
            new(NormalizeStick(gamepad.sThumbLX), NormalizeStick(gamepad.sThumbLY)),
            new(NormalizeStick(gamepad.sThumbRX), NormalizeStick(gamepad.sThumbRY)),
            gamepad.bLeftTrigger / 255f,
            gamepad.bRightTrigger / 255f);
    }

    private static float NormalizeStick(short value) => value < 0
        ? value / 32768f
        : value / 32767f;

    private static GamepadButtons ConvertButtons(XINPUT_GAMEPAD_BUTTON_FLAGS buttons)
    {
        GamepadButtons result = GamepadButtons.None;
        Add(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_A, GamepadButtons.South);
        Add(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_B, GamepadButtons.East);
        Add(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_X, GamepadButtons.West);
        Add(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_Y, GamepadButtons.North);
        Add(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_LEFT_SHOULDER, GamepadButtons.LeftShoulder);
        Add(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_RIGHT_SHOULDER, GamepadButtons.RightShoulder);
        Add(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_LEFT_THUMB, GamepadButtons.LeftStick);
        Add(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_RIGHT_THUMB, GamepadButtons.RightStick);
        Add(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_DPAD_UP, GamepadButtons.DPadUp);
        Add(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_DPAD_DOWN, GamepadButtons.DPadDown);
        Add(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_DPAD_LEFT, GamepadButtons.DPadLeft);
        Add(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_DPAD_RIGHT, GamepadButtons.DPadRight);
        Add(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_BACK, GamepadButtons.View);
        Add(XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_START, GamepadButtons.Menu);
        return result;

        void Add(XINPUT_GAMEPAD_BUTTON_FLAGS nativeButton, GamepadButtons button)
        {
            if ((buttons & nativeButton) != 0)
            {
                result |= button;
            }
        }
    }
}
