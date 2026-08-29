using Lumyte.Input;
using System.Numerics;

using Xunit;

using static Lumyte.Interaction.InteractionKit;

namespace Lumyte.Interaction.Tests;

public sealed class ActionRuntimeTests
{
    [Fact]
    public void EmulatedKeyPressAndReleaseUpdateTheBoundAction()
    {
        var jump = new InputAction<bool>("game.jump");
        ActionMap gameplay = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
        ];
        var keyboard = new VirtualKeyboard();
        using var runtime = new ActionRuntime(keyboard, new InteractionContext(), gameplay);
        var changes = new List<bool>();
        runtime.ActionChanged += (_, eventArgs) => changes.Add(eventArgs.Value);

        keyboard.Press(Key.Space);
        bool pressed = runtime.GetValue(jump);
        keyboard.Release(Key.Space);
        bool released = runtime.GetValue(jump);

        Assert.True(pressed);
        Assert.False(released);
        Assert.Equal([true, false], changes);
    }

    [Fact]
    public void KeyPressAndReleaseReportActionPhases()
    {
        var jump = new InputAction<bool>("game.jump");
        ActionMap gameplay = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
        ];
        var keyboard = new VirtualKeyboard();
        using var runtime = new ActionRuntime(keyboard, new InteractionContext(), gameplay);
        var phases = new List<ActionPhase>();
        runtime.PhaseChanged += (_, eventArgs) => phases.Add(eventArgs.Phase);

        keyboard.Press(Key.Space);
        keyboard.Release(Key.Space);

        Assert.Equal(
            [ActionPhase.Started, ActionPhase.Performed, ActionPhase.Completed],
            phases);
        Assert.Equal(ActionPhase.Completed, runtime.GetPhase(jump));
    }

    [Fact]
    public void WasdCompositeProducesNormalizedMovement()
    {
        var move = new InputAction<Vector2>("game.move");
        ActionMap gameplay = ActionMap("Gameplay")[
            new Vector2CompositeBinding(
                move,
                up: InputControls.Key(Key.W),
                down: InputControls.Key(Key.S),
                left: InputControls.Key(Key.A),
                right: InputControls.Key(Key.D))
        ];
        var keyboard = new VirtualKeyboard();
        using var runtime = new ActionRuntime(keyboard, new InteractionContext(), gameplay);

        keyboard.Press(Key.W);
        keyboard.Press(Key.D);
        Vector2 diagonal = runtime.GetValue(move);
        keyboard.Release(Key.D);
        Vector2 upward = runtime.GetValue(move);
        keyboard.Release(Key.W);

        Assert.Equal(MathF.Sqrt(0.5f), diagonal.X, precision: 5);
        Assert.Equal(MathF.Sqrt(0.5f), diagonal.Y, precision: 5);
        Assert.Equal(Vector2.UnitY, upward);
        Assert.Equal(Vector2.Zero, runtime.GetValue(move));
    }

    [Fact]
    public void StrongestBindingDrivesMovementUntilItIsReleased()
    {
        var move = new InputAction<Vector2>("game.move");
        ActionMap gameplay = ActionMap("Gameplay")[
            new Vector2CompositeBinding(
                move,
                up: InputControls.Key(Key.W),
                down: InputControls.Key(Key.S),
                left: InputControls.Key(Key.A),
                right: InputControls.Key(Key.D)),
            new ActionBinding<Vector2>(move, InputControls.GamepadLeftStick())
        ];
        var keyboard = new VirtualKeyboard();
        var gamepad = new VirtualGamepad();
        using var runtime = new ActionRuntime(
            new InteractionContext(),
            [gameplay],
            keyboards: [keyboard],
            gamepads: [gamepad]);
        gamepad.SetState(new(
            GamepadButtons.None,
            new(0.5f, 0),
            Vector2.Zero,
            0,
            0));

        keyboard.Press(Key.W);
        Vector2 whileKeyboardIsPressed = runtime.GetValue(move);
        keyboard.Release(Key.W);
        Vector2 afterKeyboardIsReleased = runtime.GetValue(move);

        Assert.Equal(Vector2.UnitY, whileKeyboardIsPressed);
        Assert.Equal(new Vector2(0.5f, 0), afterKeyboardIsReleased);
    }

    [Fact]
    public void CumulativeActionAddsValuesFromEveryBinding()
    {
        var move = new InputAction<Vector2>(
            "game.move",
            ActionValueAggregation.Cumulative);
        ActionMap gameplay = ActionMap("Gameplay")[
            new Vector2CompositeBinding(
                move,
                up: InputControls.Key(Key.W),
                down: InputControls.Key(Key.S),
                left: InputControls.Key(Key.A),
                right: InputControls.Key(Key.D)),
            new ActionBinding<Vector2>(move, InputControls.GamepadLeftStick())
        ];
        var keyboard = new VirtualKeyboard();
        var gamepad = new VirtualGamepad();
        using var runtime = new ActionRuntime(
            new InteractionContext(),
            [gameplay],
            keyboards: [keyboard],
            gamepads: [gamepad]);
        gamepad.SetState(new(
            GamepadButtons.None,
            new(0.5f, 0),
            Vector2.Zero,
            0,
            0));

        keyboard.Press(Key.W);

        Assert.Equal(new Vector2(0.5f, 1), runtime.GetValue(move));
    }

    [Fact]
    public void ResettingTransientBindingRestoresContinuousBindingValue()
    {
        var look = new InputAction<Vector2>("game.look");
        ActionMap gameplay = ActionMap("Gameplay")[
            new ActionBinding<Vector2>(look, InputControls.MouseDelta),
            new ActionBinding<Vector2>(look, InputControls.GamepadRightStick())
        ];
        var mouse = new VirtualMouse();
        var gamepad = new VirtualGamepad();
        using var runtime = new ActionRuntime(
            new InteractionContext(),
            [gameplay],
            mice: [mouse],
            gamepads: [gamepad]);
        gamepad.SetState(new(
            GamepadButtons.None,
            Vector2.Zero,
            new(0.25f, 0),
            0,
            0));
        mouse.Move(new(2, 0));

        runtime.ResetTransientValues();

        Assert.Equal(new Vector2(0.25f, 0), runtime.GetValue(look));
    }

    [Fact]
    public void GamepadDpadUsesTheSameCompositeBinding()
    {
        var move = new InputAction<Vector2>("game.move");
        ActionMap gameplay = ActionMap("Gameplay")[
            new Vector2CompositeBinding(
                move,
                up: InputControls.GamepadButton(GamepadButtons.DPadUp),
                down: InputControls.GamepadButton(GamepadButtons.DPadDown),
                left: InputControls.GamepadButton(GamepadButtons.DPadLeft),
                right: InputControls.GamepadButton(GamepadButtons.DPadRight))
        ];
        var gamepad = new VirtualGamepad();
        using var runtime = new ActionRuntime(
            new InteractionContext(),
            [gameplay],
            gamepads: [gamepad]);

        gamepad.SetState(new(
            GamepadButtons.DPadLeft | GamepadButtons.DPadUp,
            Vector2.Zero,
            Vector2.Zero,
            0,
            0));

        Vector2 actual = runtime.GetValue(move);
        Assert.Equal(-MathF.Sqrt(0.5f), actual.X, precision: 5);
        Assert.Equal(MathF.Sqrt(0.5f), actual.Y, precision: 5);
    }

    [Fact]
    public void EmulatedInputOnlyUsesMapsWhoseContextIsActive()
    {
        ContextKey<bool> gameRunning = ContextKey.Create<bool>("game.running");
        var jump = new InputAction<bool>("game.jump");
        ActionMap gameplay = ActionMap("Gameplay", gameRunning.Is(true))[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
        ];
        var context = new InteractionContext();
        var keyboard = new VirtualKeyboard();
        using var runtime = new ActionRuntime(keyboard, context, gameplay);

        keyboard.Press(Key.Space);
        bool whileDisabled = runtime.GetValue(jump);
        keyboard.Release(Key.Space);
        context.Set(gameRunning, true);
        keyboard.Press(Key.Space);
        bool whileEnabled = runtime.GetValue(jump);

        Assert.False(whileDisabled);
        Assert.True(whileEnabled);
    }

    [Fact]
    public void HigherPriorityMapReceivesEmulatedInput()
    {
        var gameplayAction = new InputAction<bool>("game.confirm");
        var menuAction = new InputAction<bool>("menu.confirm");
        ActionMap gameplay = ActionMap("Gameplay", priority: 10)[
            new ActionBinding<bool>(gameplayAction, InputControls.Key(Key.Enter))
        ];
        ActionMap menu = ActionMap("Menu", priority: 100)[
            new ActionBinding<bool>(menuAction, InputControls.Key(Key.Enter))
        ];
        var keyboard = new VirtualKeyboard();
        using var runtime = new ActionRuntime(
            keyboard,
            new InteractionContext(),
            gameplay,
            menu);

        keyboard.Press(Key.Enter);

        Assert.False(runtime.GetValue(gameplayAction));
        Assert.True(runtime.GetValue(menuAction));
    }

    [Fact]
    public void EmulatedMouseButtonAndMovementUpdateActions()
    {
        var select = new InputAction<bool>("editor.select");
        var look = new InputAction<Vector2>("editor.look");
        ActionMap map = ActionMap("Editor")[
            new ActionBinding<bool>(select, InputControls.MouseButton(MouseButton.Left)),
            new ActionBinding<Vector2>(look, InputControls.MouseDelta)
        ];
        var mouse = new VirtualMouse();
        using var runtime = new ActionRuntime(
            new InteractionContext(),
            [map],
            mice: [mouse]);

        mouse.Press(MouseButton.Left);
        mouse.Move(new(10, 5));

        Assert.True(runtime.GetValue(select));
        Assert.Equal(new Vector2(10, 5), runtime.GetValue(look));
        runtime.ResetTransientValues();
        Assert.Equal(Vector2.Zero, runtime.GetValue(look));
    }

    [Fact]
    public void EmulatedGamepadAppliesStickProcessors()
    {
        var move = new InputAction<Vector2>("game.move");
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<Vector2>(
                move,
                InputControls.GamepadLeftStick(),
                InputProcessors.RadialDeadZone(0.2f),
                InputProcessors.InvertY())
        ];
        var gamepad = new VirtualGamepad();
        using var runtime = new ActionRuntime(
            new InteractionContext(),
            [map],
            gamepads: [gamepad]);

        gamepad.SetState(new(
            GamepadButtons.None,
            new(0, 0.6f),
            Vector2.Zero,
            0,
            0));

        Vector2 actual = runtime.GetValue(move);
        Assert.Equal(0, actual.X, precision: 5);
        Assert.Equal(-0.5f, actual.Y, precision: 5);
    }

    [Fact]
    public void PlayerSpecificGamepadBindingWinsOverGenericBinding()
    {
        var generic = new InputAction<bool>("game.genericConfirm");
        var playerOne = new InputAction<bool>("game.playerOneConfirm");
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<bool>(generic, InputControls.GamepadButton(GamepadButtons.South)),
            new ActionBinding<bool>(playerOne, InputControls.GamepadButton(GamepadButtons.South, player: 0))
        ];
        var gamepad = new VirtualGamepad();
        using var runtime = new ActionRuntime(
            new InteractionContext(),
            [map],
            gamepads: [gamepad]);

        gamepad.SetState(new(
            GamepadButtons.South,
            Vector2.Zero,
            Vector2.Zero,
            0,
            0));

        Assert.False(runtime.GetValue(generic));
        Assert.True(runtime.GetValue(playerOne));
    }

    [Fact]
    public void RawMovementAndWheelAreIndependentTransientActions()
    {
        var rawLook = new InputAction<Vector2>("editor.rawLook");
        var scroll = new InputAction<Vector2>("editor.scroll");
        ActionMap map = ActionMap("Editor")[
            new ActionBinding<Vector2>(rawLook, InputControls.MouseRawDelta),
            new ActionBinding<Vector2>(scroll, InputControls.MouseWheel)
        ];
        var mouse = new VirtualMouse();
        using var runtime = new ActionRuntime(
            new InteractionContext(),
            [map],
            mice: [mouse]);

        mouse.MoveRaw(new(3, -2));
        mouse.Scroll(new(0, 1));

        Assert.Equal(new Vector2(3, -2), runtime.GetValue(rawLook));
        Assert.Equal(new Vector2(0, 1), runtime.GetValue(scroll));
        runtime.ResetTransientValues();
        Assert.Equal(Vector2.Zero, runtime.GetValue(rawLook));
        Assert.Equal(Vector2.Zero, runtime.GetValue(scroll));
    }

    [Fact]
    public void EmulatedTriggerAppliesDeadZoneAndScale()
    {
        var accelerate = new InputAction<float>("game.accelerate");
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<float>(
                accelerate,
                InputControls.GamepadRightTrigger(),
                InputProcessors.DeadZone(0.2f),
                InputProcessors.Scale(2))
        ];
        var gamepad = new VirtualGamepad();
        using var runtime = new ActionRuntime(
            new InteractionContext(),
            [map],
            gamepads: [gamepad]);

        gamepad.SetState(new(
            GamepadButtons.None,
            Vector2.Zero,
            Vector2.Zero,
            0,
            0.6f));

        Assert.Equal(1, runtime.GetValue(accelerate), precision: 5);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1f)]
    public void InvalidDeadZoneIsRejected(float minimum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InputProcessors.DeadZone(minimum));
        Assert.Throws<ArgumentOutOfRangeException>(() => InputProcessors.RadialDeadZone(minimum));
    }

    [Fact]
    public void RemovingGamepadReleasesItsActiveAction()
    {
        var confirm = new InputAction<bool>("game.confirm");
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<bool>(confirm, InputControls.GamepadButton(GamepadButtons.South, 1))
        ];
        var gamepad = new VirtualGamepad();
        using var runtime = new ActionRuntime(new InteractionContext(), [map]);
        runtime.AddGamepad(gamepad, player: 1);
        gamepad.SetState(new(
            GamepadButtons.South,
            Vector2.Zero,
            Vector2.Zero,
            0,
            0));

        bool beforeRemoval = runtime.GetValue(confirm);
        runtime.RemoveGamepad(gamepad);

        Assert.True(beforeRemoval);
        Assert.False(runtime.GetValue(confirm));
        Assert.Equal(ActionPhase.Canceled, runtime.GetPhase(confirm));
    }
}
