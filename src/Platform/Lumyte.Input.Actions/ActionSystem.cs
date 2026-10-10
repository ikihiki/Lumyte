using System.Collections.Immutable;
using System.Numerics;

namespace Lumyte.Input.Actions;

/// <summary>Maps ordered input records to context-scoped actions and recognized operations.</summary>
public sealed class ActionSystem
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<(InputDeviceId Device, InputControl Control), Vector2> _controls = new();
    private readonly HashSet<(string Context, InputDeviceId Device, InputControl Control)> _suppressed = new();
    private readonly Dictionary<string, long> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Context, string Action), ActionState> _states = new();
    private readonly Dictionary<(string Context, string Action), ImmutableArray<InputDeviceId>> _contributors = new();
    private readonly Dictionary<(string Binding, InputDeviceId Device), bool> _bindingDown = new();
    private readonly Dictionary<string, RecognitionProgress> _progress = new(StringComparer.Ordinal);
    private readonly List<ActionEvent> _events = new();
    private readonly List<RecognizedAction> _recognized = new();
    private readonly Queue<Action> _pending = new();
    private readonly HashSet<(InputDeviceId Device, InputControl Control)> _captureHeld = new();
    private readonly Dictionary<(string Context, string Action), IActionValueProcessor[]> _processors = new();
    private readonly Dictionary<string, IActionRecognizer[]> _recognizers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _recognizerEventStarts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<InputDeviceId>> _recognizerDevices = new(StringComparer.Ordinal);
    private HashSet<InputDeviceId>? _devices;
    private ActionProfile _profile;
    private RebindSession? _rebind;
    private InputDeviceId? _rebindDevice;
    private ulong _sequence;
    private long _activation;
    private TimeSpan _now;
    private bool _advancing;

    /// <summary>Initializes a new instance of the <see cref = "ActionSystem"/> class.</summary>
    /// <param name = "profile">The profile value.</param>
    /// <param name = "bufferOptions">The bufferOptions value.</param>
    public ActionSystem(ActionProfile profile, InputBufferOptions? bufferOptions = null)
    {
        profile.Validate();
        _profile = profile;
        Buffer = new ActionInputBuffer(bufferOptions ?? new InputBufferOptions(TimeSpan.FromMilliseconds(200), 32));
    }

    /// <summary>Occurs when an action state changes.</summary>
    public event Action<ActionEvent>? Changed;

    /// <summary>Occurs when an operation is recognized.</summary>
    public event Action<RecognizedAction>? Recognized;

    /// <summary>Gets buffer.</summary>
    public ActionInputBuffer Buffer { get; private set; }

    /// <summary>Queues a fresh operation buffer for the next advance.</summary>
    /// <param name = "options">The options value.</param>
    public void SetBufferOptions(InputBufferOptions options)
    {
        Check();
        var buffer = new ActionInputBuffer(options);
        _pending.Enqueue(() => Buffer = buffer);
    }

    /// <summary>Queues an independent correction pipeline for a context-scoped action.</summary>
    /// <param name="contextId">The context identifier.</param>
    /// <param name = "actionId">The action identifier.</param>
    /// <param name = "processors">The ordered pipeline.</param>
    public void SetValueProcessors(string contextId, string actionId, IEnumerable<IActionValueProcessor> processors)
    {
        Check();
        if (!_profile.Actions.Any(action => action.Id == actionId))
        {
            throw new ArgumentException("Unknown action.", nameof(actionId));
        }

        IActionValueProcessor[] prepared = processors.ToArray();
        _pending.Enqueue(() =>
        {
            CancelAll(_now);
            _processors[(contextId, actionId)] = prepared;
            SuppressHeld();
        });
    }

    /// <summary>Queues operation recognizers for one context.</summary>
    /// <param name = "contextId">The context identifier.</param>
    /// <param name = "recognizers">The recognizers.</param>
    public void SetRecognizers(string contextId, IEnumerable<IActionRecognizer> recognizers)
    {
        Check();
        if (!_profile.Contexts.Any(context => context.Id == contextId))
        {
            throw new ArgumentException("Unknown context.", nameof(contextId));
        }

        IActionRecognizer[] prepared = recognizers.ToArray();
        _pending.Enqueue(() =>
        {
            CancelAll(_now);
            _recognizers[contextId] = prepared;
        });
    }

    /// <summary>Returns the immutable current profile.</summary>
    /// <returns>The result of the operation.</returns>
    public ActionProfile ExportProfile()
    {
        Check(true);
        return _profile;
    }

    /// <summary>Queues a validated profile for the next advance.</summary>
    /// <param name = "profile">The profile value.</param>
    public void ApplyProfile(ActionProfile profile)
    {
        Check();
        profile.Validate();
        _pending.Enqueue(() =>
        {
            CancelAll(_now);
            _profile = profile;
            if (_rebind is not null)
            {
                _rebind.IsComplete = true;
            }

            foreach (string id in _active.Keys.Where(id => !profile.Contexts.Any(context => context.Id == id)).ToArray())
            {
                _active.Remove(id);
            }

            SuppressHeld();
        });
    }

    /// <summary>Queues the device selection for this player.</summary>
    /// <param name = "devices">The devices value.</param>
    public void SetDevices(IEnumerable<InputDeviceId> devices)
    {
        Check();
        var selected = devices.ToHashSet();
        _pending.Enqueue(() =>
        {
            CancelAll(_now);
            _devices = selected;
            SuppressHeld();
        });
    }

    /// <summary>Queues context activation at the next advance.</summary>
    /// <param name = "contextId">The contextId value.</param>
    public void ActivateContext(string contextId)
    {
        Check();
        if (!_profile.Contexts.Any(context => context.Id == contextId))
        {
            throw new ArgumentException("Unknown context.", nameof(contextId));
        }

        _pending.Enqueue(() =>
        {
            CancelContext(contextId, _now);
            _active[contextId] = ++_activation;
            SuppressHeld(contextId);
        });
    }

    /// <summary>Queues context deactivation and cancellation at the next advance.</summary>
    /// <param name = "contextId">The contextId value.</param>
    public void DeactivateContext(string contextId)
    {
        Check();
        _pending.Enqueue(() =>
        {
            CancelContext(contextId, _now);
            _active.Remove(contextId);
        });
    }

    /// <summary>Returns the strongest currently mapped state for an action.</summary>
    /// <param name = "actionId">The actionId value.</param>
    /// <returns>The result of the operation.</returns>
    public ActionState GetState(string actionId)
    {
        Check(true);
        ActionDefinition definition = _profile.Actions.First(action => action.Id == actionId);
        return _states.Where(pair => pair.Key.Action == actionId).Select(pair => pair.Value).OrderByDescending(state => state.Value.LengthSquared()).FirstOrDefault() ?? new ActionState(definition.Kind, Vector2.Zero);
    }

    /// <summary>Begins capturing an eligible control for a binding.</summary>
    /// <param name = "bindingId">The bindingId value.</param>
    /// <param name = "options">The options value.</param>
    /// <returns>The result of the operation.</returns>
    public RebindSession BeginRebind(string bindingId, RebindOptions options)
    {
        Check();
        if (!_profile.Bindings.Any(binding => binding.Id == bindingId))
        {
            throw new ArgumentException("Unknown binding.", nameof(bindingId));
        }

        if (options.Timeout <= TimeSpan.Zero || !float.IsFinite(options.AxisThreshold) || options.AxisThreshold <= 0 || options.AxisThreshold > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (_rebind is { IsComplete: false })
        {
            throw new InvalidOperationException("A capture is already active.");
        }

        _captureHeld.Clear();
        _captureHeld.UnionWith(_controls.Where(pair => pair.Value != Vector2.Zero).Select(pair => pair.Key));
        _rebind = new RebindSession(this, bindingId, options, _now);
        _rebindDevice = null;
        _pending.Enqueue(() =>
        {
            CancelAll(_now);
            SuppressHeld();
        });
        return _rebind;
    }

    /// <summary>Consumes new ordered records and advances operation timers.</summary>
    /// <param name = "records">The records value.</param>
    /// <param name = "now">The now value.</param>
    public void Advance(ReadOnlyMemory<InputRecord> records, TimeSpan now)
    {
        Check();
        if (now < _now)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        ulong sequence = _sequence;
        TimeSpan time = _now;
        foreach (InputRecord record in records.Span)
        {
            if (record.Sequence <= sequence || record.RecordedAt < time || record.RecordedAt > now)
            {
                throw new ArgumentException("Records must be new, ordered and no later than now.", nameof(records));
            }

            sequence = record.Sequence;
            time = record.RecordedAt;
        }

        _events.Clear();
        _recognizerEventStarts.Clear();
        _recognized.Clear();
        _advancing = true;
        try
        {
            while (_pending.TryDequeue(out Action? change))
            {
                change();
            }

            foreach (InputRecord record in records.Span)
            {
                Timers(record.RecordedAt);
                _sequence = record.Sequence;
                if (record.Data is FocusData { IsFocused: false } or DeviceDisconnectedData)
                {
                    ResetCore(record.DeviceId, record.RecordedAt);
                    continue;
                }

                if (!InputControl.TryRead(record.Data, out InputControl control, out Vector2 value))
                {
                    continue;
                }

                (InputDeviceId Device, InputControl Control) key = (record.DeviceId, control);
                if (record.Data is KeyData { IsRepeat: true } && _controls.GetValueOrDefault(key) == Vector2.Zero)
                {
                    continue;
                }

                _controls[key] = value;
                bool blocked = _captureHeld.Contains(key);
                if (value == Vector2.Zero)
                {
                    _suppressed.RemoveWhere(item => item.Device == key.Device && item.Control == key.Control);
                    _captureHeld.Remove(key);
                }

                if (_devices is not null && !_devices.Contains(record.DeviceId))
                {
                    continue;
                }

                Capture(record, control, value, blocked);
                Recompute(record.RecordedAt);
                _now = record.RecordedAt;
            }

            Timers(now);
            Recompute(now);
            foreach (KeyValuePair<string, IActionRecognizer[]> item in _recognizers.Where(item => _active.ContainsKey(item.Key)))
            {
                ActionEvent[] scoped = _events.Skip(_recognizerEventStarts.GetValueOrDefault(item.Key)).Where(action => action.ContextId == item.Key).ToArray();
                if (!_recognizerDevices.TryGetValue(item.Key, out HashSet<InputDeviceId>? devices))
                {
                    devices = new HashSet<InputDeviceId>();
                    _recognizerDevices.Add(item.Key, devices);
                }

                devices.UnionWith(scoped.SelectMany(action => action.Devices));
                foreach (IActionRecognizer recognizer in item.Value)
                {
                    foreach (RecognizedAction recognition in recognizer.Advance(scoped, now))
                    {
                        if (recognition.ContextId != item.Key || recognition.At > now)
                        {
                            throw new InvalidOperationException("Recognizer returned an invalid context or time.");
                        }

                        _recognized.Add(recognition);
                    }
                }
            }

            _now = now;
            foreach (RecognizedAction recognition in _recognized.OrderBy(action => action.At))
            {
                Buffer.Add(recognition, now);
            }

            Buffer.Prune(now);
            Notify();
        }
        finally
        {
            _advancing = false;
        }
    }

    /// <summary>Discards transient recognition or correction state.</summary>
    /// <param name = "device">The device value.</param>
    /// <param name = "now">The now value.</param>
    public void Reset(InputDeviceId device, TimeSpan now)
    {
        Check();
        if (now < _now)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        _pending.Enqueue(() => ResetCore(device, _now));
    }

    internal void CancelRebind(RebindSession session)
    {
        Check();
        if (!ReferenceEquals(session, _rebind))
        {
            throw new InvalidOperationException("The session is no longer current.");
        }

        session.IsComplete = true;
        _pending.Enqueue(() => SuppressHeld());
    }

    internal ActionProfile PrepareRebind(RebindSession session, RebindConflictPolicy policy)
    {
        Check();
        if (!ReferenceEquals(session, _rebind) || session.IsComplete || session.Candidate is not InputControl candidate)
        {
            throw new InvalidOperationException("No captured candidate.");
        }

        if (policy == RebindConflictPolicy.Reject && !session.Conflicts.IsEmpty)
        {
            throw new InvalidOperationException("The candidate conflicts with existing bindings.");
        }

        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        var bindings = _profile.Bindings.Where(binding => policy != RebindConflictPolicy.ReplaceConflicts || !session.Conflicts.Contains(binding.Id)).Select(binding => binding.Id == session.BindingId ? binding with { Control = candidate } : binding).ToImmutableArray();
        return _profile with
        {
            Bindings = bindings,
        };
    }

    private static void Dispatch<T>(Action<T>? handlers, T value, List<Exception> errors)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Action<T> handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                handler(value);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }
    }

    private void Capture(InputRecord record, InputControl control, Vector2 value, bool blocked)
    {
        if (_rebind is not { IsComplete: false } session || session.Candidate is not null)
        {
            return;
        }

        if (record.Data is KeyData { IsDown: true } key && key.Key == session.Options.CancelKey)
        {
            session.IsComplete = true;
            return;
        }

        if (blocked || record.Data is KeyData { IsRepeat: true } || value.Length() < session.Options.AxisThreshold)
        {
            return;
        }

        session.Candidate = control;
        _rebindDevice = record.DeviceId;
        ActionBinding target = _profile.Bindings.First(binding => binding.Id == session.BindingId);
        session.Conflicts = _profile.Bindings.Where(binding => binding.Id != target.Id && binding.ContextId == target.ContextId && binding.Control == control).Select(binding => binding.Id).ToImmutableArray();
    }

    private void Recompute(TimeSpan now)
    {
        var claimed = new HashSet<InputControl>();
        foreach (InputContext context in _profile.Contexts.Where(context => _active.ContainsKey(context.Id)).OrderByDescending(context => context.Priority).ThenByDescending(context => _active[context.Id]))
        {
            foreach (ActionDefinition definition in _profile.Actions)
            {
                (string, string) stateKey = (context.Id, definition.Id);
                ActionState previous = _states.GetValueOrDefault(stateKey) ?? new ActionState(definition.Kind, Vector2.Zero);
                Vector2 mapped = Vector2.Zero;
                var contributors = new HashSet<InputDeviceId>();
                foreach (ActionBinding binding in _profile.Bindings.Where(binding => binding.ContextId == context.Id && binding.ActionId == definition.Id).OrderBy(binding => binding.Id, StringComparer.Ordinal))
                {
                    if (claimed.Contains(binding.Control) || _rebind is { IsComplete: false })
                    {
                        continue;
                    }

                    foreach (KeyValuePair<(InputDeviceId Device, InputControl Control), Vector2> pair in _controls)
                    {
                        if (pair.Key.Control != binding.Control || (_devices is not null && !_devices.Contains(pair.Key.Device)) || _suppressed.Contains((context.Id, pair.Key.Device, pair.Key.Control)))
                        {
                            continue;
                        }

                        Vector2 candidate = pair.Value;
                        if (definition.Kind == ActionValueKind.Button)
                        {
                            float magnitude = candidate.Length();
                            bool down = _bindingDown.GetValueOrDefault((binding.Id, pair.Key.Device)) ? magnitude > binding.ReleaseThreshold : magnitude >= binding.PressThreshold;
                            _bindingDown[(binding.Id, pair.Key.Device)] = down;
                            candidate = down ? Vector2.UnitX : Vector2.Zero;
                        }
                        else if (binding.Control.Kind is InputControlKind.ControllerStick)
                        {
                            candidate *= binding.Scale;
                        }
                        else
                        {
                            candidate = binding.Scale * candidate.X;
                        }

                        if (definition.Kind == ActionValueKind.Axis1D)
                        {
                            candidate.Y = 0;
                        }

                        if (definition.Kind == ActionValueKind.Button && candidate != Vector2.Zero)
                        {
                            contributors.Add(pair.Key.Device);
                        }

                        if (candidate.LengthSquared() > mapped.LengthSquared())
                        {
                            mapped = candidate;
                            if (definition.Kind != ActionValueKind.Button)
                            {
                                contributors.Clear();
                                contributors.Add(pair.Key.Device);
                            }
                        }
                    }
                }

                mapped *= definition.Sensitivity;
                if (definition.Normalize && mapped.LengthSquared() > 1)
                {
                    mapped = Vector2.Normalize(mapped);
                }

                mapped = Vector2.Clamp(mapped, -Vector2.One, Vector2.One);
                if (definition.Kind != ActionValueKind.Button && definition.SmoothingSeconds > 0 && mapped != Vector2.Zero)
                {
                    float amount = 1 - MathF.Exp(-(float)(now - _now).TotalSeconds / definition.SmoothingSeconds);
                    mapped = Vector2.Lerp(previous.Value, mapped, amount);
                }

                if (_processors.TryGetValue((context.Id, definition.Id), out IActionValueProcessor[]? processors))
                {
                    var processed = new ActionState(definition.Kind, mapped);
                    foreach (IActionValueProcessor processor in processors)
                    {
                        processed = processor.Process(processed, now);
                    }

                    if (processed.Kind != definition.Kind || !float.IsFinite(processed.Value.X) || !float.IsFinite(processed.Value.Y))
                    {
                        throw new InvalidOperationException("Processor returned an invalid state.");
                    }

                    mapped = Vector2.Clamp(processed.Value, -Vector2.One, Vector2.One);
                }

                mapped = definition.Kind switch
                {
                    ActionValueKind.Button => new Vector2(mapped.X >= 0.5f ? 1 : 0, 0),
                    ActionValueKind.Axis1D => new Vector2(mapped.X, 0),
                    _ => mapped,
                };
                _states[stateKey] = new ActionState(definition.Kind, mapped);
                var devices = contributors.OrderBy(id => id.Value).ToImmutableArray();
                if (mapped != previous.Value)
                {
                    ActionPhase phase = mapped == Vector2.Zero ? ActionPhase.Canceled : previous.Value == Vector2.Zero ? ActionPhase.Started : ActionPhase.Performed;
                    if (mapped == Vector2.Zero)
                    {
                        devices = _contributors.GetValueOrDefault(stateKey, []);
                    }

                    Emit(new ActionEvent(definition.Id, phase, mapped, now, devices, context.Id));
                }

                _contributors[stateKey] = devices;
            }

            if (context.Exclusive)
            {
                foreach (ActionBinding binding in _profile.Bindings.Where(binding => binding.ContextId == context.Id))
                {
                    claimed.Add(binding.Control);
                }
            }
        }
    }

    private void Emit(ActionEvent action)
    {
        _events.Add(action);
        foreach (RecognitionDefinition definition in _profile.Recognitions.Where(definition => definition.ContextId == action.ContextId && definition.Actions.Contains(action.ActionId)))
        {
            if (!_progress.TryGetValue(definition.Id, out RecognitionProgress? progress))
            {
                progress = new RecognitionProgress();
                _progress.Add(definition.Id, progress);
            }

            if (action.Phase == ActionPhase.Canceled)
            {
                progress.Held.Remove(action.ActionId);
                continue;
            }

            if (action.Phase != ActionPhase.Started)
            {
                continue;
            }

            progress.Held[action.ActionId] = action;
            if (action.At - progress.Start > definition.Window)
            {
                progress.Count = 0;
            }

            if (definition.Kind == RecognitionKind.Sequence)
            {
                if (definition.Actions[progress.Count] != action.ActionId)
                {
                    progress.Count = 0;
                }

                if (definition.Actions[progress.Count] != action.ActionId)
                {
                    continue;
                }
            }

            if (progress.Count == 0)
            {
                progress.Start = action.At;
                progress.Devices.Clear();
            }

            progress.Devices.UnionWith(action.Devices);
            progress.Count++;
            bool complete = definition.Kind switch
            {
                RecognitionKind.Press => true,
                RecognitionKind.MultiTap => progress.Count >= definition.TapCount,
                RecognitionKind.Sequence => progress.Count >= definition.Actions.Length,
                RecognitionKind.Chord => definition.Actions.All(progress.Held.ContainsKey) && progress.Held.Values.Max(item => item.At) - progress.Held.Values.Min(item => item.At) <= definition.Window,
                _ => false,
            };
            if (complete)
            {
                Recognize(definition, action.At, action.Value, (definition.Kind == RecognitionKind.Chord ? progress.Held.Values.SelectMany(item => item.Devices).Distinct() : progress.Devices).OrderBy(device => device.Value).ToImmutableArray());
                progress.Count = 0;
            }
        }
    }

    private void Timers(TimeSpan now)
    {
        if (_rebind is { IsComplete: false, Candidate: null } session && now - session.Started >= session.Options.Timeout)
        {
            session.IsComplete = true;
        }

        foreach (RecognitionDefinition definition in _profile.Recognitions.Where(definition => definition.Kind == RecognitionKind.Hold))
        {
            if (!_progress.TryGetValue(definition.Id, out RecognitionProgress? progress))
            {
                continue;
            }

            foreach (string actionId in definition.Actions)
            {
                if (progress.Held.TryGetValue(actionId, out ActionEvent? action) && now - action.At >= definition.Window)
                {
                    Recognize(definition, action.At + definition.Window, action.Value, action.Devices);
                    progress.Held.Remove(actionId);
                }
            }
        }
    }

    private void Recognize(RecognitionDefinition definition, TimeSpan at, Vector2 value, ImmutableArray<InputDeviceId> devices)
    {
        var action = new RecognizedAction(definition.Id, definition.ContextId, at, value, devices);
        _recognized.Add(action);
    }

    private void ResetCore(InputDeviceId device, TimeSpan now)
    {
        foreach ((InputDeviceId Device, InputControl Control) key in _controls.Keys.Where(key => key.Device == device).ToArray())
        {
            _controls.Remove(key);
        }

        _suppressed.RemoveWhere(item => item.Device == device);
        (string Context, string Action)[] affectedStates = _states.Keys.Where(key => _contributors.GetValueOrDefault(key, []).Contains(device)).ToArray();
        var affectedContexts = affectedStates.Select(key => key.Context).ToHashSet(StringComparer.Ordinal);
        foreach (string contextId in _recognizers.Keys)
        {
            if ((_recognizerDevices.TryGetValue(contextId, out HashSet<InputDeviceId>? devices) && devices.Contains(device)) || _events.Skip(_recognizerEventStarts.GetValueOrDefault(contextId)).Any(action => action.ContextId == contextId && action.Devices.Contains(device)))
            {
                affectedContexts.Add(contextId);
            }
        }

        foreach ((string Context, string Action) key in affectedStates)
        {
            SuppressHeld(key.Context, _profile.Bindings.Where(binding => binding.ContextId == key.Context && binding.ActionId == key.Action).Select(binding => binding.Control).ToHashSet());
            CancelState(key, now);
            if (_processors.TryGetValue(key, out IActionValueProcessor[]? processors))
            {
                foreach (IActionValueProcessor processor in processors)
                {
                    processor.Reset();
                }
            }
        }

        foreach (string contextId in affectedContexts)
        {
            if (_recognizers.TryGetValue(contextId, out IActionRecognizer[]? recognizers))
            {
                foreach (IActionRecognizer recognizer in recognizers)
                {
                    recognizer.Reset();
                }
            }

            _recognizerEventStarts[contextId] = _events.Count;
            _recognizerDevices.Remove(contextId);
        }

        foreach (string id in _progress.Where(item => item.Value.Devices.Contains(device) || item.Value.Held.Values.Any(action => action.Devices.Contains(device))).Select(item => item.Key).ToArray())
        {
            _progress.Remove(id);
        }

        foreach ((string Binding, InputDeviceId Device) key in _bindingDown.Keys.Where(key => key.Device == device).ToArray())
        {
            _bindingDown.Remove(key);
        }

        _captureHeld.RemoveWhere(item => item.Device == device);
        if (_rebind is not null && _rebindDevice == device)
        {
            _rebind.IsComplete = true;
        }

        Buffer.ClearDevice(device);
        _recognized.RemoveAll(action => action.Devices.Contains(device));
    }

    private void CancelContext(string contextId, TimeSpan now)
    {
        foreach ((string Context, string Action) key in _states.Keys.Where(key => key.Context == contextId).ToArray())
        {
            CancelState(key, now);
        }

        foreach (RecognitionDefinition definition in _profile.Recognitions.Where(definition => definition.ContextId == contextId))
        {
            _progress.Remove(definition.Id);
        }

        foreach (KeyValuePair<(string Context, string Action), IActionValueProcessor[]> item in _processors.Where(item => item.Key.Context == contextId))
        {
            foreach (IActionValueProcessor processor in item.Value)
            {
                processor.Reset();
            }
        }

        if (_recognizers.TryGetValue(contextId, out IActionRecognizer[]? recognizers))
        {
            foreach (IActionRecognizer recognizer in recognizers)
            {
                recognizer.Reset();
            }
        }

        _recognizerEventStarts[contextId] = _events.Count;
        _recognizerDevices.Remove(contextId);
        _recognized.RemoveAll(action => action.ContextId == contextId);
        _suppressed.RemoveWhere(item => item.Context == contextId);
        Buffer.ClearContext(contextId);
    }

    private void CancelAll(TimeSpan now)
    {
        foreach ((string Context, string Action) key in _states.Keys.ToArray())
        {
            CancelState(key, now);
        }

        foreach (IActionValueProcessor processor in _processors.Values.SelectMany(value => value))
        {
            processor.Reset();
        }

        foreach (IActionRecognizer recognizer in _recognizers.Values.SelectMany(value => value))
        {
            recognizer.Reset();
        }

        foreach (string id in _recognizers.Keys)
        {
            _recognizerEventStarts[id] = _events.Count;
        }

        _progress.Clear();
        _recognizerDevices.Clear();
        _bindingDown.Clear();
        Buffer.Clear();
        _recognized.Clear();
    }

    private void CancelState((string Context, string Action) key, TimeSpan now)
    {
        ActionState state = _states[key];
        if (state.Value != Vector2.Zero)
        {
            _events.Add(new ActionEvent(key.Action, ActionPhase.Canceled, Vector2.Zero, now, _contributors.GetValueOrDefault(key, []), key.Context));
        }

        _states.Remove(key);
        _contributors.Remove(key);
    }

    private void SuppressHeld(string? contextId = null, IReadOnlySet<InputControl>? controls = null)
    {
        foreach (KeyValuePair<(InputDeviceId Device, InputControl Control), Vector2> item in _controls.Where(pair => pair.Value != Vector2.Zero && (controls is null || controls.Contains(pair.Key.Control)) && pair.Key.Control.Kind is InputControlKind.Key or InputControlKind.MouseButton or InputControlKind.ControllerButton))
        {
            foreach (string id in _active.Keys.Where(id => contextId is null || id == contextId))
            {
                _suppressed.Add((id, item.Key.Device, item.Key.Control));
            }
        }
    }

    private void Notify()
    {
        var errors = new List<Exception>();
        foreach (ActionEvent action in _events)
        {
            Dispatch(Changed, action, errors);
        }

        foreach (RecognizedAction action in _recognized)
        {
            Dispatch(Recognized, action, errors);
        }

        if (errors.Count > 0)
        {
            throw new AggregateException(errors);
        }
    }

    private void Check(bool allowDuringAdvance = false)
    {
        if (Environment.CurrentManagedThreadId != _thread || (_advancing && !allowDuringAdvance))
        {
            throw new InvalidOperationException("Use the owning thread outside notifications.");
        }
    }

    private sealed class RecognitionProgress
    {
        public Dictionary<string, ActionEvent> Held { get; } = new(StringComparer.Ordinal);

        public HashSet<InputDeviceId> Devices { get; } = new();

        public int Count { get; set; }

        public TimeSpan Start { get; set; }
    }
}
