using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Lumyte.Input.Abstractions;
using Lumyte.Input.Actions;

namespace Lumyte.Input.Diagnostics;

/// <summary>Observes and overrides explicitly selected window scopes without using current activation.</summary>
public sealed partial class InputInspectionSession
{
    private readonly Dictionary<(InputSystem System, InputDeviceId Device, InputWindowId Window, InputControl Control), InputOverrideTarget> _windowTargets = [];
    private readonly Dictionary<InputOverrideTarget, DeviceControlState> _windowValues = [];
    private readonly Dictionary<(InputSystem System, InputDeviceId Device, InputWindowId Window), bool> _windowFocus = [];
    private readonly HashSet<(InputSystem System, InputWindowId Window)> _retiredWindows = [];

    /// <summary>Gets an observed control target in exactly one live window scope.</summary>
    /// <param name="system">The inspected input system.</param>
    /// <param name="device">The physical device.</param>
    /// <param name="window">The explicit observed scope.</param>
    /// <param name="control">The control whose target is requested.</param>
    /// <returns>The target, or a missing-target exception for an unobserved or retired scope.</returns>
    public InputOverrideTarget GetWindowTarget(InputSystem system, InputDeviceId device, InputWindowId window, InputControl control)
    {
        Check();
        if (!_windowFocus.ContainsKey((system, device, window)) || _retiredWindows.Contains((system, window)))
        {
            throw new KeyNotFoundException("The window scope has not been observed or has retired.");
        }

        InputDeviceState state = system.GetState(device, window);
        if (!state.IsConnected)
        {
            throw new KeyNotFoundException("The device has disconnected.");
        }

        _ = control.Kind switch
        {
            InputControlKind.Key => state.IsDown((Key)control.Index) ? 1 : 0,
            InputControlKind.MouseButton => state.IsDown((MouseButton)control.Index) ? 1 : 0,
            InputControlKind.ControllerButton => state.IsDown((ControllerButton)control.Index) ? 1 : 0,
            InputControlKind.ControllerStick => state.GetStick((ControllerStick)control.Index).X,
            InputControlKind.ControllerTrigger => state.GetTrigger((ControllerTrigger)control.Index),
            _ => throw new ArgumentException("Unknown control kind.", nameof(control)),
        };
        return EnsureWindowTarget(system, device, window, control);
    }

    /// <inheritdoc/>
    public IReadOnlyList<InputScopedEvent> ProcessEvents(InputSystem system, Guid streamId, IReadOnlyList<InputScopedEvent> events, TimeSpan now)
    {
        _ = Input(system);
        _logicalNow = now > _logicalNow ? now : _logicalNow;
        var result = new List<InputScopedEvent>(events.Count);
        foreach (InputScopedEvent item in events)
        {
            InputSourceEvent source = item.Input;
            if (item.StateWindowId is not InputWindowId window)
            {
                // An ordered windowless control never inherits a window's diagnostic target.
                result.Add(item);
                continue;
            }

            (InputSystem System, InputDeviceId Device, InputWindowId Window) scope = (system, source.DeviceId, window);
            _windowFocus.TryAdd(scope, false);
            if (source.Data is FocusData focus)
            {
                _windowFocus[scope] = focus.IsFocused;
                if (!focus.IsFocused)
                {
                    CancelWindowTargets(system, source.DeviceId, window, retire: false);
                }
            }
            else if (source.Data is WindowInputClosedData)
            {
                _windowFocus[scope] = false;
                _retiredWindows.Add((system, window));
                CancelWindowTargets(system, source.DeviceId, window, retire: true);
            }

            if (TryReadControl(source.Data, out InputControl control, out Vector2 physical) && !_retiredWindows.Contains((system, window)))
            {
                InputOverrideTarget target = EnsureWindowTarget(system, source.DeviceId, window, control);
                DeviceControlState state = _windowValues[target];
                state.Physical = physical;
                Vector2 desired = EffectiveDeviceValue(target, physical, now);
                state.Effective = desired;
                result.Add(item with { Input = source with { Data = CreateControlData(control, desired, source.Data) } });
            }
            else
            {
                if (source.Data is MouseButtonCanceledData canceled)
                {
                    (InputSystem System, InputDeviceId Device, InputWindowId Window, InputControl Control) identity = (system, source.DeviceId, window, new InputControl(InputControlKind.MouseButton, (int)canceled.Button));
                    if (_windowTargets.TryGetValue(identity, out InputOverrideTarget target))
                    {
                        InvalidateTarget(target, "button-canceled");
                        _windowValues[target].Physical = Vector2.Zero;
                        _windowValues[target].Effective = Vector2.Zero;
                    }
                }

                result.Add(item with { Input = source with { Data = ApplyWindowPointer(system, source, window, now) } });
            }
        }

        foreach (KeyValuePair<(InputSystem System, InputDeviceId Device, InputWindowId Window, InputControl Control), InputOverrideTarget> pair in _windowTargets.Where(pair => ReferenceEquals(pair.Key.System, system)).ToArray())
        {
            if (!_targets.ContainsKey(pair.Value) || _retiredWindows.Contains((system, pair.Key.Window)))
            {
                continue;
            }

            DeviceControlState state = _windowValues[pair.Value];
            Vector2 desired = EffectiveDeviceValue(pair.Value, state.Physical, now);
            if (desired != state.Effective)
            {
                result.Add(new(new(pair.Key.Device, pair.Key.Window, null, CreateControlData(pair.Key.Control, desired)), pair.Key.Window));
                state.Effective = desired;
            }
        }

        AdvanceWindowPointers(system, now, result);
        return result.AsReadOnly();
    }

    private static void WriteWindowId(Utf8JsonWriter writer, string name, InputWindowId? id)
    {
        if (id is InputWindowId window)
        {
            writer.WriteString(name, window.Value.ToString("D"));
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private InputOverrideTarget EnsureWindowTarget(InputSystem system, InputDeviceId device, InputWindowId window, InputControl control)
    {
        (InputSystem System, InputDeviceId Device, InputWindowId Window, InputControl Control) identity = (system, device, window, control);
        if (!_windowTargets.TryGetValue(identity, out InputOverrideTarget target))
        {
            if (_windowTargets.Count >= 8192)
            {
                throw new InvalidOperationException("Window control inspection capacity reached.");
            }

            target = new(Guid.NewGuid());
            ActionValueKind kind = control.Kind switch
            {
                InputControlKind.ControllerStick => ActionValueKind.Axis2D,
                InputControlKind.ControllerTrigger => ActionValueKind.Axis1D,
                _ => ActionValueKind.Button,
            };
            _windowTargets.Add(identity, target);
            _windowValues.Add(target, new());
            _targets.Add(target, TargetEntry.ForDevice(system, device, control, kind) with { WindowId = window });
        }

        return target;
    }

    private void CancelWindowTargets(InputSystem system, InputDeviceId device, InputWindowId window, bool retire)
    {
        CancelWindowPointers(system, device, window);
        foreach (KeyValuePair<(InputSystem System, InputDeviceId Device, InputWindowId Window, InputControl Control), InputOverrideTarget> pair in _windowTargets.Where(pair => ReferenceEquals(pair.Key.System, system) && pair.Key.Device == device && pair.Key.Window == window).ToArray())
        {
            InvalidateTarget(pair.Value, retire ? "window-closed" : "window-focus-lost");
            _activeDeviceTargets.Remove(pair.Value);
            _dirtyDeviceTargets.Remove(pair.Value);
            _windowValues[pair.Value].Physical = Vector2.Zero;
            _windowValues[pair.Value].Effective = Vector2.Zero;
            if (retire)
            {
                _targets.Remove(pair.Value);
            }
        }
    }

    private void WriteWindowScopes(Utf8JsonWriter writer, InputView input)
    {
        writer.WriteStartArray("windowScopes");
        foreach ((InputSystem System, InputDeviceId Device, InputWindowId Window) scope in _windowFocus.Keys.Where(scope => ReferenceEquals(scope.System, input.System)))
        {
            writer.WriteStartObject();
            writer.WriteString("deviceId", scope.Device.Value.ToString(CultureInfo.InvariantCulture));
            WriteWindowId(writer, "windowId", scope.Window);
            writer.WriteBoolean("retired", _retiredWindows.Contains((scope.System, scope.Window)));
            writer.WriteStartObject("state");
            InputDeviceState state = scope.System.GetState(scope.Device, scope.Window);
            writer.WriteBoolean("connected", state.IsConnected);
            writer.WriteBoolean("focused", state.IsFocused);
            WriteState(writer, input.Devices[scope.Device].Descriptor.Kind, state);
            writer.WriteEndObject();
            writer.WriteStartObject("targets");
            foreach (KeyValuePair<(InputSystem System, InputDeviceId Device, InputWindowId Window, InputControl Control), InputOverrideTarget> target in _windowTargets.Where(pair => ReferenceEquals(pair.Key.System, scope.System) && pair.Key.Device == scope.Device && pair.Key.Window == scope.Window && _targets.ContainsKey(pair.Value)))
            {
                writer.WriteString(ControlName(target.Key.Control), target.Value.Id);
            }

            foreach (KeyValuePair<(InputSystem System, InputDeviceId Device, InputWindowId Window, DeviceTargetKind Kind, ulong Pointer), InputOverrideTarget> target in _windowPointerTargets.Where(pair => ReferenceEquals(pair.Key.System, scope.System) && pair.Key.Device == scope.Device && pair.Key.Window == scope.Window && _targets.ContainsKey(pair.Value)))
            {
                writer.WriteString($"{target.Key.Kind}.{target.Key.Pointer}", target.Value.Id);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
