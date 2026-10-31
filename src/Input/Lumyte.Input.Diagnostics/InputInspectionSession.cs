using System.Buffers;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Lumyte.Input.Abstractions;
using Lumyte.Input.Actions;

namespace Lumyte.Input.Diagnostics;

/// <summary>Collects detached, bounded state from all injected Input and Action systems.</summary>
public sealed partial class InputInspectionSession : IInputInspection, IActionInspection, IInputOverrideService, IInputTapeService, IInputLiveService, IDisposable
{
    private readonly Dictionary<InputSystem, InputView> _inputs = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ActionSystem, ActionView> _actions = new(ReferenceEqualityComparer.Instance);
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private long _revision;
    private bool _disposed;

    /// <summary>Gets the current snapshot revision.</summary>
    public long Revision
    {
        get
        {
            Check();
            return _revision;
        }
    }

    /// <inheritdoc/>
    public void SystemCreated(InputSystem system)
    {
        Check();
        ArgumentNullException.ThrowIfNull(system);
        if (_inputs.Count >= 16 || !_inputs.TryAdd(system, new(Guid.NewGuid()) { System = system }))
        {
            throw new InvalidOperationException("Input inspection system capacity reached or the system is already registered.");
        }

        _revision++;
    }

    /// <inheritdoc/>
    public void SourceInitializing(InputSystem system, IInputSource source)
    {
        InputView view = Input(system);
        ArgumentNullException.ThrowIfNull(source);
        if (!view.Sources.Contains(source.GetType().FullName ?? source.GetType().Name, StringComparer.Ordinal))
        {
            view.Sources.Add(source.GetType().FullName ?? source.GetType().Name);
            _revision++;
        }
    }

    /// <inheritdoc/>
    public void SourceUpdated(InputSystem system, IInputSource source)
    {
        _ = Input(system);
        ArgumentNullException.ThrowIfNull(source);
    }

    /// <inheritdoc/>
    public void SourceShutdown(InputSystem system, IInputSource source)
    {
        _ = Input(system);
        ArgumentNullException.ThrowIfNull(source);
    }

    /// <inheritdoc/>
    public void Registered(InputSystem system, InputDeviceId device, InputDeviceDescriptor descriptor)
    {
        InputView view = Input(system);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (view.Devices.Count >= 256)
        {
            throw new InvalidOperationException("Input inspection device capacity reached.");
        }

        view.Devices.Add(device, new(descriptor));
        RegisterDeviceTargets(system, device, descriptor.Kind);
        _revision++;
    }

    /// <inheritdoc/>
    public void DeviceUpdated(InputSystem system, InputDeviceId device, InputDeviceState state, ulong latestSequence)
    {
        InputView view = Input(system);
        ArgumentNullException.ThrowIfNull(state);
        DeviceView target = view.Devices.TryGetValue(device, out DeviceView? existing) ? existing : throw new InvalidOperationException("Unknown inspected device.");
        target.State = state;
        target.LatestSequence = latestSequence;
        _revision++;
    }

    /// <inheritdoc/>
    public void Unregistered(InputSystem system, InputDeviceId device)
    {
        InputView view = Input(system);
        if (!view.Devices.ContainsKey(device))
        {
            throw new InvalidOperationException("Unknown inspected device.");
        }

        foreach ((InputSystem System, InputDeviceId Device, InputWindowId Window) scope in _windowFocus.Keys.Where(scope => ReferenceEquals(scope.System, system) && scope.Device == device).ToArray())
        {
            CancelWindowPointers(system, device, scope.Window);
        }

        InvalidateDeviceTargets(system, device, "device-disconnected");
        _revision++;
    }

    /// <inheritdoc/>
    public void SystemDisposed(InputSystem system)
    {
        Check();
        foreach ((InputSystem System, InputDeviceId Device, InputWindowId Window) scope in _windowFocus.Keys.Where(scope => ReferenceEquals(scope.System, system)).ToArray())
        {
            CancelWindowPointers(system, scope.Device, scope.Window);
        }

        InvalidateDeviceTargets(system, null, "system-disposed");
        foreach ((InputSystem System, InputDeviceId Device, InputWindowId Window, InputControl Control) key in _windowTargets.Keys.Where(key => ReferenceEquals(key.System, system)).ToArray())
        {
            _windowValues.Remove(_windowTargets[key]);
            _windowTargets.Remove(key);
        }

        foreach ((InputSystem System, InputDeviceId Device, InputWindowId Window) key in _windowFocus.Keys.Where(key => ReferenceEquals(key.System, system)).ToArray())
        {
            _windowFocus.Remove(key);
        }

        _retiredWindows.RemoveWhere(key => ReferenceEquals(key.System, system));
        if (_inputs.Remove(system))
        {
            _revision++;
        }
    }

    /// <inheritdoc/>
    public void SystemCreated(ActionSystem system, ActionProfile profile)
    {
        Check();
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(profile);
        if (_actions.Count >= 16 || !_actions.TryAdd(system, new(Guid.NewGuid(), profile)))
        {
            throw new InvalidOperationException("Action inspection system capacity reached or the system is already registered.");
        }

        RegisterTargets(system, profile);
        _revision++;
    }

    /// <inheritdoc/>
    public void ProfileChanged(ActionSystem system, ActionProfile profile)
    {
        ActionView view = Action(system);
        ArgumentNullException.ThrowIfNull(profile);
        InvalidateTargets(system, "profile-changed");
        view.Profile = profile;
        RegisterTargets(system, profile);
        view.States.Clear();
        view.Recognized.Clear();
        _revision++;
    }

    /// <inheritdoc/>
    public void Advanced(ActionSystem system, ReadOnlyMemory<ActionEvent> events, ReadOnlyMemory<RecognizedAction> recognized, TimeSpan now)
    {
        ActionView view = Action(system);
        foreach (ActionEvent item in events.Span)
        {
            view.States[(item.ContextId, item.ActionId)] = new(item.Value, item.Phase, item.At, item.Devices.Select(device => device.Value).ToArray());
        }

        foreach (RecognizedAction item in recognized.Span)
        {
            if (view.Recognized.Count == 64)
            {
                view.Recognized.Dequeue();
            }

            view.Recognized.Enqueue(item);
        }

        view.Now = now;
        _revision++;
    }

    /// <inheritdoc/>
    public void SystemDisposed(ActionSystem system)
    {
        Check();
        InvalidateTargets(system, "system-disposed");
        if (_actions.Remove(system))
        {
            _revision++;
        }
    }

    /// <summary>Creates an immutable JSON document for the diagnostic page.</summary>
    /// <returns>The current detached snapshot.</returns>
    public string CreateSnapshot()
    {
        Check();
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 2);
            writer.WriteNumber("revision", _revision);
            writer.WriteStartArray("inputSystems");
            foreach (InputView input in _inputs.Values)
            {
                WriteInput(writer, input, _deviceTargetLookup, _deviceDataTargetLookup);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("actionSystems");
            foreach ((ActionSystem system, ActionView action) in _actions)
            {
                WriteActions(writer, system, action, _targetLookup);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Check();
        _inputs.Clear();
        _actions.Clear();
        _targets.Clear();
        _windowPointerTargets.Clear();
        _windowTargets.Clear();
        _windowValues.Clear();
        _windowFocus.Clear();
        _retiredWindows.Clear();
        _targetLookup.Clear();
        _deviceTargetLookup.Clear();
        _deviceDataTargetLookup.Clear();
        _deviceControlStates.Clear();
        _deviceDataStates.Clear();
        _leases.Clear();
        _reservations.Clear();
        _playbacks.Clear();
        _live.Clear();
        _activeDeviceTargets.Clear();
        _dirtyDeviceTargets.Clear();
        _disposed = true;
    }

    private static void WriteState(Utf8JsonWriter writer, InputDeviceKind kind, InputDeviceState state)
    {
        switch (kind)
        {
            case InputDeviceKind.Keyboard:
                WritePressed(writer, "keys", Enum.GetValues<Key>().Where(state.IsDown).Select(value => value.ToString()));
                break;
            case InputDeviceKind.Mouse:
                WriteVector(writer, "position", state.MousePosition);
                WriteWindowId(writer, "positionWindowId", state.MousePositionWindowId);
                WritePressed(writer, "buttons", Enum.GetValues<MouseButton>().Where(state.IsDown).Select(value => value.ToString()));
                break;
            case InputDeviceKind.Controller:
                WritePressed(writer, "buttons", Enum.GetValues<ControllerButton>().Where(state.IsDown).Select(value => value.ToString()));
                writer.WriteStartObject("sticks");
                foreach (ControllerStick stick in Enum.GetValues<ControllerStick>())
                {
                    WriteVector(writer, stick.ToString(), state.GetStick(stick));
                }

                writer.WriteEndObject();
                writer.WriteStartObject("triggers");
                foreach (ControllerTrigger trigger in Enum.GetValues<ControllerTrigger>())
                {
                    writer.WriteNumber(trigger.ToString(), state.GetTrigger(trigger));
                }

                writer.WriteEndObject();
                break;
            case InputDeviceKind.Touch:
                writer.WriteStartArray("contacts");
                foreach (TouchData touch in state.TouchContacts.Values)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", touch.ContactId.ToString());
                    writer.WriteString("phase", touch.Phase.ToString());
                    WriteWindowId(writer, "positionWindowId", state.TouchPositionWindows.GetValueOrDefault(touch.ContactId));
                    WriteVector(writer, "position", touch.Position);
                    if (touch.Pressure is float pressure)
                    {
                        writer.WriteNumber("pressure", pressure);
                    }
                    else
                    {
                        writer.WriteNull("pressure");
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                break;
            case InputDeviceKind.Pen:
                writer.WriteStartArray("pointers");
                foreach (PenData pen in state.PenPointers.Values)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", pen.PointerId.ToString());
                    writer.WriteString("phase", pen.Phase.ToString());
                    WriteWindowId(writer, "positionWindowId", state.PenPositionWindows.GetValueOrDefault(pen.PointerId));
                    WriteVector(writer, "position", pen.Position);
                    if (pen.Pressure is float pressure)
                    {
                        writer.WriteNumber("pressure", pressure);
                    }
                    else
                    {
                        writer.WriteNull("pressure");
                    }

                    writer.WriteBoolean("contact", pen.IsInContact);
                    writer.WriteString("buttons", pen.Buttons.ToString());
                    writer.WriteBoolean("eraser", pen.IsEraser);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                break;
        }
    }

    private static void WriteActions(Utf8JsonWriter writer, ActionSystem system, ActionView view, Dictionary<(ActionSystem System, string Context, string Action), InputOverrideTarget> targets)
    {
        writer.WriteStartObject();
        WriteWindowId(writer, "windowId", system.WindowId);
        writer.WriteString("id", view.Id);
        writer.WriteNumber("timeMs", view.Now.TotalMilliseconds);
        writer.WriteStartArray("contexts");
        var contexts = view.Profile.Contexts.ToDictionary(context => context.Id, StringComparer.Ordinal);
        foreach (InputContext context in view.Profile.Contexts.OrderByDescending(item => item.Priority).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", context.Id);
            if (context.ParentId == null)
            {
                writer.WriteNull("parent");
            }
            else
            {
                writer.WriteString("parent", context.ParentId);
            }

            writer.WriteNumber("priority", context.Priority);
            writer.WriteBoolean("exclusive", context.Exclusive);
            writer.WriteStartArray("actions");
            foreach (ActionDefinition action in EffectiveActions(view.Profile, context, contexts))
            {
                writer.WriteStartObject();
                writer.WriteString("id", action.Id);
                writer.WriteString("kind", action.Kind.ToString());
                writer.WriteString("target", targets[(system, context.Id, action.Id)].Id);
                writer.WriteNumber("sensitivity", action.Sensitivity);
                writer.WriteBoolean("normalize", action.Normalize);
                writer.WriteNumber("smoothingMs", action.SmoothingSeconds * 1000);
                StateView? state = view.States.GetValueOrDefault((context.Id, action.Id));
                WriteVector(writer, "value", state?.Value ?? Vector2.Zero);
                writer.WriteString("phase", state?.Phase.ToString() ?? "Idle");
                writer.WriteStartArray("bindings");
                foreach (ActionBinding binding in EffectiveBindings(view.Profile, context, action.Id, contexts))
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", binding.Id);
                    writer.WriteString("control", ControlName(binding.Control));
                    WriteVector(writer, "scale", binding.Scale);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("recognized");
        foreach (RecognizedAction recognized in view.Recognized)
        {
            writer.WriteStartObject();
            writer.WriteString("id", recognized.RecognitionId);
            writer.WriteString("context", recognized.ContextId);
            writer.WriteNumber("atMs", recognized.At.TotalMilliseconds);
            WriteVector(writer, "value", recognized.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static IEnumerable<ActionDefinition> EffectiveActions(ActionProfile profile, InputContext context, Dictionary<string, InputContext> contexts)
    {
        var result = profile.Actions.ToDictionary(action => action.Id, StringComparer.Ordinal);
        foreach (InputContext item in Lineage(context, contexts))
        {
            foreach (ActionDefinition action in item.Actions.IsDefault ? [] : item.Actions)
            {
                result[action.Id] = action;
            }
        }

        return result.Values.OrderBy(action => action.Id, StringComparer.Ordinal);
    }

    private static IEnumerable<ActionBinding> EffectiveBindings(ActionProfile profile, InputContext context, string actionId, Dictionary<string, InputContext> contexts)
    {
        ActionBinding[] result = [];
        foreach (InputContext item in Lineage(context, contexts))
        {
            ActionBinding[] local = profile.Bindings.Where(binding => binding.ContextId == item.Id && binding.ActionId == actionId).ToArray();
            if (local.Length != 0)
            {
                result = local;
            }
        }

        return result;
    }

    private static IEnumerable<InputContext> Lineage(InputContext context, Dictionary<string, InputContext> contexts)
    {
        var stack = new Stack<InputContext>();
        for (InputContext? current = context; current != null; current = current.ParentId == null ? null : contexts[current.ParentId])
        {
            stack.Push(current);
        }

        return stack;
    }

    private static void WritePressed(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static string ControlName(InputControl control)
    {
        string value = control.Kind switch
        {
            InputControlKind.Key => ((Key)control.Index).ToString(),
            InputControlKind.MouseButton => ((MouseButton)control.Index).ToString(),
            InputControlKind.ControllerButton => ((ControllerButton)control.Index).ToString(),
            InputControlKind.ControllerStick => ((ControllerStick)control.Index).ToString(),
            InputControlKind.ControllerTrigger => ((ControllerTrigger)control.Index).ToString(),
            _ => control.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return control.Kind + "." + value;
    }

    private static void WriteVector(Utf8JsonWriter writer, string name, Vector2 value)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteEndObject();
    }

    private void WriteInput(Utf8JsonWriter writer, InputView input, Dictionary<(InputSystem System, InputDeviceId Device, InputControl Control), InputOverrideTarget> targets, Dictionary<(InputSystem System, InputDeviceId Device, DeviceTargetKind Kind, ulong Pointer), InputOverrideTarget> dataTargets)
    {
        writer.WriteStartObject();
        writer.WriteString("id", input.Id);
        writer.WriteStartArray("sources");
        foreach (string source in input.Sources)
        {
            writer.WriteStringValue(source);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("devices");
        foreach ((InputDeviceId id, DeviceView device) in input.Devices)
        {
            writer.WriteStartObject();
            writer.WriteString("id", id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString("kind", device.Descriptor.Kind.ToString());
            writer.WriteString("name", device.Descriptor.Name);
            writer.WriteString("identity", device.Descriptor.IdentityKind.ToString());
            writer.WriteString("sequence", device.LatestSequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteBoolean("connected", device.State?.IsConnected ?? false);
            writer.WriteBoolean("focused", device.State?.IsFocused ?? false);
            writer.WriteStartObject("targets");
            foreach (((InputSystem targetSystem, InputDeviceId targetDevice, InputControl control), InputOverrideTarget target) in targets)
            {
                if (input.System != null && ReferenceEquals(targetSystem, input.System) && targetDevice == id && !_windowFocus.Keys.Any(scope => ReferenceEquals(scope.System, targetSystem) && scope.Device == targetDevice))
                {
                    writer.WriteString(ControlName(control), target.Id);
                }
            }

            foreach (((InputSystem targetSystem, InputDeviceId targetDevice, DeviceTargetKind kind, ulong pointer), InputOverrideTarget target) in dataTargets)
            {
                if (input.System != null && ReferenceEquals(targetSystem, input.System) && targetDevice == id && !_windowFocus.Keys.Any(scope => ReferenceEquals(scope.System, targetSystem) && scope.Device == targetDevice))
                {
                    string name = pointer == 0 || kind is DeviceTargetKind.MousePosition or DeviceTargetKind.MouseWheel ? kind.ToString() : $"{kind}.{pointer}";
                    writer.WriteString(name, target.Id);
                }
            }

            writer.WriteEndObject();
            writer.WriteStartObject("state");
            if (device.State != null)
            {
                WriteState(writer, device.Descriptor.Kind, device.State);
            }

            writer.WriteEndObject();
            writer.WriteStartArray("history");
            if (input.System is not null)
            {
                ReadOnlySpan<InputRecord> records = input.System.GetRecords(id).Span;
                foreach (InputRecord record in records[Math.Max(0, records.Length - 32)..])
                {
                    writer.WriteStartObject();
                    writer.WriteString("sequence", record.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    writer.WriteString("type", record.Data.GetType().Name);
                    WriteWindowId(writer, "windowId", record.WindowId);
                    WriteWindowId(writer, "stateWindowId", record.StateWindowId);
                    if (record.Capture is InputCaptureStamp capture)
                    {
                        writer.WriteStartObject("capture");
                        writer.WriteString("streamId", capture.StreamId.ToString("D"));
                        writer.WriteString("sequence", capture.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        writer.WriteEndObject();
                    }
                    else
                    {
                        writer.WriteNull("capture");
                    }

                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        WriteWindowScopes(writer, input);
        writer.WriteEndObject();
    }

    private InputView Input(InputSystem system)
    {
        Check();
        return _inputs.TryGetValue(system, out InputView? view) ? view : throw new InvalidOperationException("Unknown inspected InputSystem.");
    }

    private ActionView Action(ActionSystem system)
    {
        Check();
        return _actions.TryGetValue(system, out ActionView? view) ? view : throw new InvalidOperationException("Unknown inspected ActionSystem.");
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _owner)
        {
            throw new InvalidOperationException("Input inspection requires its owning thread.");
        }
    }

    private sealed class InputView(Guid id)
    {
        public Guid Id { get; } = id;

        public InputSystem? System { get; set; }

        public List<string> Sources { get; } = [];

        public Dictionary<InputDeviceId, DeviceView> Devices { get; } = [];
    }

    private sealed class DeviceView(InputDeviceDescriptor descriptor)
    {
        public InputDeviceDescriptor Descriptor { get; } = descriptor;

        public InputDeviceState? State { get; set; }

        public ulong LatestSequence { get; set; }

        public bool Focused { get; set; }
    }

    private sealed class ActionView(Guid id, ActionProfile profile)
    {
        public Guid Id { get; } = id;

        public ActionProfile Profile { get; set; } = profile;

        public Dictionary<(string Context, string Action), StateView> States { get; } = [];

        public Queue<RecognizedAction> Recognized { get; } = new();

        public TimeSpan Now { get; set; }
    }

    private sealed record StateView(Vector2 Value, ActionPhase Phase, TimeSpan At, ulong[] Devices);
}
