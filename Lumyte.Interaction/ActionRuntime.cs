using System.Numerics;

using Lumyte.Input;

namespace Lumyte.Interaction;

public sealed class ActionRuntime : IDisposable
{
    private static readonly GamepadButtons[] s_gamepadButtons =
        [.. Enum.GetValues<GamepadButtons>().Where(button => button != GamepadButtons.None)];

    private readonly Dictionary<InputAction<bool>, HashSet<(object Source, InputControl<bool> Control)>> activeButtons = [];
    private readonly Dictionary<(object Source, InputControl<bool> Control), ActionBinding<bool>> activeButtonBindings = [];
    private readonly Dictionary<(object Source, InputControl<bool> Control), (Vector2CompositeBinding Binding, CompositePart Part)> activeCompositeBindings = [];
    private readonly Dictionary<(object Source, Vector2CompositeBinding Binding), CompositeButtonState> compositeStates = [];
    private readonly Dictionary<ContributionKey, object> contributions = [];
    private BindingEntry[] bindings;
    private readonly List<IGamepad> gamepads = [];
    private readonly Dictionary<IGamepad, int> gamepadPlayers = [];
    private readonly List<IKeyboard> keyboards = [];
    private readonly List<IMouse> mice = [];
    private readonly InteractionContext context;
    private readonly Dictionary<InteractionIntent, ActionPhase> phases = [];
    private readonly HashSet<InteractionIntent> performedActions = [];
    private readonly Dictionary<InteractionIntent, object> transientGestureActions = [];
    private readonly HashSet<ContributionKey> transientContributions = [];
    private readonly Dictionary<InteractionIntent, object?> values = [];
    private IGamepad? cancelingGamepad;
    private bool disposed;

    public ActionRuntime(
        IKeyboard keyboard,
        InteractionContext context,
        params ReadOnlySpan<ActionMap> maps)
        : this(context, maps.ToArray(), [keyboard], [], [])
    {
    }

    public ActionRuntime(
        InteractionContext context,
        IEnumerable<ActionMap> maps,
        IEnumerable<IKeyboard>? keyboards = null,
        IEnumerable<IMouse>? mice = null,
        IEnumerable<IGamepad>? gamepads = null)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        ArgumentNullException.ThrowIfNull(maps);
        bindings = CompileBindings(maps);
        foreach (IKeyboard keyboard in keyboards ?? [])
        {
            AddKeyboard(keyboard);
        }

        foreach (IMouse mouse in mice ?? [])
        {
            AddMouse(mouse);
        }

        int player = 0;
        foreach (IGamepad gamepad in gamepads ?? [])
        {
            AddGamepad(gamepad, player++);
        }
    }

    public event EventHandler<ActionChangedEventArgs>? ActionChanged;

    public event EventHandler<ActionValueChangedEventArgs>? ValueChanged;

    public event EventHandler<ActionPhaseChangedEventArgs>? PhaseChanged;

    public T GetValue<T>(InputAction<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return values.TryGetValue(action, out object? value) ? (T)value! : default!;
    }

    public ActionPhase GetPhase<T>(InputAction<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return phases.GetValueOrDefault(action, ActionPhase.Waiting);
    }

    public bool WasPerformed<T>(InputAction<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return performedActions.Contains(action);
    }

    public void ResetTransientValues()
    {
        performedActions.Clear();
        InputAction<Vector2>[] affectedActions =
        [.. transientContributions
            .Select(key => (InputAction<Vector2>)key.Action)
            .Distinct()];
        foreach (ContributionKey key in transientContributions)
        {
            contributions.Remove(key);
        }

        transientContributions.Clear();
        foreach (InputAction<Vector2> action in affectedActions)
        {
            SetValue(action, Aggregate(action));
        }

        foreach (InteractionIntent action in transientGestureActions.Keys)
        {
            switch (action)
            {
                case InputAction<bool> boolean:
                    SetValue(boolean, false);
                    break;
                case InputAction<float> scalar:
                    SetValue(scalar, 0);
                    break;
                case InputAction<Vector2> vector:
                    SetValue(vector, Vector2.Zero);
                    break;
            }
        }

        transientGestureActions.Clear();
    }

    public void ReplaceMaps(IEnumerable<ActionMap> maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        BindingEntry[] replacement = CompileBindings(maps);
        CancelAllValues();
        activeButtons.Clear();
        activeButtonBindings.Clear();
        activeCompositeBindings.Clear();
        compositeStates.Clear();
        contributions.Clear();
        transientContributions.Clear();
        transientGestureActions.Clear();
        performedActions.Clear();
        bindings = replacement;
        ResampleDevices();
    }

    public void AddGamepad(IGamepad gamepad, int player)
    {
        ArgumentNullException.ThrowIfNull(gamepad);
        if (player < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(player));
        }

        if (gamepadPlayers.ContainsKey(gamepad))
        {
            throw new ArgumentException("The gamepad is already registered.", nameof(gamepad));
        }

        gamepads.Add(gamepad);
        gamepadPlayers.Add(gamepad, player);
        gamepad.StateChanged += OnGamepadStateChanged;
    }

    public bool RemoveGamepad(IGamepad gamepad)
    {
        ArgumentNullException.ThrowIfNull(gamepad);
        if (!gamepadPlayers.ContainsKey(gamepad))
        {
            return false;
        }

        cancelingGamepad = gamepad;
        OnGamepadStateChanged(gamepad, new(gamepad.State, default));
        cancelingGamepad = null;
        gamepadPlayers.Remove(gamepad);
        gamepad.StateChanged -= OnGamepadStateChanged;
        gamepads.Remove(gamepad);
        return true;
    }

    public void AddKeyboard(IKeyboard keyboard)
    {
        ArgumentNullException.ThrowIfNull(keyboard);
        if (keyboards.Contains(keyboard))
        {
            throw new ArgumentException("The keyboard is already registered.", nameof(keyboard));
        }

        keyboards.Add(keyboard);
        keyboard.KeyChanged += OnKeyChanged;
    }

    public bool RemoveKeyboard(IKeyboard keyboard)
    {
        ArgumentNullException.ThrowIfNull(keyboard);
        if (!keyboards.Remove(keyboard))
        {
            return false;
        }

        keyboard.KeyChanged -= OnKeyChanged;
        CancelButtonsFrom(keyboard);
        return true;
    }

    public void AddMouse(IMouse mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (mice.Contains(mouse))
        {
            throw new ArgumentException("The mouse is already registered.", nameof(mouse));
        }

        mice.Add(mouse);
        mouse.ButtonChanged += OnMouseButtonChanged;
        mouse.Moved += OnMouseMoved;
        mouse.RawMoved += OnMouseRawMoved;
        mouse.WheelChanged += OnMouseWheelChanged;
    }

    public bool RemoveMouse(IMouse mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (!mice.Remove(mouse))
        {
            return false;
        }

        mouse.ButtonChanged -= OnMouseButtonChanged;
        mouse.Moved -= OnMouseMoved;
        mouse.RawMoved -= OnMouseRawMoved;
        mouse.WheelChanged -= OnMouseWheelChanged;
        CancelButtonsFrom(mouse);
        ResetTransientValues();
        return true;
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

        foreach (IGamepad gamepad in gamepads.ToArray())
        {
            RemoveGamepad(gamepad);
        }
    }

    private void OnKeyChanged(object? sender, KeyChangedEventArgs eventArgs)
    {
        if (sender is not IKeyboard keyboard || eventArgs.IsRepeat)
        {
            return;
        }

        UpdateButton(keyboard, InputControls.Key(eventArgs.Key), eventArgs.IsPressed);
    }

    private void OnMouseButtonChanged(object? sender, MouseButtonChangedEventArgs eventArgs)
    {
        if (sender is IMouse mouse)
        {
            UpdateButton(mouse, InputControls.MouseButton(eventArgs.Button), eventArgs.IsPressed);
        }
    }

    private void OnMouseMoved(object? sender, MouseMovedEventArgs eventArgs)
    {
        if (sender is IMouse mouse)
        {
            UpdateTransient(mouse, InputControls.MouseDelta, eventArgs.Delta);
        }
    }

    private void OnMouseRawMoved(object? sender, RawMouseMovedEventArgs eventArgs)
    {
        if (sender is IMouse mouse)
        {
            UpdateTransient(mouse, InputControls.MouseRawDelta, eventArgs.Delta);
        }
    }

    private void OnMouseWheelChanged(object? sender, MouseWheelChangedEventArgs eventArgs)
    {
        if (sender is IMouse mouse)
        {
            UpdateTransient(mouse, InputControls.MouseWheel, eventArgs.Delta);
        }
    }

    private void OnGamepadStateChanged(object? sender, GamepadStateChangedEventArgs eventArgs)
    {
        if (sender is not IGamepad gamepad)
        {
            return;
        }

        if (!gamepadPlayers.TryGetValue(gamepad, out int player))
        {
            return;
        }
        bool canceled = ReferenceEquals(gamepad, cancelingGamepad);
        foreach (GamepadButtons button in s_gamepadButtons)
        {
            if (eventArgs.Previous.IsPressed(button) == eventArgs.Current.IsPressed(button))
            {
                continue;
            }

            UpdateButton(
                gamepad,
                InputControls.GamepadButton(button, player),
                eventArgs.Current.IsPressed(button),
                InputControls.GamepadButton(button),
                canceled);
        }

        UpdateContinuous(
            gamepad,
            InputControls.GamepadLeftStick(player),
            InputControls.GamepadLeftStick(),
            eventArgs.Previous.LeftStick,
            eventArgs.Current.LeftStick,
            canceled);
        UpdateContinuous(
            gamepad,
            InputControls.GamepadRightStick(player),
            InputControls.GamepadRightStick(),
            eventArgs.Previous.RightStick,
            eventArgs.Current.RightStick,
            canceled);
        UpdateContinuous(
            gamepad,
            InputControls.GamepadLeftTrigger(player),
            InputControls.GamepadLeftTrigger(),
            eventArgs.Previous.LeftTrigger,
            eventArgs.Current.LeftTrigger,
            canceled);
        UpdateContinuous(
            gamepad,
            InputControls.GamepadRightTrigger(player),
            InputControls.GamepadRightTrigger(),
            eventArgs.Previous.RightTrigger,
            eventArgs.Current.RightTrigger,
            canceled);
    }

    private void UpdateButton(
        object source,
        InputControl<bool> control,
        bool isPressed,
        InputControl<bool>? alias = null,
        bool canceled = false)
    {
        var sourceControl = (source, control);
        UpdateCompositeButton(source, control, alias, isPressed, canceled);
        if (isPressed)
        {
            ActionBinding<bool>? binding = Resolve(control, alias);
            if (binding is null)
            {
                return;
            }

            activeButtonBindings[sourceControl] = binding;
            if (!activeButtons.TryGetValue(
                binding.TypedAction,
                out HashSet<(object Source, InputControl<bool> Control)>? controls))
            {
                controls = [];
                activeButtons.Add(binding.TypedAction, controls);
            }

            bool wasActive = controls.Count != 0;
            controls.Add(sourceControl);
            if (!wasActive)
            {
                SetValue(binding.TypedAction, binding.Process(true));
            }

            return;
        }

        if (!activeButtonBindings.Remove(sourceControl, out ActionBinding<bool>? released)
            || !activeButtons.TryGetValue(
                released.TypedAction,
                out HashSet<(object Source, InputControl<bool> Control)>? active))
        {
            return;
        }

        active.Remove(sourceControl);
        if (active.Count == 0)
        {
            activeButtons.Remove(released.TypedAction);
            SetValue(released.TypedAction, released.Process(false), canceled);
        }
    }

    private void UpdateCompositeButton(
        object source,
        InputControl<bool> control,
        InputControl<bool>? alias,
        bool isPressed,
        bool canceled)
    {
        var sourceControl = (source, control);
        Vector2CompositeBinding binding;
        CompositePart part;
        if (isPressed)
        {
            if (ResolveComposite(control, alias) is not CompositeMatch match)
            {
                return;
            }

            binding = match.Binding;
            part = match.Part;
            activeCompositeBindings[sourceControl] = (binding, part);
        }
        else if (activeCompositeBindings.Remove(sourceControl, out var active))
        {
            binding = active.Binding;
            part = active.Part;
        }
        else
        {
            return;
        }

        var stateKey = (source, binding);
        CompositeButtonState state = compositeStates.GetValueOrDefault(stateKey).With(part, isPressed);
        if (state.Value == Vector2.Zero)
        {
            compositeStates.Remove(stateKey);
        }
        else
        {
            compositeStates[stateKey] = state;
        }

        SetContribution(
            binding.TypedAction,
            source,
            binding,
            binding.Process(state.Value),
            canceled);
    }

    private void CancelButtonsFrom(object source)
    {
        InputControl<bool>[] controls =
        [.. activeButtonBindings.Keys
            .Where(key => ReferenceEquals(key.Source, source))
            .Select(key => key.Control)
            .Concat(activeCompositeBindings.Keys
                .Where(key => ReferenceEquals(key.Source, source))
                .Select(key => key.Control))
            .Distinct()];
        foreach (InputControl<bool> control in controls)
        {
            UpdateButton(source, control, false, canceled: true);
        }
    }

    private void CancelAllValues()
    {
        contributions.Clear();
        foreach ((InteractionIntent action, object? value) in values.ToArray())
        {
            switch (action, value)
            {
                case (InputAction<bool> boolean, bool current) when current:
                    SetValue(boolean, false, canceled: true);
                    break;
                case (InputAction<float> scalar, float current) when current != 0:
                    SetValue(scalar, 0, canceled: true);
                    break;
                case (InputAction<Vector2> vector, Vector2 current) when current != Vector2.Zero:
                    SetValue(vector, Vector2.Zero, canceled: true);
                    break;
            }
        }
    }

    private void ResampleDevices()
    {
        foreach (IKeyboard keyboard in keyboards)
        {
            foreach (Key key in Enum.GetValues<Key>())
            {
                if (key != Key.Unknown && keyboard.IsKeyPressed(key))
                {
                    UpdateButton(keyboard, InputControls.Key(key), true);
                }
            }
        }

        foreach (IMouse mouse in mice)
        {
            foreach (MouseButton button in Enum.GetValues<MouseButton>())
            {
                if (mouse.IsButtonPressed(button))
                {
                    UpdateButton(mouse, InputControls.MouseButton(button), true);
                }
            }
        }

        foreach (IGamepad gamepad in gamepads)
        {
            OnGamepadStateChanged(gamepad, new(default, gamepad.State));
        }
    }

    private void UpdateTransient(
        object source,
        InputControl<Vector2> control,
        Vector2 value)
    {
        ActionBinding<Vector2>? binding = Resolve(control);
        if (binding is null)
        {
            return;
        }

        var key = new ContributionKey(binding.TypedAction, source, binding);
        SetContribution(
            binding.TypedAction,
            source,
            binding,
            binding.Process(value),
            canceled: false);
        transientContributions.Add(key);
    }

    private void UpdateContinuous<T>(
        object source,
        InputControl<T> control,
        InputControl<T> alias,
        T previous,
        T value,
        bool canceled = false)
    {
        if (EqualityComparer<T>.Default.Equals(previous, value))
        {
            return;
        }

        ActionBinding<T>? binding = Resolve(control, alias);
        if (binding is not null)
        {
            SetContribution(
                binding.TypedAction,
                source,
                binding,
                binding.Process(value),
                canceled);
        }
    }

    private void SetContribution<T>(
        InputAction<T> action,
        object source,
        ActionBinding binding,
        T value,
        bool canceled)
    {
        var key = new ContributionKey(action, source, binding);
        if (EqualityComparer<T>.Default.Equals(value, default!))
        {
            contributions.Remove(key);
        }
        else
        {
            contributions[key] = value!;
        }

        SetValue(action, Aggregate(action), canceled);
    }

    private T Aggregate<T>(InputAction<T> action)
    {
        T[] actionValues =
        [.. contributions
            .Where(pair => ReferenceEquals(pair.Key.Action, action))
            .Select(pair => (T)pair.Value)];
        if (typeof(T) == typeof(Vector2))
        {
            Vector2[] vectors = [.. actionValues.Cast<Vector2>()];
            Vector2 result = action.Aggregation == ActionValueAggregation.Cumulative
                ? vectors.Aggregate(Vector2.Zero, (sum, value) => sum + value)
                : vectors.Aggregate(Vector2.Zero, (best, value) =>
                    value.LengthSquared() > best.LengthSquared() ? value : best);
            return (T)(object)result;
        }

        if (typeof(T) == typeof(float))
        {
            float[] scalars = [.. actionValues.Cast<float>()];
            float result = action.Aggregation == ActionValueAggregation.Cumulative
                ? scalars.Sum()
                : scalars.Aggregate(0f, (best, value) =>
                    MathF.Abs(value) > MathF.Abs(best) ? value : best);
            return (T)(object)result;
        }

        return actionValues.FirstOrDefault()!;
    }

    private CompositeMatch? ResolveComposite(
        InputControl<bool> control,
        InputControl<bool>? alias)
    {
        Vector2CompositeBinding? best = null;
        CompositePart bestPart = default;
        int bestPriority = int.MinValue;
        int bestSpecificity = int.MinValue;
        bool conflict = false;
        foreach (BindingEntry entry in bindings)
        {
            if (entry.Binding is not Vector2CompositeBinding binding
                || !entry.When.Evaluate(context)
                || !binding.TryMatch(control, alias, out CompositePart part, out int specificity))
            {
                continue;
            }

            if (entry.Priority > bestPriority
                || (entry.Priority == bestPriority && specificity > bestSpecificity))
            {
                best = binding;
                bestPart = part;
                bestPriority = entry.Priority;
                bestSpecificity = specificity;
                conflict = false;
            }
            else if (entry.Priority == bestPriority && specificity == bestSpecificity)
            {
                conflict = true;
            }
        }

        return best is not null && !conflict ? new(best, bestPart) : null;
    }

    private ActionBinding<T>? Resolve<T>(
        InputControl<T> control,
        InputControl<T>? alias = null)
    {
        ActionBinding<T>? best = null;
        int bestPriority = int.MinValue;
        int bestSpecificity = int.MinValue;
        bool conflict = false;
        foreach (BindingEntry entry in bindings)
        {
            if (entry.Binding is not ActionBinding<T> binding
                || !entry.When.Evaluate(context))
            {
                continue;
            }

            int specificity = binding.TypedControl == control
                ? 2
                : alias is not null && binding.TypedControl == alias ? 1 : 0;
            if (specificity == 0)
            {
                continue;
            }

            if (entry.Priority > bestPriority
                || (entry.Priority == bestPriority && specificity > bestSpecificity))
            {
                best = binding;
                bestPriority = entry.Priority;
                bestSpecificity = specificity;
                conflict = false;
            }
            else if (entry.Priority == bestPriority && specificity == bestSpecificity)
            {
                conflict = true;
            }
        }

        return conflict ? null : best;
    }

    private void SetValue<T>(InputAction<T> action, T value, bool canceled = false)
    {
        bool hadPrevious = values.TryGetValue(action, out object? previous);
        T typedPrevious = hadPrevious && previous is T existing ? existing : default!;
        if (EqualityComparer<T>.Default.Equals(typedPrevious, value))
        {
            return;
        }

        values[action] = value;
        ValueChanged?.Invoke(this, new(action, previous, value));
        bool wasActive = !EqualityComparer<T>.Default.Equals(typedPrevious, default!);
        bool isActive = !EqualityComparer<T>.Default.Equals(value, default!);
        if (!wasActive && isActive)
        {
            SetPhase(action, ActionPhase.Started, value);
            SetPhase(action, ActionPhase.Performed, value);
        }
        else if (isActive)
        {
            SetPhase(action, ActionPhase.Performed, value);
        }
        else if (wasActive)
        {
            SetPhase(action, canceled ? ActionPhase.Canceled : ActionPhase.Completed, value);
        }

        if (action is InputAction<bool> booleanAction && value is bool booleanValue)
        {
            ActionChanged?.Invoke(this, new(booleanAction, booleanValue));
        }
    }

    private void SetPhase<T>(InputAction<T> action, ActionPhase phase, T value)
    {
        phases[action] = phase;
        InteractionDiagnostics.ActionPhaseChanges.Add(
            1,
            new("phase", phase.ToString()),
            new("value_type", GetValueTypeName<T>()));
        if (phase == ActionPhase.Performed)
        {
            performedActions.Add(action);
        }

        PhaseChanged?.Invoke(this, new(action, phase, value));
    }

    private static string GetValueTypeName<T>() => typeof(T) == typeof(bool)
        ? "button"
        : typeof(T) == typeof(float)
            ? "scalar"
            : typeof(T) == typeof(Vector2) ? "vector2" : "other";

    internal void CancelSource(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        HashSet<InteractionIntent> canceledActions =
        [.. activeButtonBindings
            .Where(pair => ReferenceEquals(pair.Key.Source, source))
            .Select(pair => (InteractionIntent)pair.Value.TypedAction),
            .. activeCompositeBindings
                .Where(pair => ReferenceEquals(pair.Key.Source, source))
                .Select(pair => (InteractionIntent)pair.Value.Binding.TypedAction)];
        CancelButtonsFrom(source);
        InteractionIntent[] affected =
        [.. contributions.Keys
            .Where(key => ReferenceEquals(key.Source, source))
            .Select(key => key.Action)
            .Distinct()];
        foreach (ContributionKey key in contributions.Keys
            .Where(key => ReferenceEquals(key.Source, source))
            .ToArray())
        {
            contributions.Remove(key);
            transientContributions.Remove(key);
        }

        foreach (InteractionIntent action in affected)
        {
            canceledActions.Add(action);
            switch (action)
            {
                case InputAction<float> scalar:
                    SetValue(scalar, Aggregate(scalar), canceled: true);
                    break;
                case InputAction<Vector2> vector:
                    SetValue(vector, Aggregate(vector), canceled: true);
                    break;
            }
        }

        foreach (InteractionIntent action in transientGestureActions
            .Where(pair => ReferenceEquals(pair.Value, source))
            .Select(pair => pair.Key)
            .ToArray())
        {
            transientGestureActions.Remove(action);
            canceledActions.Add(action);
            switch (action)
            {
                case InputAction<bool> boolean:
                    SetValue(boolean, false, canceled: true);
                    break;
                case InputAction<float> scalar:
                    SetValue(scalar, 0, canceled: true);
                    break;
                case InputAction<Vector2> vector:
                    SetValue(vector, Vector2.Zero, canceled: true);
                break;
            }
        }

        performedActions.ExceptWith(canceledActions);
    }

    internal void ApplyGesture(GestureRecognizedEventArgs gesture, object source)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        ArgumentNullException.ThrowIfNull(source);
        switch (gesture.Intent)
        {
            case InputAction<bool> boolean:
                SetValue(boolean, true);
                transientGestureActions[boolean] = source;
                break;
            case InputAction<float> scalar:
                SetValue(scalar, gesture.Scale);
                transientGestureActions[scalar] = source;
                break;
            case InputAction<Vector2> vector:
                SetValue(vector, gesture.Delta);
                transientGestureActions[vector] = source;
                break;
            default:
                throw new InvalidOperationException(
                    $"Gesture intent '{gesture.Intent}' is not a supported input action.");
        }
    }

    private sealed record BindingEntry(
        ActionBinding Binding,
        ContextCondition When,
        int Priority);

    private static BindingEntry[] CompileBindings(IEnumerable<ActionMap> maps)
    {
        var compiled = new List<BindingEntry>();
        foreach (ActionMap map in maps)
        {
            foreach (ActionBinding binding in map.Bindings)
            {
                compiled.Add(new(binding, map.When, map.Priority));
            }
        }

        return [.. compiled];
    }

    private readonly record struct CompositeMatch(
        Vector2CompositeBinding Binding,
        CompositePart Part);

    private readonly record struct ContributionKey(
        InteractionIntent Action,
        object Source,
        ActionBinding Binding);
}
