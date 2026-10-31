using Lumyte.Input.Abstractions;
using Lumyte.Input.Internal;

namespace Lumyte.Input;

/// <summary>Implements acknowledged cross-device acquisition and window-scoped state.</summary>
public sealed partial class InputSystem
{
    private readonly HashSet<IInputSource> _stopped = [];
    private readonly Dictionary<Guid, OrderedState> _ordered = [];

    /// <summary>Gets the immutable state of exactly one window scope, or the windowless scope.</summary>
    /// <param name="device">The physical device.</param>
    /// <param name="windowId">The window connection, or null.</param>
    /// <returns>A neutral state for an unobserved scope.</returns>
    public InputDeviceState GetState(InputDeviceId device, InputWindowId? windowId)
    {
        if (windowId?.Value == Guid.Empty)
        {
            throw new ArgumentException("A window ID cannot be empty.", nameof(windowId));
        }

        Entry entry = GetEntry(device);
        if (entry.Owner.Source is not IOrderedInputSource)
        {
            return windowId is null ? GetState(device) : new InputDeviceState(new(entry.Info.Kind) { Connected = entry.State.Connected });
        }

        return new InputDeviceState(entry.Scopes.GetValueOrDefault(windowId?.Value ?? Guid.Empty) ?? new(entry.Info.Kind) { Connected = entry.State.Connected });
    }

    /// <summary>Permanently stops one registered ordered source and records an acquisition gap.</summary>
    /// <param name="source">The borrowed registered source.</param>
    public void StopSource(IOrderedInputSource source)
    {
        CheckMutation();
        ArgumentNullException.ThrowIfNull(source);
        if (!_sources.Contains(source))
        {
            throw new ArgumentException("The source is not registered here.", nameof(source));
        }

        if (!_stopped.Add(source))
        {
            return;
        }

        _busy = true;
        var errors = new List<Exception>();
        var notifications = new List<InputRecord>();
        try
        {
            foreach (Entry entry in _activeEntries.Where(entry => ReferenceEquals(entry.Owner.Source, source)))
            {
                entry.AcquisitionGapAfter = _sequence;
                entry.Removing = true;
                DisconnectOrdered(entry, notifications);
                if (_inspection is not null)
                {
                    Capture(() => _inspection.DeviceUpdated(this, entry.Info.Id, new(entry.State), entry.LatestSequence), errors);
                    Capture(() => _inspection.Unregistered(this, entry.Info.Id), errors);
                }
            }

            Capture(source.AbortEvents, errors);
            Notify(notifications, errors);
            ReleaseRemoved(errors);
        }
        finally
        {
            _busy = false;
        }

        ThrowErrors(errors);
    }

    private static bool IsTerminal(InputData data) => data is KeyData { IsDown: false } or MouseButtonData { IsDown: false } or MouseButtonCanceledData or TouchData { Phase: TouchPhase.Ended or TouchPhase.Canceled } or PenData { Phase: PenPhase.Left or PenPhase.Canceled };

    private static bool SameControl(InputData a, InputData b) => (a, b) switch
    {
        (MouseMoveData, MouseMoveData) or (MouseWheelData, MouseWheelData) => true,
        (TouchData x, TouchData y) => x.ContactId == y.ContactId && x.Phase == y.Phase,
        (PenData x, PenData y) => x.PointerId == y.PointerId && x.Phase == y.Phase && x.IsInContact == y.IsInContact && x.Buttons == y.Buttons,
        (KeyData x, KeyData y) => x.Key == y.Key,
        (MouseButtonData x, MouseButtonData y) => x.Button == y.Button,
        (ControllerButtonData x, ControllerButtonData y) => x.Button == y.Button,
        (ControllerStickData x, ControllerStickData y) => x.Stick == y.Stick,
        (ControllerTriggerData x, ControllerTriggerData y) => x.Trigger == y.Trigger,
        _ => Equals(a, b),
    };

    private static DeviceState Aggregate(Entry entry, Dictionary<Guid, DeviceState> scopes, List<InputRecord> records)
    {
        var state = new DeviceState(entry.Info.Kind) { Connected = entry.State.Connected };
        foreach (DeviceState scope in scopes.Values)
        {
            state.Focused |= scope.Focused;
            for (int i = 0; i < state.Keys.Length; i++)
            {
                state.Keys[i] |= scope.Keys[i];
            }

            for (int i = 0; i < state.ControllerButtons.Length; i++)
            {
                state.ControllerButtons[i] |= scope.ControllerButtons[i];
            }

            scope.Sticks.CopyTo(state.Sticks, 0);
            scope.Triggers.CopyTo(state.Triggers, 0);
            for (int i = 0; i < state.MouseButtons.Length; i++)
            {
                state.MouseButtons[i] |= scope.MouseButtons[i];
            }

            foreach (KeyValuePair<TouchContactId, TouchData> pair in scope.Touches)
            {
                state.Touches[pair.Key] = pair.Value;
                state.TouchWindows[pair.Key] = scope.TouchWindows.GetValueOrDefault(pair.Key);
            }

            foreach (KeyValuePair<PenPointerId, PenData> pair in scope.Pens)
            {
                state.Pens[pair.Key] = pair.Value;
                state.PenWindows[pair.Key] = scope.PenWindows.GetValueOrDefault(pair.Key);
            }
        }

        state.Position = entry.State.Position;
        state.PositionWindow = entry.State.PositionWindow;
        foreach (InputRecord record in records)
        {
            if (record.DeviceId == entry.Info.Id && record.Data is MouseMoveData move)
            {
                state.Position = move.Position;
                state.PositionWindow = record.WindowId;
            }
        }

        return state;
    }

    private void CollectOrdered(Registry registry, IOrderedInputSource source, List<InputRecord> notifications, List<Exception> errors)
    {
        OrderedState stream = _ordered.GetValueOrDefault(source.StreamId) ?? new();
        _ordered[source.StreamId] = stream;
        foreach (Entry entry in _activeEntries.Where(entry => entry.Owner == registry && !entry.Published))
        {
            Append(entry, new DeviceConnectedData(entry.Info), notifications);
            entry.Published = true;
        }

        bool completed = false;
        try
        {
            while (source.PeekEvents() is InputEventBatch batch)
            {
                if (batch.Id <= stream.Batch || batch.CapturedThrough < stream.CapturedThrough)
                {
                    throw new ArgumentException("Ordered acquisition moved backwards.");
                }

                OrderedState candidate = stream.Clone();
                var states = _activeEntries.Where(entry => entry.Owner == registry).ToDictionary(entry => entry.Info.Id, entry => entry.Scopes.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()));
                var scoped = new List<InputScopedEvent>();
                var suppressed = new HashSet<InputScopedEvent>();
                ulong capture = stream.CapturedThrough;
                foreach (InputSourceEvent input in batch.Events)
                {
                    Entry entry = GetEntry(input.DeviceId);
                    if (entry.Owner != registry || !entry.State.Connected)
                    {
                        throw new ArgumentException("An event references a foreign or disconnected device.");
                    }

                    entry.State.Validate(input.Data);
                    if (input.CaptureSequence is ulong sequence)
                    {
                        if (sequence < capture || sequence > batch.CapturedThrough)
                        {
                            throw new ArgumentException("Acquisition order moved backwards.");
                        }

                        capture = sequence;
                    }

                    InputWindowId? owner;
                    bool suppress;
                    if (input.OriginDeviceId is InputDeviceId originDevice)
                    {
                        Entry origin = GetEntry(originDevice);
                        if (origin.Owner != registry || originDevice == input.DeviceId)
                        {
                            throw new ArgumentException("A derived event requires a different causal device in the same source.");
                        }

                        owner = input.StateWindowId;
                        if (input.CaptureSequence is ulong cause && candidate.Causes.TryGetValue((originDevice, cause), out InputWindowId? projected) && projected != owner)
                        {
                            throw new ArgumentException("A derived event changed its causal processing scope.");
                        }

                        suppress = input.Data is not (FocusData { IsFocused: false } or WindowInputClosedData) && owner is InputWindowId scope && candidate.IsClosed(scope);
                    }
                    else
                    {
                        (owner, suppress) = candidate.Resolve(input);
                    }

                    if (input.CaptureSequence is ulong stamp)
                    {
                        candidate.Causes[(input.DeviceId, stamp)] = owner;
                    }

                    if (!suppress || IsTerminal(input.Data))
                    {
                        var item = new InputScopedEvent(input, owner);
                        scoped.Add(item);
                        if (suppress)
                        {
                            suppressed.Add(item);
                        }
                    }
                }

                TimeSpan now = stream.PendingTime ??= _clock.GetElapsedTime(_started);
                IReadOnlyList<InputScopedEvent> effective = _inspection?.ProcessEvents(this, source.StreamId, scoped.AsReadOnly(), now) ?? scoped;
                int physicalIndex = 0;
                var prepared = new List<InputRecord>();
                ulong next = _sequence;
                for (int i = 0; i < effective.Count; i++)
                {
                    InputScopedEvent value = effective[i];
                    InputScopedEvent? original = physicalIndex < scoped.Count ? scoped[physicalIndex] : null;
                    if (original is not null && value.StateWindowId == original.StateWindowId && value.Input.DeviceId == original.Input.DeviceId && value.Input.OriginDeviceId == original.Input.OriginDeviceId && value.Input.StateWindowId == original.Input.StateWindowId && value.Input.WindowId == original.Input.WindowId && value.Input.CaptureSequence == original.Input.CaptureSequence && SameControl(value.Input.Data, original.Input.Data))
                    {
                        physicalIndex++;
                    }
                    else
                    {
                        original = null;
                        if (value.Input.Data is not (KeyData or MouseButtonData or ControllerButtonData or ControllerStickData or ControllerTriggerData or MouseMoveData or MouseWheelData or TouchData { Phase: TouchPhase.Moved } or PenData { Phase: PenPhase.Moved }) || value.Input.CaptureSequence is not null || value.StateWindowId != value.Input.WindowId || value.StateWindowId is not InputWindowId injected || !states.TryGetValue(value.Input.DeviceId, out Dictionary<Guid, DeviceState>? observed) || !observed.ContainsKey(injected.Value) || candidate.IsClosed(injected))
                        {
                            throw new ArgumentException("Inspection changed acquisition metadata or injected into an unobserved/retired scope.");
                        }
                    }

                    Entry entry = GetEntry(value.Input.DeviceId);
                    Dictionary<Guid, DeviceState> scopes = states[entry.Info.Id];
                    Guid scope = value.StateWindowId?.Value ?? Guid.Empty;
                    if (!scopes.TryGetValue(scope, out DeviceState? state))
                    {
                        state = new(entry.Info.Kind);
                        scopes.Add(scope, state);
                    }

                    InputData data = value.Input.Data;
                    state.Validate(data);
                    var applied = new List<InputData>();
                    if (original is not null && suppressed.Contains(original))
                    {
                        // Keep physical terminals visible to series consumers, without reviving state.
                        applied.Add(data);
                    }
                    else if (data is WindowInputClosedData)
                    {
                        state.Neutralize([]);
                        state.Focused = false;
                        applied.Add(data);
                    }
                    else
                    {
                        bool focused = state.Focused;
                        bool pointer = value.StateWindowId is not null && entry.Info.Kind is InputDeviceKind.Mouse or InputDeviceKind.Touch or InputDeviceKind.Pen;
                        if (pointer && data is not FocusData)
                        {
                            state.Focused = true;
                        }

                        state.Apply(data, applied);
                        if (pointer && data is not FocusData)
                        {
                            state.Focused = focused;
                        }

                        // Scope loss is a cancellation boundary, never a synthetic normal key/button Up.
                        if (data is FocusData { IsFocused: false })
                        {
                            applied.Clear();
                            applied.Add(data);
                        }
                    }

                    if (data is MouseMoveData)
                    {
                        state.PositionWindow = value.Input.WindowId;
                    }

                    if (data is TouchData touch)
                    {
                        if (state.Touches.ContainsKey(touch.ContactId))
                        {
                            state.TouchWindows[touch.ContactId] = value.Input.WindowId;
                        }
                        else
                        {
                            state.TouchWindows.Remove(touch.ContactId);
                        }
                    }

                    if (data is PenData pen)
                    {
                        if (state.Pens.ContainsKey(pen.PointerId))
                        {
                            state.PenWindows[pen.PointerId] = value.Input.WindowId;
                        }
                        else
                        {
                            state.PenWindows.Remove(pen.PointerId);
                        }
                    }

                    foreach (InputData output in applied)
                    {
                        prepared.Add(new(entry.Info.Id, checked(++next), now, output)
                        {
                            WindowId = value.Input.WindowId,
                            StateWindowId = value.StateWindowId,
                            Capture = value.Input.CaptureSequence is ulong cause ? new(source.StreamId, cause) : null,
                        });
                    }
                }

                if (physicalIndex != scoped.Count)
                {
                    throw new ArgumentException("Inspection removed or reordered acquired events.");
                }

                // All allocations and validation precede publication and acknowledgment.
                var aggregates = states.ToDictionary(pair => pair.Key, pair => Aggregate(GetEntry(pair.Key), pair.Value, prepared));
                notifications.EnsureCapacity(notifications.Count + prepared.Count);
                foreach (IGrouping<InputDeviceId, InputRecord> group in prepared.GroupBy(record => record.DeviceId))
                {
                    Entry entry = GetEntry(group.Key);
                    entry.Records.EnsureCapacity(entry.Records.Count + group.Count());
                }

                foreach (KeyValuePair<InputDeviceId, Dictionary<Guid, DeviceState>> pair in states)
                {
                    Entry entry = GetEntry(pair.Key);
                    entry.Scopes = pair.Value;
                    entry.State = aggregates[pair.Key];
                    entry.StateSnapshot = null;
                }

                foreach (InputRecord record in prepared)
                {
                    Entry entry = GetEntry(record.DeviceId);
                    entry.Records.Add(record);
                    entry.RecordSnapshot = null;
                    entry.LatestSequence = record.Sequence;
                    notifications.Add(record);
                }

                candidate.Batch = batch.Id;
                candidate.CapturedThrough = batch.CapturedThrough;
                candidate.PendingTime = null;
                foreach ((InputDeviceId Device, ulong Capture) key in candidate.Causes.Keys.Where(key => key.Capture < batch.CapturedThrough).ToArray())
                {
                    candidate.Causes.Remove(key);
                }

                _sequence = next;
                _ordered[source.StreamId] = stream = candidate;
                source.CommitEvents(batch.Id);
            }

            completed = true;
        }
        finally
        {
            foreach (Entry entry in _activeEntries.Where(entry => entry.Owner == registry))
            {
                if (completed && entry.Removing && entry.State.Connected)
                {
                    DisconnectOrdered(entry, notifications);
                }

                if (_inspection != null)
                {
                    Capture(() => _inspection.DeviceUpdated(this, entry.Info.Id, new(entry.State), entry.LatestSequence), errors);
                    if (entry.Removing)
                    {
                        Capture(() => _inspection.Unregistered(this, entry.Info.Id), errors);
                    }
                }
            }
        }
    }

    private void DisconnectOrdered(Entry entry, List<InputRecord> notifications)
    {
        if (!entry.Published)
        {
            Append(entry, new DeviceConnectedData(entry.Info), notifications);
            entry.Published = true;
        }

        foreach (KeyValuePair<Guid, DeviceState> pair in entry.Scopes)
        {
            pair.Value.Neutralize([]);
            pair.Value.Connected = false;
            pair.Value.Focused = false;
        }

        entry.State.Neutralize([]);
        entry.State.Connected = false;
        entry.State.Focused = false;
        entry.StateSnapshot = null;
        Append(entry, new DeviceDisconnectedData(), notifications);
    }

    private sealed class OrderedState
    {
        internal ulong Batch { get; set; }

        internal ulong CapturedThrough { get; set; }

        internal TimeSpan? PendingTime { get; set; }

        internal Dictionary<(InputDeviceId Device, ulong Capture), InputWindowId?> Causes { get; private set; } = [];

        private InputSeriesOwners Owners { get; set; } = new();

        internal OrderedState Clone() => new() { Batch = Batch, CapturedThrough = CapturedThrough, Owners = Owners.Clone(), Causes = new(Causes) };

        internal bool IsClosed(InputWindowId window) => Owners.IsClosed(window);

        internal (InputWindowId? Owner, bool Suppress) Resolve(InputSourceEvent input) => Owners.Resolve(input);
    }
}
