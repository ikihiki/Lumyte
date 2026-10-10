using System.Collections.Immutable;
using System.Numerics;

namespace Lumyte.Input.Actions;

/// <summary>Maps ordered input records to context-scoped actions and recognized operations.</summary>
public sealed class ActionSystem
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<(InputDeviceId Device, InputControl Control), Vector2> _controls = new();
    private readonly Dictionary<InputControl, List<InputDeviceId>> _devicesByControl = new();
    private readonly HashSet<InputControl> _claimedControls = new();
    private readonly HashSet<InputDeviceId> _scratchContributors = new();
    private readonly HashSet<(string Context, InputDeviceId Device, InputControl Control)> _suppressed = new();
    private readonly Dictionary<string, long> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Context, string Action), ActionState> _states = new();
    private readonly Dictionary<(string Context, string Action), ImmutableArray<InputDeviceId>> _contributors = new();
    private readonly Dictionary<(string Context, string Binding, InputDeviceId Device), bool> _bindingDown = new();
    private readonly Dictionary<(string Context, string Recognition), RecognitionProgress> _progress = new();
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
    private ProfileIndex _profileIndex;
    private InputContext[] _orderedContexts = [];
    private RebindSession? _rebind;
    private InputDeviceId? _rebindDevice;
    private ulong _sequence;
    private long _activation;
    private TimeSpan _now;
    private bool _advancing;
    private bool _controlsChanged;

    /// <summary>Initializes a new instance of the <see cref = "ActionSystem"/> class.</summary>
    /// <param name = "profile">The profile value.</param>
    /// <param name = "bufferOptions">The bufferOptions value.</param>
    public ActionSystem(ActionProfile profile, InputBufferOptions? bufferOptions = null)
    {
        profile.Validate();
        _profile = profile;
        _profileIndex = new ProfileIndex(profile);
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
        if (!_profileIndex.ActionsByContext.TryGetValue(contextId, out ActionDefinition[]? definitions) || !definitions.Any(action => action.Id == actionId))
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
        var index = new ProfileIndex(profile);
        _pending.Enqueue(() =>
        {
            CancelAll(_now);
            _profile = profile;
            _profileIndex = index;
            if (_rebind is not null)
            {
                _rebind.IsComplete = true;
            }

            foreach (string id in _active.Keys.Where(id => !profile.Contexts.Any(context => context.Id == id)).ToArray())
            {
                _active.Remove(id);
            }

            RefreshContextOrder();
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
            RefreshContextOrder();
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
            RefreshContextOrder();
        });
    }

    /// <summary>Returns the strongest currently mapped state for an action.</summary>
    /// <param name = "actionId">The actionId value.</param>
    /// <returns>The result of the operation.</returns>
    public ActionState GetState(string actionId)
    {
        Check(true);
        if (actionId is null || !_profileIndex.NeutralStates.TryGetValue(actionId, out ActionState? strongest))
        {
            throw new InvalidOperationException("Unknown action.");
        }

        float strength = -1;
        foreach (KeyValuePair<(string Context, string Action), ActionState> pair in _states)
        {
            if (pair.Key.Action == actionId && pair.Value.Value.LengthSquared() > strength)
            {
                strongest = pair.Value;
                strength = pair.Value.Value.LengthSquared();
            }
        }

        return strongest;
    }

    /// <summary>Returns a context-scoped state, including inherited action definitions.</summary>
    /// <param name="contextId">The context identifier.</param>
    /// <param name="actionId">The action identifier.</param>
    /// <returns>The immutable mapped state or its neutral state when inactive.</returns>
    public ActionState GetState(string contextId, string actionId)
    {
        Check(true);
        (string, string) key = (contextId, actionId);
        if (!_profileIndex.NeutralStatesByContext.TryGetValue(key, out ActionState? neutral))
        {
            throw new InvalidOperationException("Unknown context action.");
        }

        return _states.GetValueOrDefault(key) ?? neutral;
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

                if (_controls.TryAdd(key, value))
                {
                    _controlsChanged = true;
                }
                else
                {
                    _controls[key] = value;
                }

                bool blocked = _captureHeld.Contains(key);
                if (value == Vector2.Zero)
                {
                    if (_suppressed.Count > 0)
                    {
                        ReleaseSuppressedControl(key.Device, key.Control);
                    }

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
            foreach (KeyValuePair<string, IActionRecognizer[]> item in _recognizers)
            {
                if (!_active.ContainsKey(item.Key))
                {
                    continue;
                }

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
            if (_recognized.Count > 0)
            {
                foreach (RecognizedAction recognition in _recognized.OrderBy(action => action.At))
                {
                    Buffer.Add(recognition, now);
                }
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

    /// <summary>Activates a composed context using its resolved identifier.</summary>
    /// <param name="context">The composed context.</param>
    public void ActivateContext(Compose.Definitions.Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ActivateContext(context.Identifier);
    }

    /// <summary>Deactivates a composed context.</summary>
    /// <param name="context">The composed context.</param>
    public void DeactivateContext(Compose.Definitions.Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        DeactivateContext(context.Identifier);
    }

    /// <summary>Returns the strongest state for a composed action.</summary>
    /// <param name="action">The action definition.</param>
    /// <returns>The immutable action state.</returns>
    public ActionState GetState(Compose.Definitions.Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return GetState(action.Identifier);
    }

    /// <summary>Returns the state of a composed action in a composed context.</summary>
    /// <param name="context">The context definition.</param>
    /// <param name="action">The action definition.</param>
    /// <returns>The immutable context-scoped state.</returns>
    public ActionState GetState(Compose.Definitions.Context context, Compose.Definitions.Action action)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);
        return GetState(context.Identifier, action.Identifier);
    }

    /// <summary>Begins capturing input for a composed binding.</summary>
    /// <param name="binding">The binding definition.</param>
    /// <param name="options">The capture options.</param>
    /// <returns>The rebind capture session.</returns>
    public RebindSession BeginRebind(Compose.Definitions.Binding binding, RebindOptions options)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return BeginRebind(binding.Identifier, options);
    }

    /// <summary>Sets value processors using composed context and action definitions.</summary>
    /// <param name="context">The context definition.</param>
    /// <param name="action">The action definition.</param>
    /// <param name="processors">The independent context-scoped processors.</param>
    public void SetValueProcessors(Compose.Definitions.Context context, Compose.Definitions.Action action, IEnumerable<IActionValueProcessor> processors)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);
        SetValueProcessors(context.Identifier, action.Identifier, processors);
    }

    /// <summary>Sets recognizers using a composed context.</summary>
    /// <param name="context">The context definition.</param>
    /// <param name="recognizers">The independent context-scoped recognizers.</param>
    public void SetRecognizers(Compose.Definitions.Context context, IEnumerable<IActionRecognizer> recognizers)
    {
        ArgumentNullException.ThrowIfNull(context);
        SetRecognizers(context.Identifier, recognizers);
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

    private static void Dispatch<T>(Action<T>? handlers, T value, ref List<Exception>? errors)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Action<T> handler in Delegate.EnumerateInvocationList(handlers))
        {
            try
            {
                handler(value);
            }
            catch (Exception exception)
            {
                (errors ??= new List<Exception>()).Add(exception);
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
        session.Conflicts = _profileIndex.BindingsByContext.GetValueOrDefault(target.ContextId, []).Where(binding => binding.Id != target.Id && binding.Control == control).Select(binding => binding.Id).ToImmutableArray();
    }

    private void Recompute(TimeSpan now)
    {
        RefreshControlIndex();
        _claimedControls.Clear();
        foreach (InputContext context in _orderedContexts)
        {
            foreach (ActionDefinition definition in _profileIndex.ActionsByContext[context.Id])
            {
                (string, string) stateKey = (context.Id, definition.Id);
                ActionState neutral = _profileIndex.NeutralStates[definition.Id];
                ActionState previous = _states.GetValueOrDefault(stateKey) ?? neutral;
                Vector2 mapped = Vector2.Zero;
                _scratchContributors.Clear();
                foreach (ActionBinding binding in _profileIndex.BindingsByAction.GetValueOrDefault(stateKey, []))
                {
                    if (_claimedControls.Contains(binding.Control) || _rebind is { IsComplete: false } || !_devicesByControl.TryGetValue(binding.Control, out List<InputDeviceId>? devices))
                    {
                        continue;
                    }

                    foreach (InputDeviceId device in devices)
                    {
                        if ((_devices is not null && !_devices.Contains(device)) || _suppressed.Contains((context.Id, device, binding.Control)))
                        {
                            continue;
                        }

                        Vector2 candidate = _controls[(device, binding.Control)];
                        if (definition.Kind == ActionValueKind.Button)
                        {
                            float magnitude = candidate.Length();
                            bool down = _bindingDown.GetValueOrDefault((context.Id, binding.Id, device)) ? magnitude > binding.ReleaseThreshold : magnitude >= binding.PressThreshold;
                            _bindingDown[(context.Id, binding.Id, device)] = down;
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
                            _scratchContributors.Add(device);
                        }

                        if (candidate.LengthSquared() > mapped.LengthSquared())
                        {
                            mapped = candidate;
                            if (definition.Kind != ActionValueKind.Button)
                            {
                                _scratchContributors.Clear();
                                _scratchContributors.Add(device);
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
                    ActionState processed = mapped == previous.Value ? previous : mapped == Vector2.Zero ? neutral : new ActionState(definition.Kind, mapped);
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
                _states[stateKey] = mapped == previous.Value ? previous : mapped == Vector2.Zero ? neutral : new ActionState(definition.Kind, mapped);
                ImmutableArray<InputDeviceId> contributors = SnapshotContributors(_contributors.GetValueOrDefault(stateKey, []));
                if (mapped != previous.Value)
                {
                    ActionPhase phase = mapped == Vector2.Zero ? ActionPhase.Canceled : previous.Value == Vector2.Zero ? ActionPhase.Started : ActionPhase.Performed;
                    if (mapped == Vector2.Zero)
                    {
                        contributors = _contributors.GetValueOrDefault(stateKey, []);
                    }

                    Emit(new ActionEvent(definition.Id, phase, mapped, now, contributors, context.Id));
                }

                _contributors[stateKey] = contributors;
            }

            if (context.Exclusive)
            {
                foreach (ActionBinding binding in _profileIndex.BindingsByContext.GetValueOrDefault(context.Id, []))
                {
                    _claimedControls.Add(binding.Control);
                }
            }
        }
    }

    private void RefreshContextOrder()
        => _orderedContexts = _profile.Contexts.Where(context => _active.ContainsKey(context.Id)).OrderByDescending(context => context.Priority).ThenByDescending(context => _active[context.Id]).ToArray();

    private void RefreshControlIndex()
    {
        if (!_controlsChanged)
        {
            return;
        }

        foreach (List<InputDeviceId> devices in _devicesByControl.Values)
        {
            devices.Clear();
        }

        foreach ((InputDeviceId Device, InputControl Control) key in _controls.Keys)
        {
            if (!_devicesByControl.TryGetValue(key.Control, out List<InputDeviceId>? devices))
            {
                devices = new List<InputDeviceId>();
                _devicesByControl.Add(key.Control, devices);
            }

            devices.Add(key.Device);
        }

        _controlsChanged = false;
    }

    private ImmutableArray<InputDeviceId> SnapshotContributors(ImmutableArray<InputDeviceId> previous)
    {
        if (_scratchContributors.Count == 0)
        {
            return [];
        }

        bool unchanged = _scratchContributors.Count == previous.Length;
        if (unchanged)
        {
            foreach (InputDeviceId device in previous)
            {
                if (!_scratchContributors.Contains(device))
                {
                    unchanged = false;
                    break;
                }
            }
        }

        if (unchanged)
        {
            return previous;
        }

        ImmutableArray<InputDeviceId>.Builder result = ImmutableArray.CreateBuilder<InputDeviceId>(_scratchContributors.Count);
        result.AddRange(_scratchContributors);
        result.Sort(static (left, right) => left.Value.CompareTo(right.Value));
        return result.MoveToImmutable();
    }

    private void ReleaseSuppressedControl(InputDeviceId device, InputControl control)
        => _suppressed.RemoveWhere(item => item.Device == device && item.Control == control);

    private void Emit(ActionEvent action)
    {
        _events.Add(action);
        foreach (RecognitionDefinition definition in _profileIndex.Recognitions)
        {
            if (definition.ContextId != action.ContextId || !definition.Actions.Contains(action.ActionId))
            {
                continue;
            }

            if (!_progress.TryGetValue((definition.ContextId, definition.Id), out RecognitionProgress? progress))
            {
                progress = new RecognitionProgress();
                _progress.Add((definition.ContextId, definition.Id), progress);
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

        foreach (RecognitionDefinition definition in _profileIndex.Recognitions)
        {
            if (definition.Kind != RecognitionKind.Hold || !_progress.TryGetValue((definition.ContextId, definition.Id), out RecognitionProgress? progress))
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
            _controlsChanged = true;
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
            SuppressHeld(key.Context, _profileIndex.BindingsByContext.GetValueOrDefault(key.Context, []).Where(binding => binding.ActionId == key.Action).Select(binding => binding.Control).ToHashSet());
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

        foreach ((string Context, string Recognition) id in _progress.Where(item => item.Value.Devices.Contains(device) || item.Value.Held.Values.Any(action => action.Devices.Contains(device))).Select(item => item.Key).ToArray())
        {
            _progress.Remove(id);
        }

        foreach ((string Context, string Binding, InputDeviceId Device) key in _bindingDown.Keys.Where(key => key.Device == device).ToArray())
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

        foreach (RecognitionDefinition definition in _profileIndex.Recognitions.Where(definition => definition.ContextId == contextId))
        {
            _progress.Remove((definition.ContextId, definition.Id));
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
        List<Exception>? errors = null;
        foreach (ActionEvent action in _events)
        {
            Dispatch(Changed, action, ref errors);
        }

        foreach (RecognizedAction action in _recognized)
        {
            Dispatch(Recognized, action, ref errors);
        }

        if (errors is not null)
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

    private sealed class ProfileIndex
    {
        public ProfileIndex(ActionProfile profile)
        {
            NeutralStates = profile.Actions.Concat(profile.Contexts.SelectMany(context => ActionProfile.LocalActions(context))).GroupBy(action => action.Id).ToDictionary(group => group.Key, group => new ActionState(group.First().Kind, Vector2.Zero), StringComparer.Ordinal);
            var contexts = profile.Contexts.ToDictionary(context => context.Id, StringComparer.Ordinal);
            var recognitions = profile.Recognitions.ToList();
            foreach (InputContext context in profile.Contexts)
            {
                var lineage = new List<InputContext>();
                InputContext current = context;
                while (true)
                {
                    lineage.Add(current);
                    if (current.ParentId is null)
                    {
                        break;
                    }

                    current = contexts[current.ParentId];
                }

                lineage.Reverse();
                var actions = profile.Actions.ToDictionary(action => action.Id, StringComparer.Ordinal);
                var bindings = new Dictionary<string, ActionBinding[]>(StringComparer.Ordinal);
                var inheritedRecognitions = new Dictionary<string, RecognitionDefinition>(StringComparer.Ordinal);
                foreach (InputContext ancestor in lineage)
                {
                    foreach (ActionDefinition action in ActionProfile.LocalActions(ancestor))
                    {
                        actions[action.Id] = action;
                    }

                    foreach (IGrouping<string, ActionBinding> group in profile.Bindings.Where(binding => binding.ContextId == ancestor.Id).GroupBy(binding => binding.ActionId))
                    {
                        bindings[group.Key] = group.Select(binding => binding with { ContextId = context.Id }).ToArray();
                    }

                    foreach (RecognitionDefinition recognition in profile.Recognitions.Where(recognition => recognition.ContextId == ancestor.Id))
                    {
                        inheritedRecognitions[recognition.Id] = recognition with { ContextId = context.Id };
                    }
                }

                ActionsByContext.Add(context.Id, actions.Values.ToArray());
                foreach (ActionDefinition action in actions.Values)
                {
                    NeutralStatesByContext.Add((context.Id, action.Id), NeutralStates[action.Id]);
                }

                ActionBinding[] contextBindings = bindings.Values.SelectMany(group => group).ToArray();
                BindingsByContext.Add(context.Id, contextBindings);
                foreach (IGrouping<string, ActionBinding> group in contextBindings.GroupBy(binding => binding.ActionId))
                {
                    BindingsByAction.Add((context.Id, group.Key), group.OrderBy(binding => binding.Id, StringComparer.Ordinal).ToArray());
                }

                recognitions.AddRange(inheritedRecognitions.Values.Where(recognition => !profile.Recognitions.Any(local => local.ContextId == context.Id && local.Id == recognition.Id)));
            }

            Recognitions = recognitions.ToArray();
        }

        public Dictionary<string, ActionState> NeutralStates { get; }

        public Dictionary<(string Context, string Action), ActionState> NeutralStatesByContext { get; } = new();

        public Dictionary<string, ActionDefinition[]> ActionsByContext { get; } = new(StringComparer.Ordinal);

        public Dictionary<(string Context, string Action), ActionBinding[]> BindingsByAction { get; } = new();

        public Dictionary<string, ActionBinding[]> BindingsByContext { get; } = new(StringComparer.Ordinal);

        public RecognitionDefinition[] Recognitions { get; }
    }

    private sealed class RecognitionProgress
    {
        public Dictionary<string, ActionEvent> Held { get; } = new(StringComparer.Ordinal);

        public HashSet<InputDeviceId> Devices { get; } = new();

        public int Count { get; set; }

        public TimeSpan Start { get; set; }
    }
}
