using System.Numerics;
using Lumyte.Input;
using Lumyte.Input.Abstractions;
using Lumyte.Input.Actions;
using Xunit;

namespace Lumyte.UI.IntegrationTests;

/// <summary>Checks action recognition selects the physical owner and scopes cancellation.</summary>
public sealed class WindowActionTests
{
    /// <summary>Checks focus loss and a button cancellation preserve unrelated window/key/button state.</summary>
    [Fact]
    public void WindowAndButtonCancellationDoNotResetOtherControls()
    {
        var source = new OrderedFixture(InputDeviceKind.Keyboard, InputDeviceKind.Mouse);
        using var input = new InputSystem([source]);
        InputWindowId a = new(Guid.NewGuid());
        InputWindowId b = new(Guid.NewGuid());
        var profile = new ActionProfile(
            [new("jump", ActionValueKind.Button), new("left", ActionValueKind.Button), new("right", ActionValueKind.Button)],
            [new("a", "jump", "game", InputControl.ForKey(Key.A), Vector2.One), new("b", "jump", "game", InputControl.ForKey(Key.B), Vector2.One), new("l", "left", "game", new(InputControlKind.MouseButton, (int)MouseButton.Left), Vector2.One), new("r", "right", "game", new(InputControlKind.MouseButton, (int)MouseButton.Right), Vector2.One)],
            [new("game")],
            []);
        using var first = new ActionSystem(profile, null, null, a);
        using var second = new ActionSystem(profile, null, null, b);
        first.QueueContextActivation("game");
        second.QueueContextActivation("game");
        var records = new List<InputRecord>();
        input.Recorded += records.Add;
        source.Add((0, a, new FocusData(true)), (0, b, new FocusData(true)), (0, a, new KeyData(Key.A, true, false)), (0, b, new KeyData(Key.B, true, false)), (1, b, new MouseButtonData(MouseButton.Left, true)), (1, b, new MouseButtonData(MouseButton.Right, true)));
        input.Update();
        first.Advance(records.ToArray(), input.ElapsedTime);
        second.Advance(records.ToArray(), input.ElapsedTime);
        Assert.Equal(Vector2.UnitX, first.GetState("game", "jump").Value);
        Assert.Equal(Vector2.UnitX, second.GetState("game", "jump").Value);
        records.Clear();
        source.Add((0, a, new FocusData(false)), (1, b, new MouseButtonCanceledData(MouseButton.Left)));
        input.Update();
        first.Advance(records.ToArray(), input.ElapsedTime);
        second.Advance(records.ToArray(), input.ElapsedTime);
        Assert.Equal(Vector2.Zero, first.GetState("game", "jump").Value);
        Assert.Equal(Vector2.UnitX, second.GetState("game", "jump").Value);
        Assert.Equal(Vector2.Zero, second.GetState("game", "left").Value);
        Assert.Equal(Vector2.UnitX, second.GetState("game", "right").Value);
    }
}
