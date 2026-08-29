using Lumyte.Input;

namespace Lumyte.Interaction;

public sealed class ActionRuntime : IDisposable
{
    private readonly Dictionary<InputAction<bool>, HashSet<InputControl<bool>>> activeControls = [];
    private readonly Dictionary<Key, ActionBinding<bool>> activeKeys = [];
    private readonly InteractionContext context;
    private readonly IKeyboard keyboard;
    private readonly IReadOnlyList<ActionMap> maps;
    private readonly InteractionResolver resolver = new();
    private bool disposed;

    public ActionRuntime(
        IKeyboard keyboard,
        InteractionContext context,
        params ReadOnlySpan<ActionMap> maps)
    {
        this.keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.maps = maps.ToArray();
        keyboard.KeyChanged += OnKeyChanged;
    }

    public event EventHandler<ActionChangedEventArgs>? ActionChanged;

    public bool GetValue(InputAction<bool> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return activeControls.TryGetValue(action, out HashSet<InputControl<bool>>? controls)
            && controls.Count != 0;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        keyboard.KeyChanged -= OnKeyChanged;
    }

    private void OnKeyChanged(object? sender, KeyChangedEventArgs eventArgs)
    {
        if (eventArgs.IsRepeat)
        {
            return;
        }

        if (eventArgs.IsPressed)
        {
            Press(eventArgs.Key);
        }
        else
        {
            Release(eventArgs.Key);
        }
    }

    private void Press(Key key)
    {
        InputControl<bool> control = InputControls.Key(key);
        InteractionCandidate<ActionBinding<bool>>[] candidates =
        [
            .. maps.SelectMany(map =>
                map.Bindings
                    .OfType<ActionBinding<bool>>()
                    .Where(binding => binding.TypedControl == control)
                    .Select(binding => new InteractionCandidate<ActionBinding<bool>>(binding)
                    {
                        When = map.When,
                        Priority = map.Priority,
                        Specificity = 1,
                    })),
        ];
        if (resolver.Resolve(candidates, context)
            is not InteractionResolution<ActionBinding<bool>>.Match match)
        {
            return;
        }

        ActionBinding<bool> binding = match.Candidate.Value;
        activeKeys[key] = binding;
        if (!activeControls.TryGetValue(binding.TypedAction, out HashSet<InputControl<bool>>? controls))
        {
            controls = [];
            activeControls.Add(binding.TypedAction, controls);
        }

        bool wasActive = controls.Count != 0;
        controls.Add(control);
        if (!wasActive)
        {
            ActionChanged?.Invoke(this, new(binding.TypedAction, true));
        }
    }

    private void Release(Key key)
    {
        if (!activeKeys.Remove(key, out ActionBinding<bool>? binding)
            || !activeControls.TryGetValue(binding.TypedAction, out HashSet<InputControl<bool>>? controls))
        {
            return;
        }

        controls.Remove(InputControls.Key(key));
        if (controls.Count == 0)
        {
            activeControls.Remove(binding.TypedAction);
            ActionChanged?.Invoke(this, new(binding.TypedAction, false));
        }
    }
}
