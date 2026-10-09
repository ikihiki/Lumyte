using Lumyte.Core.Time;
using Lumyte.StateMachines;

namespace Lumyte.Animation;

/// <summary>Owns a generic control instance and evaluates animation values using one captured clock time.</summary>
/// <typeparam name="TState">The external state identifier type.</typeparam>
/// <typeparam name="TContext">The application input and callback context type.</typeparam>
public sealed class AnimationStateMachine<TState, TContext>
    where TState : notnull
{
    private readonly IMonotonicClock _clock;

    private readonly CapturedClock _captured = new();
    private readonly AnimationStateMachineDefinition<TState, TContext> _definition;
    private readonly Dictionary<TState, Entry> _entries = [];

    private readonly Dictionary<State<AnimationStateContext<TContext>>, Entry> _controlEntries = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<StateMachineTrigger> _knownTriggers = [];
    private readonly List<StateMachineTrigger> _pending = [];
    private readonly List<StateMachineTrigger> _inputs = [];

    private readonly AnimationOutput _scratch = new();
    private readonly List<AnimationEventOccurrence> _markers = [];
    private readonly List<AnimationEventOccurrence> _incomingMarkers = [];
    private StateMachineInstance<AnimationStateContext<TContext>, StateMachineTrigger>? _instance;
    private Transition<AnimationStateContext<TContext>, StateMachineTrigger>? _selected;
    private Entry? _current;
    private TimePoint _lastObserved;
    private TimePoint _lastEventClock;
    private bool _observed;
    private bool _operating;
    private double _speed = 1;

    /// <summary>Initializes a new instance of the <see cref="AnimationStateMachine{TState, TContext}"/> class.</summary>
    /// <param name="clock">The clock.</param>
    /// <param name="definition">The definition.</param>
    public AnimationStateMachine(IMonotonicClock clock, ComposeAnimation.Definitions.StateMachine<TState, TContext> definition)
        : this(ValidateClock(clock), Compile(definition))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AnimationStateMachine{TState, TContext}"/> class.</summary>
    /// <param name="clock">The clock.</param>
    /// <param name="builder">The builder.</param>
    /// <param name="initialState">The initial state.</param>
    public AnimationStateMachine(IMonotonicClock clock, AnimationStateMachineBuilder<TState, TContext> builder, TState initialState)
        : this(ValidateClock(clock), Compile(builder, initialState))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AnimationStateMachine{TState, TContext}"/> class.</summary>
    /// <param name="clock">The clock.</param>
    /// <param name="definition">The definition.</param>
    public AnimationStateMachine(IMonotonicClock clock, AnimationStateMachineDefinition<TState, TContext> definition)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(definition);
        _clock = clock;
        _definition = definition;
        foreach (AnimationStateBinding<TState, TContext> binding in definition.Bindings)
        {
            var entry = new Entry(binding);
            _entries.Add(binding.Id, entry);
            _controlEntries.Add(binding.State, entry);
        }

        foreach (Transition<AnimationStateContext<TContext>, StateMachineTrigger> transition in definition.Control.Transitions)
        {
            _knownTriggers.Add(transition.Trigger);
        }
    }

    /// <summary>Gets the status.</summary>
    public AnimationStateMachineStatus Status { get; private set; }

    /// <summary>Gets the current state.</summary>
    public TState CurrentState => ActiveEntry().Binding.Id;

    /// <summary>Gets the position.</summary>
    public Duration Position => ActiveEntry().Playback?.Position ?? Duration.Zero;

    /// <summary>Gets or sets the speed.</summary>
    public double Speed
    {
        get => _speed;
        set
        {
            EnsureIdle();
            if (!double.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _operating = true;
            try
            {
                if (Status != AnimationStateMachineStatus.Stopped)
                {
                    CaptureTime();
                    _current!.Playback!.Speed = value;
                }

                _speed = value;
            }
            finally
            {
                _operating = false;
            }
        }
    }

    /// <summary>Creates the internal control instance, executes initial entry and starts playback at zero.</summary>
    /// <param name="context">The context.</param>
    public void Start(TContext context)
    {
        EnsureIdle();
        ValidateContext(context);
        if (Status != AnimationStateMachineStatus.Stopped)
        {
            throw new InvalidOperationException("The animation machine is already active.");
        }

        _operating = true;
        try
        {
            TimePoint now = CaptureTime();
            Entry initial = _entries[_definition.InitialState];
            _current = initial;
            Status = AnimationStateMachineStatus.Running;
            _pending.Clear();
            _lastEventClock = now;
            var input = new AnimationStateContext<TContext>(context, new AnimationStateInfo(Duration.Zero, initial.Binding.Timeline.Duration, false));
            _instance = new StateMachineInstance<AnimationStateContext<TContext>, StateMachineTrigger>(_definition.Control, input);
            _instance.Transitioned += OnTransitioned;
            Play(initial);
        }
        catch
        {
            Reset();
            throw;
        }
        finally
        {
            _operating = false;
        }
    }

    /// <summary>Freezes the current position without applying any values.</summary>
    public void Pause()
    {
        EnsureIdle();
        if (Status != AnimationStateMachineStatus.Running)
        {
            return;
        }

        _operating = true;
        try
        {
            CaptureTime();
            _current!.Playback!.Pause();
            Status = AnimationStateMachineStatus.Paused;
        }
        finally
        {
            _operating = false;
        }
    }

    /// <summary>Resumes paused playback from a new clock anchor.</summary>
    public void Resume()
    {
        EnsureIdle();
        if (Status != AnimationStateMachineStatus.Paused)
        {
            return;
        }

        _operating = true;
        try
        {
            CaptureTime();
            _current!.Playback!.Resume();
            Status = AnimationStateMachineStatus.Running;
        }
        finally
        {
            _operating = false;
        }
    }

    /// <summary>Discards current execution and pending inputs without applying or restoring consumer values.</summary>
    public void Stop()
    {
        EnsureIdle();
        Reset();
    }

    /// <summary>Queues a known trigger for the next running update, coalescing duplicate inputs.</summary>
    /// <param name="trigger">The trigger.</param>
    public void SetTrigger(StateMachineTrigger trigger)
    {
        EnsureIdle();
        ArgumentNullException.ThrowIfNull(trigger);
        if (Status == AnimationStateMachineStatus.Stopped)
        {
            throw new InvalidOperationException("The animation machine is stopped.");
        }

        if (!_knownTriggers.Contains(trigger))
        {
            throw new ArgumentException("The trigger is not used by this definition.", nameof(trigger));
        }

        if (!_pending.Contains(trigger))
        {
            _pending.Add(trigger);
        }
    }

    /// <summary>Discards pending trigger inputs without changing state or playback.</summary>
    public void ClearTriggers()
    {
        EnsureIdle();
        _pending.Clear();
    }

    /// <summary>Reads the clock once, evaluates playback and selects at most one transition, appending consumer results.</summary>
    /// <param name="context">The context.</param>
    /// <param name="output">The output.</param>
    /// <param name="events">The events.</param>
    /// <param name="transitions">The transitions.</param>
    /// <returns>The operation result.</returns>
    public bool Update(TContext context, AnimationOutput output, ICollection<AnimationStateEvent<TState>> events, ICollection<AnimationStateTransition<TState>> transitions)
    {
        EnsureIdle();
        ValidateContext(context);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(transitions);
        if (Status == AnimationStateMachineStatus.Stopped)
        {
            return false;
        }

        _operating = true;
        try
        {
            TimePoint now = CaptureTime();
            Entry previous = _current!;
            _scratch.Clear();
            if (Status == AnimationStateMachineStatus.Paused)
            {
                _markers.Clear();
                previous.Playback!.Update(_scratch, _markers);
                _scratch.CopyTo(output);
                return false;
            }

            _markers.Clear();
            TimePoint eventBase = previous.LastEvaluationClock;
            previous.Playback!.Update(_scratch, _markers);
            previous.LastEvaluationClock = now;
            var info = new AnimationStateInfo(previous.Playback.Position, previous.Binding.Timeline.Duration, previous.Playback.State == AnimationPlaybackState.Completed);
            _inputs.Clear();
            _inputs.AddRange(_pending);
            if (_definition.AutomaticTrigger is { } automatic && !_inputs.Contains(automatic))
            {
                _inputs.Add(automatic);
            }

            _selected = null;
            bool changed = _instance!.FireAny(_inputs, new AnimationStateContext<TContext>(context, info));
            _pending.Clear();
            if (changed)
            {
                previous.Playback.Stop();
                _current = _controlEntries[_selected!.To];
                Play(_current);
                _scratch.Clear();
                _incomingMarkers.Clear();
                _current.Playback!.Update(_scratch, _incomingMarkers);
                _current.LastEvaluationClock = now;
            }

            _scratch.CopyTo(output);
            foreach (AnimationEventOccurrence marker in _markers)
            {
                AnimationEventOccurrence corrected = marker with
                {
                    UpdateOffset = (eventBase - _lastEventClock) + marker.UpdateOffset,
                };
                events.Add(new AnimationStateEvent<TState>(previous.Binding.Id, corrected, eventBase + marker.UpdateOffset));
            }

            if (changed)
            {
                transitions.Add(new AnimationStateTransition<TState>(previous.Binding.Id, _current!.Binding.Id, now));
            }

            _lastEventClock = now;
            return changed;
        }
        finally
        {
            _operating = false;
        }
    }

    private static IMonotonicClock ValidateClock(IMonotonicClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return clock;
    }

    private static AnimationStateMachineDefinition<TState, TContext> Compile(ComposeAnimation.Definitions.StateMachine<TState, TContext> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Compile();
    }

    private static AnimationStateMachineDefinition<TState, TContext> Compile(AnimationStateMachineBuilder<TState, TContext> builder, TState initialState)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Compile(initialState);
    }

    private static void ValidateContext(TContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }
    }

    private Entry ActiveEntry()
    {
        if (Status == AnimationStateMachineStatus.Stopped)
        {
            throw new InvalidOperationException("The animation machine is stopped.");
        }

        return _instance is null ? _current! : _controlEntries[_instance.CurrentState];
    }

    private TimePoint CaptureTime()
    {
        TimePoint now = _clock.Now;
        if (_observed && now < _lastObserved)
        {
            throw new InvalidOperationException("A monotonic clock moved backwards.");
        }

        _observed = true;
        _lastObserved = now;
        _captured.Now = now;
        return now;
    }

    private void Play(Entry entry)
    {
        entry.Playback ??= new AnimationPlayback(_captured, entry.Binding.Timeline, entry.Binding.Wrap);
        entry.Playback.Stop();
        entry.Playback.Speed = _speed;
        entry.Playback.Play();
        entry.LastEvaluationClock = _captured.Now;
    }

    private void OnTransitioned(Transition<AnimationStateContext<TContext>, StateMachineTrigger> transition) => _selected = transition;

    private void Reset()
    {
        if (_instance is not null)
        {
            _instance.Transitioned -= OnTransitioned;
        }

        foreach (Entry entry in _entries.Values)
        {
            entry.Playback?.Stop();
        }

        _instance = null;
        _current = null;
        _selected = null;
        _pending.Clear();
        _inputs.Clear();
        _markers.Clear();
        _incomingMarkers.Clear();
        _scratch.Clear();
        Status = AnimationStateMachineStatus.Stopped;
    }

    private void EnsureIdle()
    {
        if (_operating)
        {
            throw new InvalidOperationException("The animation machine cannot be operated recursively.");
        }
    }

    private sealed class CapturedClock : IMonotonicClock
    {
        /// <summary>Gets or sets the now.</summary>
        public TimePoint Now { get; internal set; }
    }

    private sealed class Entry(AnimationStateBinding<TState, TContext> binding)
    {
        internal AnimationStateBinding<TState, TContext> Binding { get; } = binding;

        internal AnimationPlayback? Playback { get; set; }

        internal TimePoint LastEvaluationClock { get; set; }
    }
}
