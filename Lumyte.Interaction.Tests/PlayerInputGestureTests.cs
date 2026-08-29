using System.Numerics;

using Lumyte.Core.Time;
using Lumyte.Input;

using Xunit;

using static Lumyte.Interaction.InteractionKit;

namespace Lumyte.Interaction.Tests;

public sealed class PlayerInputGestureTests
{
    [Fact]
    public void TapIsReadThroughTheCommonActionRuntime()
    {
        var select = new InputAction<bool>("game.select");
        GestureMap gestures = GestureMap("Gameplay")[
            new TapGesture(select)
        ];
        var clock = new ManualClock();
        var touchscreen = new VirtualTouchscreen();
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(
            platform,
            [ActionMap("Gameplay")],
            gestures: [gestures],
            clock: clock);
        PlayerInput player = inputs.AddSinglePlayer(
            new() { Player = 0 },
            new VirtualWindowInput(touchscreens: [touchscreen]));

        touchscreen.Begin(1, new(20, 30));
        touchscreen.End(1, new(22, 31));

        Assert.True(player.Actions.GetValue(select));
        Assert.True(player.Actions.WasPerformed(select));
        Assert.Equal(ActionPhase.Performed, player.Actions.GetPhase(select));
        player.Actions.ResetTransientValues();
        Assert.False(player.Actions.GetValue(select));
        Assert.False(player.Actions.WasPerformed(select));
        Assert.Equal(ActionPhase.Completed, player.Actions.GetPhase(select));
    }

    [Fact]
    public void SwipeValueIsReadThroughTheCommonActionRuntime()
    {
        var dodge = new InputAction<Vector2>("game.dodge");
        GestureMap gestures = GestureMap("Gameplay")[
            new SwipeGesture(
                dodge,
                direction: SwipeDirection.Right,
                minimumDistance: 50,
                minimumVelocity: 100)
        ];
        var clock = new ManualClock();
        var touchscreen = new VirtualTouchscreen();
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(
            platform,
            [ActionMap("Gameplay")],
            gestures: [gestures],
            clock: clock);
        PlayerInput player = inputs.AddSinglePlayer(
            new() { Player = 0 },
            new VirtualWindowInput(touchscreens: [touchscreen]));

        touchscreen.Begin(1, Vector2.Zero);
        clock.Advance(Duration.FromSeconds(0.25));
        touchscreen.End(1, new(100, 0));

        Assert.Equal(new Vector2(100, 0), player.Actions.GetValue(dodge));
        Assert.True(player.Actions.WasPerformed(dodge));
    }

    [Fact]
    public void GestureOnlyAffectsThePlayerOwningItsTouchscreen()
    {
        var select = new InputAction<bool>("game.select");
        GestureMap gestures = GestureMap("Gameplay")[
            new TapGesture(select)
        ];
        var clock = new ManualClock();
        var firstTouchscreen = new VirtualTouchscreen();
        var secondTouchscreen = new VirtualTouchscreen();
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(
            platform,
            [ActionMap("Gameplay")],
            maximumPlayers: 2,
            gestures: [gestures],
            clock: clock);
        PlayerInput first = inputs.AddPlayer(new() { Player = 0 });
        PlayerInput second = inputs.AddPlayer(new() { Player = 1 });
        inputs.Assign(firstTouchscreen, first);
        inputs.Assign(secondTouchscreen, second);

        secondTouchscreen.Begin(1, new(20, 30));
        secondTouchscreen.End(1, new(20, 30));

        Assert.False(first.Actions.WasPerformed(select));
        Assert.True(second.Actions.WasPerformed(select));
    }

    [Fact]
    public void LosingWindowFocusCancelsActionsAndGestureValuesTogether()
    {
        var jump = new InputAction<bool>("game.jump");
        var select = new InputAction<bool>("game.select");
        ActionMap actions = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
        ];
        GestureMap gestures = GestureMap("Gameplay")[
            new TapGesture(select)
        ];
        var clock = new ManualClock();
        var keyboard = new VirtualKeyboard();
        var touchscreen = new VirtualTouchscreen();
        var window = new VirtualWindow();
        var windowInput = new VirtualWindowInput(
            keyboards: [keyboard],
            touchscreens: [touchscreen],
            window: window);
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(
            platform,
            [actions],
            gestures: [gestures],
            clock: clock);
        PlayerInput player = inputs.AddSinglePlayer(new() { Player = 0 }, windowInput);
        keyboard.Press(Key.Space);
        touchscreen.Begin(1, new(20, 30));
        touchscreen.End(1, new(20, 30));

        window.SetFocus(false);

        Assert.False(player.Actions.GetValue(jump));
        Assert.False(player.Actions.WasPerformed(jump));
        Assert.Equal(ActionPhase.Canceled, player.Actions.GetPhase(jump));
        Assert.False(player.Actions.GetValue(select));
        Assert.False(player.Actions.WasPerformed(select));
        Assert.Equal(ActionPhase.Canceled, player.Actions.GetPhase(select));
    }

    [Fact]
    public void LosingWindowFocusRejectsAnInProgressGesture()
    {
        var dodge = new InputAction<Vector2>("game.dodge");
        GestureMap gestures = GestureMap("Gameplay")[
            new SwipeGesture(
                dodge,
                minimumDistance: 50,
                minimumVelocity: 100)
        ];
        var clock = new ManualClock();
        var touchscreen = new VirtualTouchscreen();
        var window = new VirtualWindow();
        var windowInput = new VirtualWindowInput(
            touchscreens: [touchscreen],
            window: window);
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(
            platform,
            [ActionMap("Gameplay")],
            gestures: [gestures],
            clock: clock);
        PlayerInput player = inputs.AddSinglePlayer(new() { Player = 0 }, windowInput);

        touchscreen.Begin(1, Vector2.Zero);
        window.SetFocus(false);
        clock.Advance(Duration.FromSeconds(0.25));
        touchscreen.End(1, new(100, 0));

        Assert.False(player.Actions.WasPerformed(dodge));
        Assert.Equal(Vector2.Zero, player.Actions.GetValue(dodge));
    }
}
