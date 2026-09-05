using System.Diagnostics;

using Lumyte.Core.Time;
using Lumyte.Input;
using Lumyte.Platform;

namespace Lumyte.Interaction;

public sealed class PlayerInputManager : IDisposable
{
    private readonly Dictionary<IGamepad, PlayerInput> assignedGamepads = [];
    private readonly Dictionary<IKeyboard, PlayerInput> assignedKeyboards = [];
    private readonly Dictionary<IMouse, PlayerInput> assignedMice = [];
    private readonly Dictionary<ITouchscreen, PlayerInput> assignedTouchscreens = [];
    private readonly Dictionary<IWindow, (IWindowInput Input, PlayerInput Player)> assignedWindows = [];
    private readonly HashSet<IGamepad> connectedGamepads = [];
    private readonly GamepadButtons joinButtons;
    private readonly IReadOnlyList<GestureMap> gestureMaps;
    private readonly IMonotonicClock? gestureClock;
    private readonly IReadOnlyList<ActionMap> maps;
    private readonly int maximumPlayers;
    private readonly List<PlayerJoinRequest> pendingJoins = [];
    private readonly IPlatformInput platformInput;
    private readonly List<PlayerInput> players = [];
    private readonly Dictionary<GamepadId, int> reservations = [];
    private PlayerInput? singlePlayer;
    private bool disposed;

    public PlayerInputManager(
        IPlatformInput platformInput,
        IEnumerable<ActionMap> actions,
        int maximumPlayers = 4,
        GamepadButtons joinButtons = GamepadButtons.South | GamepadButtons.Menu,
        IEnumerable<GestureMap>? gestures = null,
        IMonotonicClock? clock = null,
        bool requireJoinApproval = false)
    {
        this.platformInput = platformInput ?? throw new ArgumentNullException(nameof(platformInput));
        ArgumentNullException.ThrowIfNull(actions);
        this.maps = actions.ToArray();
        this.gestureMaps = gestures?.ToArray() ?? [];
        if (this.gestureMaps.Count != 0 && clock is null)
        {
            throw new ArgumentNullException(nameof(clock));
        }

        this.gestureClock = clock;
        if (maximumPlayers <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPlayers));
        }

        if (joinButtons == GamepadButtons.None)
        {
            throw new ArgumentOutOfRangeException(nameof(joinButtons));
        }

        this.maximumPlayers = maximumPlayers;
        this.joinButtons = joinButtons;
        RequireJoinApproval = requireJoinApproval;
        foreach (IGamepad gamepad in platformInput.Gamepads)
        {
            Connect(gamepad);
        }

        platformInput.GamepadConnectionChanged += OnGamepadConnectionChanged;
    }

    public IReadOnlyList<PlayerInput> Players => players;

    public IReadOnlyList<PlayerJoinRequest> PendingJoins => pendingJoins;

    public bool RequireJoinApproval { get; }

    public event EventHandler<PlayerJoinRequestedEventArgs>? JoinRequested;

    public void AcceptJoin(PlayerJoinRequest request, PlayerInput player)
    {
        ValidatePendingRequest(request);
        ArgumentNullException.ThrowIfNull(player);
        if (!connectedGamepads.Contains(request.Gamepad))
        {
            throw new InvalidOperationException("The joining gamepad is no longer connected.");
        }

        Assign(request.Gamepad, player);
    }

    public void RejectJoin(PlayerJoinRequest request)
    {
        ValidatePendingRequest(request);
        CompleteJoin(request, PlayerJoinRequestStatus.Rejected);
    }

    public PlayerInput AddPlayer(PlayerInputOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        if (options.Player < 0 || options.Player >= maximumPlayers)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (players.Any(player => player.Player == options.Player))
        {
            throw new ArgumentException($"Player {options.Player} already exists.", nameof(options));
        }

        var input = new PlayerInput(options, maps, gestureMaps, gestureClock);
        players.Add(input);
        players.Sort((left, right) => left.Player.CompareTo(right.Player));
        return input;
    }

    public PlayerInput AddSinglePlayer(
        PlayerInputOptions options,
        IWindowInput windowInput)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(windowInput);
        ThrowIfDisposed();
        if (players.Count != 0 || singlePlayer is not null)
        {
            throw new InvalidOperationException("Single-player input must be configured before other players.");
        }

        PlayerInput player = AddPlayer(options with { AcceptsGamepadJoin = false });
        singlePlayer = player;
        Assign(windowInput, player);
        foreach (IGamepad gamepad in connectedGamepads)
        {
            Assign(gamepad, player);
        }

        return player;
    }

    public bool RemovePlayer(PlayerInput player)
    {
        ArgumentNullException.ThrowIfNull(player);
        ThrowIfDisposed();
        if (!players.Remove(player))
        {
            return false;
        }

        foreach (IKeyboard keyboard in assignedKeyboards
            .Where(pair => ReferenceEquals(pair.Value, player))
            .Select(pair => pair.Key)
            .ToArray())
        {
            Unassign(keyboard);
        }

        foreach (IMouse mouse in assignedMice
            .Where(pair => ReferenceEquals(pair.Value, player))
            .Select(pair => pair.Key)
            .ToArray())
        {
            Unassign(mouse);
        }

        foreach (ITouchscreen touchscreen in assignedTouchscreens
            .Where(pair => ReferenceEquals(pair.Value, player))
            .Select(pair => pair.Key)
            .ToArray())
        {
            Unassign(touchscreen);
        }

        foreach (IGamepad gamepad in assignedGamepads
            .Where(pair => ReferenceEquals(pair.Value, player))
            .Select(pair => pair.Key)
            .ToArray())
        {
            Unassign(gamepad);
        }

        foreach (IWindow window in assignedWindows
            .Where(pair => ReferenceEquals(pair.Value.Player, player))
            .Select(pair => pair.Key)
            .ToArray())
        {
            window.FocusChanged -= OnWindowFocusChanged;
            assignedWindows.Remove(window);
        }

        foreach (GamepadId identity in reservations
            .Where(pair => pair.Value == player.Player)
            .Select(pair => pair.Key)
            .ToArray())
        {
            reservations.Remove(identity);
        }

        if (ReferenceEquals(singlePlayer, player))
        {
            singlePlayer = null;
        }

        player.Dispose();
        return true;
    }

    public void Assign(IWindowInput windowInput, PlayerInput player)
    {
        ArgumentNullException.ThrowIfNull(windowInput);
        ArgumentNullException.ThrowIfNull(player);
        if (assignedWindows.ContainsKey(windowInput.Window))
        {
            throw new ArgumentException("The window input is already assigned.", nameof(windowInput));
        }

        assignedWindows.Add(windowInput.Window, (windowInput, player));
        windowInput.Window.FocusChanged += OnWindowFocusChanged;
        foreach (IKeyboard keyboard in windowInput.Keyboards)
        {
            Assign(keyboard, player);
        }

        foreach (IMouse mouse in windowInput.Mice)
        {
            Assign(mouse, player);
        }

        foreach (ITouchscreen touchscreen in windowInput.Touchscreens)
        {
            Assign(touchscreen, player);
        }
    }

    public void Assign(IKeyboard keyboard, PlayerInput player)
    {
        ValidateAssignment(keyboard, player, assignedKeyboards);
        assignedKeyboards.Add(keyboard, player);
        player.AddKeyboard(keyboard);
        RecordDeviceAssignment("keyboard", "assigned");
    }

    public void Assign(IMouse mouse, PlayerInput player)
    {
        ValidateAssignment(mouse, player, assignedMice);
        assignedMice.Add(mouse, player);
        player.AddMouse(mouse);
        RecordDeviceAssignment("mouse", "assigned");
    }

    public void Assign(ITouchscreen touchscreen, PlayerInput player)
    {
        ValidateAssignment(touchscreen, player, assignedTouchscreens);
        assignedTouchscreens.Add(touchscreen, player);
        player.AddTouchscreen(touchscreen);
        RecordDeviceAssignment("touch", "assigned");
    }

    public void Assign(IGamepad gamepad, PlayerInput player)
    {
        ValidateAssignment(gamepad, player, assignedGamepads);
        if (!connectedGamepads.Contains(gamepad))
        {
            throw new ArgumentException("The gamepad is not connected.", nameof(gamepad));
        }

        assignedGamepads.Add(gamepad, player);
        if (pendingJoins.FirstOrDefault(request => ReferenceEquals(request.Gamepad, gamepad))
            is PlayerJoinRequest pending)
        {
            CompleteJoin(pending, PlayerJoinRequestStatus.Accepted);
        }

        reservations.Remove(gamepad.Id);
        player.AddGamepad(gamepad);
        RecordDeviceAssignment("gamepad", "assigned");
    }

    public bool Unassign(IKeyboard keyboard)
    {
        ArgumentNullException.ThrowIfNull(keyboard);
        if (!assignedKeyboards.Remove(keyboard, out PlayerInput? player))
        {
            return false;
        }

        player.RemoveKeyboard(keyboard);
        RecordDeviceAssignment("keyboard", "unassigned");
        return true;
    }

    public bool Unassign(IMouse mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (!assignedMice.Remove(mouse, out PlayerInput? player))
        {
            return false;
        }

        player.RemoveMouse(mouse);
        RecordDeviceAssignment("mouse", "unassigned");
        return true;
    }

    public bool Unassign(ITouchscreen touchscreen)
    {
        ArgumentNullException.ThrowIfNull(touchscreen);
        if (!assignedTouchscreens.Remove(touchscreen, out PlayerInput? player))
        {
            return false;
        }

        player.RemoveTouchscreen(touchscreen);
        RecordDeviceAssignment("touch", "unassigned");
        return true;
    }

    public bool Unassign(IGamepad gamepad)
    {
        ArgumentNullException.ThrowIfNull(gamepad);
        if (!assignedGamepads.Remove(gamepad, out PlayerInput? player))
        {
            return false;
        }

        player.RemoveGamepad(gamepad);
        RecordDeviceAssignment("gamepad", "unassigned");
        reservations.Remove(gamepad.Id);
        return true;
    }

    public bool TryGetPlayer(IGamepad gamepad, out PlayerInput? player)
    {
        ArgumentNullException.ThrowIfNull(gamepad);
        return assignedGamepads.TryGetValue(gamepad, out player);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        platformInput.GamepadConnectionChanged -= OnGamepadConnectionChanged;
        foreach (IGamepad gamepad in connectedGamepads)
        {
            gamepad.StateChanged -= OnUnassignedGamepadStateChanged;
        }

        foreach (PlayerInput player in players.ToArray())
        {
            RemovePlayer(player);
        }

        connectedGamepads.Clear();
        reservations.Clear();
        assignedWindows.Clear();
        foreach (PlayerJoinRequest request in pendingJoins.ToArray())
        {
            CompleteJoin(request, PlayerJoinRequestStatus.Canceled);
        }

        disposed = true;
    }

    private void OnGamepadConnectionChanged(
        object? sender,
        GamepadConnectionChangedEventArgs eventArgs)
    {
        if (eventArgs.IsConnected)
        {
            Connect(eventArgs.Gamepad);
        }
        else
        {
            Disconnect(eventArgs.Gamepad);
        }
    }

    private void OnWindowFocusChanged(object? sender, WindowFocusChangedEventArgs eventArgs)
    {
        if (!eventArgs.IsFocused
            && sender is IWindow window
            && assignedWindows.TryGetValue(window, out var assignment))
        {
            using Activity? activity = InteractionDiagnostics.Activities.StartActivity(
                "PlayerInput.CancelWindowInput");
            activity?.SetTag("interaction.player", assignment.Player.Player);
            activity?.SetTag("interaction.input.keyboard_count", assignment.Input.Keyboards.Count);
            activity?.SetTag("interaction.input.mouse_count", assignment.Input.Mice.Count);
            activity?.SetTag("interaction.input.touch_count", assignment.Input.Touchscreens.Count);
            assignment.Player.Cancel(assignment.Input);
        }
    }

    private void Connect(IGamepad gamepad)
    {
        if (!connectedGamepads.Add(gamepad))
        {
            return;
        }

        gamepad.StateChanged += OnUnassignedGamepadStateChanged;
        if (singlePlayer is not null)
        {
            Assign(gamepad, singlePlayer);
            return;
        }

        GamepadId identity = gamepad.Id;
        if (reservations.Remove(identity, out int playerIndex)
            && players.FirstOrDefault(player => player.Player == playerIndex) is PlayerInput player)
        {
            Assign(gamepad, player);
        }
    }

    private void Disconnect(IGamepad gamepad)
    {
        if (!connectedGamepads.Remove(gamepad))
        {
            return;
        }

        gamepad.StateChanged -= OnUnassignedGamepadStateChanged;
        if (pendingJoins.FirstOrDefault(request => ReferenceEquals(request.Gamepad, gamepad))
            is PlayerJoinRequest pending)
        {
            CompleteJoin(pending, PlayerJoinRequestStatus.Canceled);
        }

        if (!assignedGamepads.Remove(gamepad, out PlayerInput? player))
        {
            return;
        }

        player.RemoveGamepad(gamepad);
        if (!ReferenceEquals(player, singlePlayer))
        {
            reservations[gamepad.Id] = player.Player;
        }
    }

    private void OnUnassignedGamepadStateChanged(
        object? sender,
        GamepadStateChangedEventArgs eventArgs)
    {
        if (sender is not IGamepad gamepad || assignedGamepads.ContainsKey(gamepad))
        {
            return;
        }

        GamepadButtons newlyPressed = eventArgs.Current.Buttons & ~eventArgs.Previous.Buttons;
        if ((newlyPressed & joinButtons) == 0)
        {
            return;
        }

        PlayerInput? available = players.FirstOrDefault(player =>
            player.AcceptsGamepadJoin && player.Gamepads.Count == 0);
        if (available is not null && !RequireJoinApproval)
        {
            Assign(gamepad, available);
        }
        else if (!pendingJoins.Any(request => ReferenceEquals(request.Gamepad, gamepad)))
        {
            var request = new PlayerJoinRequest(gamepad);
            pendingJoins.Add(request);
            InteractionDiagnostics.PlayerJoinRequests.Add(
                1,
                [new("status", "pending")]);
            JoinRequested?.Invoke(this, new(request));
        }
    }

    private void ValidatePendingRequest(PlayerJoinRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (!pendingJoins.Contains(request)
            || request.Status != PlayerJoinRequestStatus.Pending)
        {
            throw new ArgumentException("The join request is not pending in this manager.", nameof(request));
        }
    }

    private void CompleteJoin(PlayerJoinRequest request, PlayerJoinRequestStatus status)
    {
        pendingJoins.Remove(request);
        request.Complete(status);
        using Activity? activity = InteractionDiagnostics.Activities.StartActivity(
            "PlayerInputManager.CompleteJoin");
        activity?.SetTag("interaction.player_join.status", status.ToString());
        InteractionDiagnostics.PlayerJoinRequests.Add(
            1,
            [new("status", status.ToString().ToLowerInvariant())]);
    }

    private static void RecordDeviceAssignment(string deviceType, string outcome) =>
        InteractionDiagnostics.DeviceAssignments.Add(
            1,
            new("device_type", deviceType),
            new("outcome", outcome));

    private void ValidateAssignment<T>(
        T device,
        PlayerInput player,
        IReadOnlyDictionary<T, PlayerInput> assignments)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(player);
        ThrowIfDisposed();
        if (!players.Contains(player))
        {
            throw new ArgumentException("The player input belongs to another manager.", nameof(player));
        }

        if (assignments.ContainsKey(device))
        {
            throw new ArgumentException("The input device is already assigned.", nameof(device));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
