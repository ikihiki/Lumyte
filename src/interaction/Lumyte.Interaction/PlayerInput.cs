using System.Diagnostics;
using System.Numerics;

using Lumyte.Core.Time;
using Lumyte.Input;
using Lumyte.Platform;

namespace Lumyte.Interaction;

public sealed class PlayerInput : IDisposable
{
    private readonly HashSet<IGamepad> gamepads = [];
    private readonly IMonotonicClock? gestureClock;
    private readonly IReadOnlyList<GestureMap> gestureMaps;
    private readonly Dictionary<object, GestureRuntime> gestureRuntimes = [];
    private readonly HashSet<IKeyboard> keyboards = [];
    private readonly HashSet<IMouse> mice = [];
    private readonly IReadOnlyList<ActionMap> maps;
    private readonly PlayerInputOptions options;
    private readonly HashSet<ITouchscreen> touchscreens = [];
    private bool disposed;

    internal PlayerInput(
        PlayerInputOptions options,
        IReadOnlyList<ActionMap> maps,
        IReadOnlyList<GestureMap> gestureMaps,
        IMonotonicClock? gestureClock)
    {
        this.options = options;
        this.maps = maps;
        this.gestureMaps = gestureMaps;
        this.gestureClock = gestureClock;
        ValidateGestureMaps(gestureMaps);
        Player = options.Player;
        Bindings = ActionBindingDocument.Create(maps);
        if (options.BindingOverridesJson is string json)
        {
            Bindings.LoadOverrides(json);
        }

        var context = new InteractionContext();
        Actions = new(context, ActionBindingCompiler.Compile(maps, Bindings));
        GestureContext = context;
    }

    public int Player { get; }

    public bool AcceptsGamepadJoin => options.AcceptsGamepadJoin;

    public ActionRuntime Actions { get; }

    private InteractionContext GestureContext { get; }

    public ActionBindingDocument Bindings { get; private set; }

    public PlayerInputSource? ActiveSource { get; private set; }

    public IReadOnlyCollection<IGamepad> Gamepads => gamepads;

    public event EventHandler<PlayerInputSourceChangedEventArgs>? ActiveSourceChanged;

    public void ApplyBindings()
    {
        using Activity? activity = InteractionDiagnostics.Activities.StartActivity(
            "PlayerInput.ApplyBindings");
        activity?.SetTag("interaction.player", Player);
        try
        {
            IReadOnlyList<ActionMap> effective = ActionBindingCompiler.Compile(maps, Bindings);
            Actions.ReplaceMaps(effective);
            activity?.SetTag("interaction.binding.apply_outcome", "applied");
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag("error.type", exception.GetType().FullName);
            throw;
        }
    }

    public void ApplyBindingOverrides(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using Activity? activity = InteractionDiagnostics.Activities.StartActivity(
            "PlayerInput.ApplyBindingOverrides");
        activity?.SetTag("interaction.player", Player);
        try
        {
            ActionBindingDocument replacement = ActionBindingDocument.Create(maps);
            replacement.LoadOverrides(json);
            IReadOnlyList<ActionMap> effective = ActionBindingCompiler.Compile(maps, replacement);
            Actions.ReplaceMaps(effective);
            Bindings = replacement;
            activity?.SetTag("interaction.binding.apply_outcome", "applied");
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag("error.type", exception.GetType().FullName);
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (IKeyboard keyboard in keyboards.ToArray())
        {
            RemoveKeyboard(keyboard);
        }

        foreach (IMouse mouse in mice.ToArray())
        {
            RemoveMouse(mouse);
        }

        foreach (ITouchscreen touchscreen in touchscreens.ToArray())
        {
            RemoveTouchscreen(touchscreen);
        }

        foreach (IGamepad gamepad in gamepads.ToArray())
        {
            RemoveGamepad(gamepad);
        }

        Actions.Dispose();
    }

    internal void AddKeyboard(IKeyboard keyboard)
    {
        if (keyboards.Add(keyboard))
        {
            Actions.AddKeyboard(keyboard);
            keyboard.KeyChanged += OnKeyChanged;
        }
    }

    internal void RemoveKeyboard(IKeyboard keyboard)
    {
        if (keyboards.Remove(keyboard))
        {
            keyboard.KeyChanged -= OnKeyChanged;
            Actions.RemoveKeyboard(keyboard);
            ClearSource(keyboard);
        }
    }

    internal void AddMouse(IMouse mouse)
    {
        if (mice.Add(mouse))
        {
            Actions.AddMouse(mouse);
            if (gestureMaps.Count != 0)
            {
                AddGestureRuntime(
                    mouse,
                    new(mouse, GestureContext, gestureClock!, maps: [.. gestureMaps]));
            }
            mouse.ButtonChanged += OnMouseButtonChanged;
            mouse.Moved += OnMouseMoved;
            mouse.RawMoved += OnRawMouseMoved;
            mouse.WheelChanged += OnMouseWheelChanged;
        }
    }

    internal void RemoveMouse(IMouse mouse)
    {
        if (mice.Remove(mouse))
        {
            RemoveGestureRuntime(mouse);
            mouse.ButtonChanged -= OnMouseButtonChanged;
            mouse.Moved -= OnMouseMoved;
            mouse.RawMoved -= OnRawMouseMoved;
            mouse.WheelChanged -= OnMouseWheelChanged;
            Actions.RemoveMouse(mouse);
            ClearSource(mouse);
        }
    }

    internal void AddTouchscreen(ITouchscreen touchscreen)
    {
        if (touchscreens.Add(touchscreen))
        {
            if (gestureMaps.Count != 0)
            {
                AddGestureRuntime(
                    touchscreen,
                    new(touchscreen, GestureContext, gestureClock!, [.. gestureMaps]));
            }

            touchscreen.TouchChanged += OnTouchChanged;
        }
    }

    internal void RemoveTouchscreen(ITouchscreen touchscreen)
    {
        if (touchscreens.Remove(touchscreen))
        {
            RemoveGestureRuntime(touchscreen);
            touchscreen.TouchChanged -= OnTouchChanged;
            ClearSource(touchscreen);
        }
    }

    internal void AddGamepad(IGamepad gamepad)
    {
        if (gamepads.Add(gamepad))
        {
            Actions.AddGamepad(gamepad, Player);
            gamepad.StateChanged += OnGamepadStateChanged;
        }
    }

    internal void RemoveGamepad(IGamepad gamepad)
    {
        if (gamepads.Remove(gamepad))
        {
            gamepad.StateChanged -= OnGamepadStateChanged;
            Actions.RemoveGamepad(gamepad);
            ClearSource(gamepad);
        }
    }

    internal void Cancel(IWindowInput windowInput)
    {
        foreach (IKeyboard keyboard in windowInput.Keyboards.Where(keyboards.Contains))
        {
            Actions.CancelSource(keyboard);
        }

        foreach (IMouse mouse in windowInput.Mice.Where(mice.Contains))
        {
            if (gestureRuntimes.TryGetValue(mouse, out GestureRuntime? gestures))
            {
                gestures.Cancel();
            }

            Actions.CancelSource(mouse);
        }

        foreach (ITouchscreen touchscreen in windowInput.Touchscreens.Where(touchscreens.Contains))
        {
            if (gestureRuntimes.TryGetValue(touchscreen, out GestureRuntime? gestures))
            {
                gestures.Cancel();
            }

            Actions.CancelSource(touchscreen);
        }
    }

    private void OnKeyChanged(object? sender, KeyChangedEventArgs eventArgs)
    {
        if (sender is IKeyboard keyboard && eventArgs.IsPressed && !eventArgs.IsRepeat)
        {
            SetSource(new PlayerInputSource.Keyboard(keyboard));
        }
    }

    private void OnMouseButtonChanged(object? sender, MouseButtonChangedEventArgs eventArgs)
    {
        if (sender is IMouse mouse && eventArgs.IsPressed)
        {
            SetSource(new PlayerInputSource.Mouse(mouse));
        }
    }

    private void OnMouseMoved(object? sender, MouseMovedEventArgs eventArgs)
    {
        if (sender is IMouse mouse
            && eventArgs.Delta.Length() >= options.MouseMovementThreshold)
        {
            SetSource(new PlayerInputSource.Mouse(mouse));
        }
    }

    private void OnRawMouseMoved(object? sender, RawMouseMovedEventArgs eventArgs)
    {
        if (sender is IMouse mouse
            && eventArgs.Delta.Length() >= options.MouseMovementThreshold)
        {
            SetSource(new PlayerInputSource.Mouse(mouse));
        }
    }

    private void OnMouseWheelChanged(object? sender, MouseWheelChangedEventArgs eventArgs)
    {
        if (sender is IMouse mouse && eventArgs.Delta != Vector2.Zero)
        {
            SetSource(new PlayerInputSource.Mouse(mouse));
        }
    }

    private void OnTouchChanged(object? sender, TouchChangedEventArgs eventArgs)
    {
        if (sender is ITouchscreen touchscreen && eventArgs.Touch.Phase == TouchPhase.Began)
        {
            SetSource(new PlayerInputSource.Touch(touchscreen));
        }
    }

    private void OnGamepadStateChanged(object? sender, GamepadStateChangedEventArgs eventArgs)
    {
        if (sender is not IGamepad gamepad)
        {
            return;
        }

        GamepadButtons pressed = eventArgs.Current.Buttons & ~eventArgs.Previous.Buttons;
        bool meaningful = pressed != GamepadButtons.None
            || CrossedThreshold(eventArgs.Previous.LeftStick, eventArgs.Current.LeftStick)
            || CrossedThreshold(eventArgs.Previous.RightStick, eventArgs.Current.RightStick)
            || CrossedThreshold(eventArgs.Previous.LeftTrigger, eventArgs.Current.LeftTrigger)
            || CrossedThreshold(eventArgs.Previous.RightTrigger, eventArgs.Current.RightTrigger);
        if (meaningful)
        {
            SetSource(new PlayerInputSource.Gamepad(gamepad));
        }
    }

    private bool CrossedThreshold(Vector2 previous, Vector2 current) =>
        previous != current
        && current.Length() >= options.GamepadStickThreshold;

    private bool CrossedThreshold(float previous, float current) =>
        previous != current
        && current >= options.GamepadTriggerThreshold;

    private void SetSource(PlayerInputSource? source)
    {
        if (Equals(ActiveSource, source))
        {
            return;
        }

        PlayerInputSource? previous = ActiveSource;
        ActiveSource = source;
        InteractionDiagnostics.SourceChanges.Add(
            1,
            [new("source_type", GetSourceType(source))]);
        ActiveSourceChanged?.Invoke(this, new(previous, source));
    }

    private static string GetSourceType(PlayerInputSource? source) => source switch
    {
        PlayerInputSource.Keyboard => "keyboard",
        PlayerInputSource.Mouse => "mouse",
        PlayerInputSource.Touch => "touch",
        PlayerInputSource.Gamepad => "gamepad",
        _ => "none",
    };

    private void ClearSource(object device)
    {
        object? activeDevice = ActiveSource switch
        {
            PlayerInputSource.Keyboard source => source.Device,
            PlayerInputSource.Mouse source => source.Device,
            PlayerInputSource.Touch source => source.Device,
            PlayerInputSource.Gamepad source => source.Device,
            _ => null,
        };
        if (ReferenceEquals(activeDevice, device))
        {
            SetSource(null);
        }
    }

    private void AddGestureRuntime(object source, GestureRuntime runtime)
    {
        runtime.Recognized += OnGestureRecognized;
        gestureRuntimes.Add(source, runtime);
    }

    private void RemoveGestureRuntime(object source)
    {
        if (gestureRuntimes.Remove(source, out GestureRuntime? runtime))
        {
            runtime.Recognized -= OnGestureRecognized;
            runtime.Dispose();
        }
    }

    private void OnGestureRecognized(object? sender, GestureRecognizedEventArgs eventArgs)
    {
        object? source = gestureRuntimes
            .FirstOrDefault(pair => ReferenceEquals(pair.Value, sender))
            .Key;
        if (source is not null)
        {
            Actions.ApplyGesture(eventArgs, source);
        }
    }

    private static void ValidateGestureMaps(IEnumerable<GestureMap> maps)
    {
        foreach (GestureBinding binding in maps.SelectMany(map => map.Bindings))
        {
            bool compatible = binding.Intent switch
            {
                InputAction<bool> => binding.ValueType == typeof(bool),
                InputAction<float> => binding.ValueType == typeof(float),
                InputAction<Vector2> => binding.ValueType == typeof(Vector2),
                _ => false,
            };
            if (!compatible)
            {
                throw new ArgumentException(
                    $"Gesture '{binding.Kind}' has an incompatible action value type.",
                    nameof(maps));
            }
        }
    }
}
