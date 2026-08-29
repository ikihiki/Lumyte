using Lumyte.Input;
using Lumyte.Platform.SilkNet;

using Xunit;

namespace Lumyte.Platform.SilkNet.Tests;

public sealed class SilkConversionsTests
{
    [Theory]
    [InlineData(Silk.NET.Input.Key.Number0, Key.D0)]
    [InlineData(Silk.NET.Input.Key.Keypad4, Key.NumPad4)]
    [InlineData(Silk.NET.Input.Key.ControlLeft, Key.LeftControl)]
    [InlineData(Silk.NET.Input.Key.A, Key.A)]
    public void KeysMapToPortableNames(Silk.NET.Input.Key native, Key expected)
    {
        Key actual = SilkInputConversions.ToLumyte(native);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(Silk.NET.Input.ButtonName.A, GamepadButtons.South)]
    [InlineData(Silk.NET.Input.ButtonName.LeftBumper, GamepadButtons.LeftShoulder)]
    [InlineData(Silk.NET.Input.ButtonName.Back, GamepadButtons.View)]
    [InlineData(Silk.NET.Input.ButtonName.DPadRight, GamepadButtons.DPadRight)]
    public void GamepadButtonsMapToPhysicalPositions(
        Silk.NET.Input.ButtonName native,
        GamepadButtons expected)
    {
        GamepadButtons actual = SilkInputConversions.ToLumyte(native);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Minimized)]
    [InlineData(WindowState.Maximized)]
    public void WindowStatesRoundTrip(WindowState expected)
    {
        WindowState actual = SilkConversions.FromSilk(SilkConversions.ToSilk(expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TextInputReportsUnavailableWithoutAKeyboard()
    {
        using var context = new SilkTextInputContext([]);

        Assert.False(context.IsAvailable);
        Assert.False(context.IsActive);
    }
}
