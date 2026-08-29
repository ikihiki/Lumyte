using Lumyte.Core.Time;
using Lumyte.Input;

namespace Lumyte.Interaction;

public sealed class KeybindingRuntime : IDisposable
{
    private static readonly Duration s_defaultChordTimeout = Duration.FromSeconds(1);
    private readonly List<KeyStroke> sequence = [];
    private readonly IReadOnlyList<Keybinding> bindings;
    private readonly IMonotonicClock clock;
    private readonly InteractionContext context;
    private readonly IKeyboard keyboard;
    private readonly Duration chordTimeout;
    private ModifierKeys modifiers;
    private TimePoint? deadline;
    private Keybinding? pendingExact;
    private bool disposed;

    public KeybindingRuntime(
        IKeyboard keyboard,
        InteractionContext context,
        IMonotonicClock clock,
        KeybindingMap map,
        Duration? chordTimeout = null)
    {
        this.keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentNullException.ThrowIfNull(map);
        bindings = map.Bindings;
        this.chordTimeout = chordTimeout ?? s_defaultChordTimeout;
        keyboard.KeyChanged += OnKeyChanged;
    }

    public event EventHandler<CommandInvokedEventArgs>? CommandInvoked;

    public event EventHandler<KeybindingConflictEventArgs>? ConflictDetected;

    public bool IsChordPending => sequence.Count != 0;

    public void Update()
    {
        if (deadline is not TimePoint timeout || clock.Now < timeout)
        {
            return;
        }

        Keybinding? binding = pendingExact;
        ResetSequence();
        if (binding is not null && binding.When.Evaluate(context))
        {
            Invoke(binding);
        }
    }

    public void Reset()
    {
        modifiers = ModifierKeys.None;
        ResetSequence();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        keyboard.KeyChanged -= OnKeyChanged;
        Reset();
    }

    private void OnKeyChanged(object? sender, KeyChangedEventArgs eventArgs)
    {
        UpdateModifier(eventArgs.Key, eventArgs.IsPressed);
        if (!eventArgs.IsPressed || eventArgs.IsRepeat || IsModifier(eventArgs.Key))
        {
            return;
        }

        Update();
        Process(new(eventArgs.Key, modifiers), retry: true);
    }

    private void Process(KeyStroke stroke, bool retry)
    {
        sequence.Add(stroke);
        Keybinding[] candidates =
        [
            .. bindings.Where(binding =>
                binding.When.Evaluate(context)
                && StartsWith(binding.Chord.Strokes, sequence)),
        ];
        if (candidates.Length == 0)
        {
            ResetSequence();
            if (retry)
            {
                Process(stroke, retry: false);
            }

            return;
        }

        Keybinding[] exact =
        [
            .. candidates.Where(binding => binding.Chord.Strokes.Count == sequence.Count),
        ];
        bool hasLonger = candidates.Any(binding => binding.Chord.Strokes.Count > sequence.Count);
        Keybinding? selected = Select(exact);
        if (selected is not null && !hasLonger)
        {
            ResetSequence();
            Invoke(selected);
            return;
        }

        pendingExact = selected;
        deadline = clock.Now + chordTimeout;
    }

    private Keybinding? Select(IReadOnlyList<Keybinding> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        int priority = candidates.Max(binding => binding.Priority);
        Keybinding[] best = [.. candidates.Where(binding => binding.Priority == priority)];
        if (best.Length != 1)
        {
            ConflictDetected?.Invoke(this, new(best));
            return null;
        }

        return best[0];
    }

    private void Invoke(Keybinding binding) =>
        CommandInvoked?.Invoke(this, new(binding.Command));

    private void ResetSequence()
    {
        sequence.Clear();
        deadline = null;
        pendingExact = null;
    }

    private void UpdateModifier(Key key, bool isPressed)
    {
        ModifierKeys modifier = key switch
        {
            Key.LeftControl or Key.RightControl => ModifierKeys.Control,
            Key.LeftShift or Key.RightShift => ModifierKeys.Shift,
            Key.LeftAlt or Key.RightAlt => ModifierKeys.Alt,
            Key.LeftSuper or Key.RightSuper => ModifierKeys.Meta,
            _ => ModifierKeys.None,
        };
        if (isPressed)
        {
            modifiers |= modifier;
        }
        else
        {
            modifiers &= ~modifier;
        }
    }

    private static bool IsModifier(Key key) => key is
        Key.LeftControl or Key.RightControl
        or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt
        or Key.LeftSuper or Key.RightSuper;

    private static bool StartsWith(
        IReadOnlyList<KeyStroke> chord,
        IReadOnlyList<KeyStroke> prefix)
    {
        if (prefix.Count > chord.Count)
        {
            return false;
        }

        for (int index = 0; index < prefix.Count; index++)
        {
            if (prefix[index] != chord[index])
            {
                return false;
            }
        }

        return true;
    }
}
