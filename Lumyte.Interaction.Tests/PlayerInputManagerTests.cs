using System.Numerics;

using Lumyte.Input;

using Xunit;

using static Lumyte.Interaction.InteractionKit;

namespace Lumyte.Interaction.Tests;

public sealed class PlayerInputManagerTests
{
    [Fact]
    public void SinglePlayerUsesKeyboardAndGamepadWithoutJoining()
    {
        var confirm = new InputAction<bool>("game.confirm");
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<bool>(confirm, InputControls.Key(Key.Enter)),
            new ActionBinding<bool>(confirm, InputControls.GamepadButton(GamepadButtons.South))
        ];
        var keyboard = new VirtualKeyboard();
        var gamepad = new VirtualGamepad("Gamepad");
        var platform = new VirtualPlatformInput();
        platform.Connect(gamepad);
        using var inputs = new PlayerInputManager(platform, [map]);
        PlayerInput player = inputs.AddSinglePlayer(
            new() { Player = 0 },
            new VirtualWindowInput(keyboards: [keyboard]));
        var sources = new List<Type>();
        player.ActiveSourceChanged += (_, eventArgs) =>
        {
            if (eventArgs.Current is not null)
            {
                sources.Add(eventArgs.Current.GetType());
            }
        };

        keyboard.Press(Key.Enter);
        keyboard.Release(Key.Enter);
        gamepad.SetState(State(GamepadButtons.South));

        Assert.True(player.Actions.GetValue(confirm));
        Assert.IsType<PlayerInputSource.Gamepad>(player.ActiveSource);
        Assert.Equal(
            [typeof(PlayerInputSource.Keyboard), typeof(PlayerInputSource.Gamepad)],
            sources);
    }

    [Fact]
    public void SinglePlayerMovementFallsBackBetweenKeyboardAndGamepadBindings()
    {
        var move = new InputAction<Vector2>("game.move");
        ActionMap map = ActionMap("Gameplay")[
            new Vector2CompositeBinding(
                move,
                up: InputControls.Key(Key.W),
                down: InputControls.Key(Key.S),
                left: InputControls.Key(Key.A),
                right: InputControls.Key(Key.D)),
            new ActionBinding<Vector2>(move, InputControls.GamepadLeftStick())
        ];
        var keyboard = new VirtualKeyboard();
        var gamepad = new VirtualGamepad("Gamepad", "movement-gamepad");
        var platform = new VirtualPlatformInput();
        platform.Connect(gamepad);
        using var inputs = new PlayerInputManager(platform, [map]);
        PlayerInput player = inputs.AddSinglePlayer(
            new() { Player = 0 },
            new VirtualWindowInput(keyboards: [keyboard]));
        gamepad.SetState(new(
            GamepadButtons.None,
            new(0.5f, 0),
            Vector2.Zero,
            0,
            0));

        keyboard.Press(Key.W);
        Vector2 whileKeyboardIsPressed = player.Actions.GetValue(move);
        keyboard.Release(Key.W);
        Vector2 afterKeyboardIsReleased = player.Actions.GetValue(move);

        Assert.Equal(Vector2.UnitY, whileKeyboardIsPressed);
        Assert.Equal(new Vector2(0.5f, 0), afterKeyboardIsReleased);
    }

    [Fact]
    public void FixedTwoPlayersKeepTheirInputSourcesIndependent()
    {
        var confirm = new InputAction<bool>("game.confirm");
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<bool>(confirm, InputControls.Key(Key.Enter)),
            new ActionBinding<bool>(confirm, InputControls.GamepadButton(GamepadButtons.South))
        ];
        var keyboard = new VirtualKeyboard();
        var gamepad = new VirtualGamepad("Second player");
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(platform, [map], maximumPlayers: 2);
        PlayerInput playerOne = inputs.AddPlayer(new()
        {
            Player = 0,
            AcceptsGamepadJoin = false,
        });
        PlayerInput playerTwo = inputs.AddPlayer(new() { Player = 1 });
        inputs.Assign(keyboard, playerOne);
        platform.Connect(gamepad);

        gamepad.SetState(State(GamepadButtons.Menu));
        keyboard.Press(Key.Enter);
        gamepad.SetState(State(GamepadButtons.None));
        gamepad.SetState(State(GamepadButtons.South));

        Assert.IsType<PlayerInputSource.Keyboard>(playerOne.ActiveSource);
        Assert.IsType<PlayerInputSource.Gamepad>(playerTwo.ActiveSource);
        Assert.True(playerOne.Actions.GetValue(confirm));
        Assert.True(playerTwo.Actions.GetValue(confirm));
    }

    [Fact]
    public void JoinRequestCanCreateAPlayerDuringTheLobby()
    {
        ActionMap map = ActionMap("Gameplay");
        var gamepad = new VirtualGamepad("Late joiner");
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(platform, [map], maximumPlayers: 2);
        PlayerInput? joined = null;
        PlayerJoinRequest? request = null;
        inputs.JoinRequested += (_, eventArgs) =>
        {
            request = eventArgs.Request;
            joined = inputs.AddPlayer(new() { Player = 0 });
            inputs.AcceptJoin(eventArgs.Request, joined);
        };
        platform.Connect(gamepad);

        gamepad.SetState(State(GamepadButtons.Menu));

        Assert.NotNull(joined);
        Assert.Same(joined, Assert.Single(inputs.Players));
        Assert.True(inputs.TryGetPlayer(gamepad, out PlayerInput? assigned));
        Assert.Same(joined, assigned);
        Assert.Equal(PlayerJoinRequestStatus.Accepted, request?.Status);
        Assert.Empty(inputs.PendingJoins);
    }

    [Fact]
    public void ApprovalModeKeepsOnePendingRequestUntilRejected()
    {
        ActionMap map = ActionMap("Gameplay");
        var gamepad = new VirtualGamepad("Applicant");
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(
            platform,
            [map],
            requireJoinApproval: true);
        inputs.AddPlayer(new() { Player = 0 });
        platform.Connect(gamepad);

        gamepad.SetState(State(GamepadButtons.Menu));
        gamepad.SetState(State(GamepadButtons.None));
        gamepad.SetState(State(GamepadButtons.Menu));
        PlayerJoinRequest request = Assert.Single(inputs.PendingJoins);
        inputs.RejectJoin(request);

        Assert.Equal(PlayerJoinRequestStatus.Rejected, request.Status);
        Assert.Empty(inputs.PendingJoins);
        Assert.False(inputs.TryGetPlayer(gamepad, out _));
    }

    [Fact]
    public void DisconnectingGamepadCancelsItsPendingJoinRequest()
    {
        ActionMap map = ActionMap("Gameplay");
        var gamepad = new VirtualGamepad("Applicant");
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(platform, [map]);
        platform.Connect(gamepad);
        gamepad.SetState(State(GamepadButtons.Menu));
        PlayerJoinRequest request = Assert.Single(inputs.PendingJoins);

        platform.Disconnect(gamepad);

        Assert.Equal(PlayerJoinRequestStatus.Canceled, request.Status);
        Assert.Empty(inputs.PendingJoins);
    }

    [Fact]
    public void ReconnectedGamepadReturnsToItsExistingPlayer()
    {
        ActionMap map = ActionMap("Gameplay");
        var original = new VirtualGamepad("Original name", "stable-device");
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(platform, [map]);
        PlayerInput player = inputs.AddPlayer(new() { Player = 0 });
        platform.Connect(original);
        original.SetState(State(GamepadButtons.Menu));

        platform.Disconnect(original);
        var reconnected = new VirtualGamepad("Renamed device", "stable-device");
        platform.Connect(reconnected);

        Assert.DoesNotContain(original, player.Gamepads);
        Assert.Contains(reconnected, player.Gamepads);
        Assert.True(inputs.TryGetPlayer(reconnected, out PlayerInput? restored));
        Assert.Same(player, restored);
    }

    [Fact]
    public void DisplayNameDoesNotRestoreAnotherGamepadsReservation()
    {
        ActionMap map = ActionMap("Gameplay");
        var original = new VirtualGamepad("Shared name", "original-device");
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(platform, [map]);
        PlayerInput player = inputs.AddPlayer(new() { Player = 0 });
        platform.Connect(original);
        original.SetState(State(GamepadButtons.Menu));

        platform.Disconnect(original);
        var other = new VirtualGamepad("Shared name", "other-device");
        platform.Connect(other);

        Assert.Empty(player.Gamepads);
        Assert.False(inputs.TryGetPlayer(other, out _));
    }

    [Fact]
    public void BindingDocumentsRemainIndependentPerPlayer()
    {
        var jump = new InputAction<bool>("game.jump");
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
        ];
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(platform, [map], maximumPlayers: 2);
        PlayerInput playerOne = inputs.AddPlayer(new() { Player = 0 });
        PlayerInput playerTwo = inputs.AddPlayer(new() { Player = 1 });
        ActionBindingSlot playerOneSlot = Assert.Single(playerOne.Bindings.Slots);
        RebindingSession session = playerOne.Bindings.BeginRebinding(playerOneSlot.Id);

        session.TryOffer(RebindingCandidate.From(InputControls.Key(Key.J)));
        session.Confirm();

        Assert.Equal(InputControlDescriptor.From(InputControls.Key(Key.J)), playerOneSlot.Control);
        Assert.Equal(
            InputControlDescriptor.From(InputControls.Key(Key.Space)),
            Assert.Single(playerTwo.Bindings.Slots).Control);
    }

    [Fact]
    public void InsignificantMouseMovementDoesNotReplaceTheActiveSource()
    {
        ActionMap map = ActionMap("Gameplay");
        var keyboard = new VirtualKeyboard();
        var mouse = new VirtualMouse();
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(platform, [map]);
        PlayerInput player = inputs.AddPlayer(new()
        {
            Player = 0,
            MouseMovementThreshold = 2,
        });
        inputs.Assign(keyboard, player);
        inputs.Assign(mouse, player);
        keyboard.Press(Key.Space);

        mouse.Move(new(1, 0));
        PlayerInputSource? afterSmallMovement = player.ActiveSource;
        mouse.Move(new(3, 0));

        Assert.IsType<PlayerInputSource.Keyboard>(afterSmallMovement);
        Assert.IsType<PlayerInputSource.Mouse>(player.ActiveSource);
    }

    [Fact]
    public void GamepadDriftDoesNotReplaceTheActiveSource()
    {
        ActionMap map = ActionMap("Gameplay");
        var keyboard = new VirtualKeyboard();
        var gamepad = new VirtualGamepad("Gamepad");
        var platform = new VirtualPlatformInput();
        platform.Connect(gamepad);
        using var inputs = new PlayerInputManager(platform, [map]);
        PlayerInput player = inputs.AddPlayer(new()
        {
            Player = 0,
            GamepadStickThreshold = 0.25f,
        });
        inputs.Assign(keyboard, player);
        inputs.Assign(gamepad, player);
        keyboard.Press(Key.Space);

        gamepad.SetState(new(
            GamepadButtons.None,
            new(0.1f, 0),
            Vector2.Zero,
            0,
            0));
        PlayerInputSource? afterDrift = player.ActiveSource;
        gamepad.SetState(new(
            GamepadButtons.None,
            new(0.5f, 0),
            Vector2.Zero,
            0,
            0));

        Assert.IsType<PlayerInputSource.Keyboard>(afterDrift);
        Assert.IsType<PlayerInputSource.Gamepad>(player.ActiveSource);
    }

    private static GamepadState State(GamepadButtons buttons) =>
        new(buttons, Vector2.Zero, Vector2.Zero, 0, 0);
}
