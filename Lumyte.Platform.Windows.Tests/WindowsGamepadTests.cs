using System.Numerics;

using Lumyte.Input;
using Windows.Win32.UI.Input.XboxController;
using Xunit;

namespace Lumyte.Platform.Windows.Tests;

public sealed class WindowsGamepadTests
{
    [Fact]
    public void ConvertsNativeStateToPortableState()
    {
        var nativeState = new XINPUT_STATE
        {
            Gamepad = new()
            {
                wButtons = XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_A
                    | XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_DPAD_LEFT,
                bLeftTrigger = 255,
                bRightTrigger = 128,
                sThumbLX = short.MinValue,
                sThumbLY = short.MaxValue,
                sThumbRX = 0,
                sThumbRY = 0,
            },
        };

        GamepadState state = WindowsGamepad.Convert(nativeState);

        Assert.True(state.IsPressed(GamepadButtons.South));
        Assert.True(state.IsPressed(GamepadButtons.DPadLeft));
        Assert.Equal(new Vector2(-1, 1), state.LeftStick);
        Assert.Equal(Vector2.Zero, state.RightStick);
        Assert.Equal(1, state.LeftTrigger);
        Assert.Equal(128 / 255f, state.RightTrigger);
    }

    [Fact]
    public void StateChangeIsNotifiedOnlyWhenValueChanges()
    {
        var gamepad = new WindowsGamepad(0);
        List<GamepadStateChangedEventArgs> changes = [];
        gamepad.StateChanged += (_, eventArgs) => changes.Add(eventArgs);
        var state = new GamepadState(
            GamepadButtons.Menu,
            new(0.25f, -0.5f),
            Vector2.Zero,
            0,
            1);

        gamepad.Update(state);
        gamepad.Update(state);

        GamepadStateChangedEventArgs change = Assert.Single(changes);
        Assert.Equal(default, change.Previous);
        Assert.Equal(state, change.Current);
        Assert.Equal(state, gamepad.State);
    }
}
