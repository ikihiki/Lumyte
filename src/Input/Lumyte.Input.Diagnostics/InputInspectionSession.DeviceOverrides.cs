using System.Numerics;
using Lumyte.Input.Actions;

namespace Lumyte.Input.Diagnostics;

#pragma warning disable SA1204 // Partial implementation groups helpers with the feature they support.

/// <summary>Implements device-control targets and effective event generation.</summary>
public sealed partial class InputInspectionSession
{
    /// <inheritdoc/>
    public IReadOnlyList<InputData> Process(InputSystem system, InputDeviceId device, IReadOnlyList<InputData> data, TimeSpan now)
    {
        InputView input = Input(system);
        DeviceView view = input.Devices.TryGetValue(device, out DeviceView? existing) ? existing : throw new InvalidOperationException("Unknown inspected device.");
        ArgumentNullException.ThrowIfNull(data);
        if (now > _logicalNow)
        {
            _logicalNow = now;
        }

        List<InputData>? effective = null;
        foreach (InputData item in data)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item is FocusData focus)
            {
                effective ??= new(data.Count + 4);
                effective.Add(item);
                view.Focused = focus.IsFocused;
                if (!focus.IsFocused)
                {
                    ResetDeviceControlStates(system, device);
                    InvalidateAllPointerTargets(system, device);
                }

                continue;
            }

            if (TryProcessTypedData(system, device, item, now, out InputData? typed))
            {
                if (!ReferenceEquals(typed, item) || effective != null)
                {
                    effective ??= CopyPrefix(data, item);
                    effective.Add(typed);
                }

                continue;
            }

            if (!TryReadControl(item, out InputControl control, out Vector2 physical))
            {
                if (effective != null)
                {
                    effective.Add(item);
                }

                continue;
            }

            DeviceControlState state = _deviceControlStates[(system, device, control)];
            state.Physical = physical;
            if (!view.Focused)
            {
                if (effective != null)
                {
                    effective.Add(item);
                }

                continue;
            }

            InputOverrideTarget target = _deviceTargetLookup[(system, device, control)];
            if (!_reservations.ContainsKey(target))
            {
                if (effective != null)
                {
                    effective.Add(item);
                }

                state.Effective = physical;
                continue;
            }

            Vector2 desired = EffectiveDeviceValue(system, device, control, physical, now);
            if (desired != physical || effective != null)
            {
                effective ??= CopyPrefix(data, item);
                if (desired != state.Effective || (item is KeyData { IsRepeat: true } && desired == physical))
                {
                    effective.Add(CreateControlData(control, desired, item));
                }
            }

            state.Effective = desired;
        }

        if (view.Focused)
        {
            foreach (InputOverrideTarget target in _activeDeviceTargets.ToArray())
            {
                EvaluateDeviceTarget(target, system, device, now, ref effective, data, removeWhenRestored: false);
            }

            if (_dirtyDeviceTargets.Count != 0)
            {
                var restored = new List<InputOverrideTarget>();
                foreach (InputOverrideTarget target in _dirtyDeviceTargets)
                {
                    if (EvaluateDeviceTarget(target, system, device, now, ref effective, data, removeWhenRestored: true))
                    {
                        restored.Add(target);
                    }
                }

                foreach (InputOverrideTarget target in restored)
                {
                    _dirtyDeviceTargets.Remove(target);
                }
            }
        }

        return effective ?? data;
    }

    private bool EvaluateDeviceTarget(InputOverrideTarget target, InputSystem system, InputDeviceId device, TimeSpan now, ref List<InputData>? effective, IReadOnlyList<InputData> data, bool removeWhenRestored)
    {
        if (!_targets.TryGetValue(target, out TargetEntry? entry) || !ReferenceEquals(entry.InputSystem, system) || entry.Device != device)
        {
            return false;
        }

        if (entry.DeviceKind == DeviceTargetKind.Control)
        {
            DeviceControlState state = _deviceControlStates[(system, device, entry.Control)];
            Vector2 desired = EffectiveDeviceValue(target, state.Physical, now);
            if (desired != state.Effective)
            {
                effective ??= data.ToList();
                effective.Add(CreateControlData(entry.Control, desired));
                state.Effective = desired;
            }

            return removeWhenRestored && desired == state.Physical;
        }

        DeviceDataState dataState = _deviceDataStates[target];
        Vector2 typedDesired = EffectiveDeviceValue(target, dataState.Physical, now);
        if (typedDesired != dataState.Effective && dataState.Template != null)
        {
            effective ??= data.ToList();
            effective.Add(CreateSyntheticTypedData(entry, dataState.Template, typedDesired));
            dataState.Effective = typedDesired;
        }

        return removeWhenRestored && typedDesired == dataState.Physical;
    }

    private static List<InputData> CopyPrefix(IReadOnlyList<InputData> data, InputData current)
    {
        var result = new List<InputData>(data.Count + 4);
        for (int i = 0; i < data.Count && !ReferenceEquals(data[i], current); i++)
        {
            result.Add(data[i]);
        }

        return result;
    }

    private static bool TryReadControl(InputData data, out InputControl control, out Vector2 value)
    {
        (control, value) = data switch
        {
            KeyData key => (InputControl.ForKey(key.Key), new Vector2(key.IsDown ? 1 : 0, 0)),
            MouseButtonData mouse => (new InputControl(InputControlKind.MouseButton, (int)mouse.Button), new Vector2(mouse.IsDown ? 1 : 0, 0)),
            ControllerButtonData button => (InputControl.ForButton(button.Button), new Vector2(button.IsDown ? 1 : 0, 0)),
            ControllerStickData stick => (new(InputControlKind.ControllerStick, (int)stick.Stick), stick.Value),
            ControllerTriggerData trigger => (new InputControl(InputControlKind.ControllerTrigger, (int)trigger.Trigger), new Vector2(trigger.Value, 0)),
            _ => (default, default(Vector2)),
        };
        return data is KeyData or MouseButtonData or ControllerButtonData or ControllerStickData or ControllerTriggerData;
    }

    private static InputData CreateControlData(InputControl control, Vector2 value, InputData? original = null) => control.Kind switch
    {
        InputControlKind.Key => new KeyData((Key)control.Index, value.X >= 0.5f, original is KeyData { IsRepeat: true }),
        InputControlKind.MouseButton => new MouseButtonData((MouseButton)control.Index, value.X >= 0.5f),
        InputControlKind.ControllerButton => new ControllerButtonData((ControllerButton)control.Index, value.X >= 0.5f),
        InputControlKind.ControllerStick => new ControllerStickData((ControllerStick)control.Index, Vector2.Clamp(value, -Vector2.One, Vector2.One)),
        InputControlKind.ControllerTrigger => new ControllerTriggerData((ControllerTrigger)control.Index, Math.Clamp(value.X, 0, 1)),
        _ => throw new ArgumentOutOfRangeException(nameof(control)),
    };

    private Vector2 EffectiveDeviceValue(InputSystem system, InputDeviceId device, InputControl control, Vector2 physical, TimeSpan now)
    {
        InputOverrideTarget target = _deviceTargetLookup[(system, device, control)];
        return EffectiveDeviceValue(target, physical, now);
    }

    private Vector2 EffectiveDeviceValue(InputOverrideTarget target, Vector2 physical, TimeSpan now)
    {
        if (!_reservations.TryGetValue(target, out Guid reservation))
        {
            return physical;
        }

        if (_leases.TryGetValue(reservation, out LeaseEntry? lease))
        {
            if (now >= lease.ExpiresAt)
            {
                ReleaseLease(lease, "expired");
                return physical;
            }

            return Combine(target, physical, lease.Spec);
        }

        if (_playbacks.TryGetValue(reservation, out PlaybackEntry? playback) && playback.Values.TryGetValue(target, out Vector2 tapeValue))
        {
            return tapeValue;
        }

        return _live.TryGetValue(reservation, out LiveEntry? live) && live.Values.TryGetValue(target, out Vector2 liveValue) ? liveValue : physical;
    }

    private void RegisterDeviceTargets(InputSystem system, InputDeviceId device, InputDeviceKind kind)
    {
        IEnumerable<(InputControl Control, ActionValueKind Kind)> controls = kind switch
        {
            InputDeviceKind.Keyboard => Enum.GetValues<Key>().Where(key => key != Key.Unknown).Select(key => (InputControl.ForKey(key), ActionValueKind.Button)),
            InputDeviceKind.Mouse => Enum.GetValues<MouseButton>().Select(button => (new InputControl(InputControlKind.MouseButton, (int)button), ActionValueKind.Button)),
            InputDeviceKind.Controller => Enum.GetValues<ControllerButton>().Select(button => (InputControl.ForButton(button), ActionValueKind.Button))
                .Concat(Enum.GetValues<ControllerStick>().Select(stick => (new InputControl(InputControlKind.ControllerStick, (int)stick), ActionValueKind.Axis2D)))
                .Concat(Enum.GetValues<ControllerTrigger>().Select(trigger => (new InputControl(InputControlKind.ControllerTrigger, (int)trigger), ActionValueKind.Axis1D))),
            _ => [],
        };

        foreach ((InputControl control, ActionValueKind valueKind) in controls)
        {
            var target = new InputOverrideTarget(Guid.NewGuid());
            _deviceTargetLookup.Add((system, device, control), target);
            _deviceControlStates.Add((system, device, control), new());
            _targets.Add(target, TargetEntry.ForDevice(system, device, control, valueKind));
        }

        if (kind == InputDeviceKind.Mouse)
        {
            RegisterDataTarget(system, device, DeviceTargetKind.MousePosition, 0, ActionValueKind.Axis2D);
            RegisterDataTarget(system, device, DeviceTargetKind.MouseWheel, 0, ActionValueKind.Axis2D);
        }
    }

    private bool TryProcessTypedData(InputSystem system, InputDeviceId device, InputData data, TimeSpan now, out InputData result)
    {
        switch (data)
        {
            case MouseMoveData move:
                result = ApplyTypedTarget(system, device, DeviceTargetKind.MousePosition, 0, move.Position, move, now);
                return true;
            case MouseWheelData wheel:
                result = ApplyTypedTarget(system, device, DeviceTargetKind.MouseWheel, 0, wheel.Delta, wheel, now);
                return true;
            case TouchData touch:
                EnsurePointerTargets(system, device, touch.ContactId.Value, touch: true);
                if (touch.Phase is TouchPhase.Ended or TouchPhase.Canceled)
                {
                    UpdateTypedPhysical(system, device, DeviceTargetKind.TouchPosition, touch.ContactId.Value, touch.Position, touch);
                    UpdateTypedPhysical(system, device, DeviceTargetKind.TouchPressure, touch.ContactId.Value, new(touch.Pressure ?? -1, 0), touch);
                    InvalidatePointerTargets(system, device, touch.ContactId.Value, touch: true);
                    result = touch;
                    return true;
                }

                var touchPosition = (TouchData)ApplyTypedTarget(system, device, DeviceTargetKind.TouchPosition, touch.ContactId.Value, touch.Position, touch, now);
                result = ApplyTypedTarget(system, device, DeviceTargetKind.TouchPressure, touch.ContactId.Value, new(touch.Pressure ?? -1, 0), touchPosition, now);
                return true;
            case PenData pen:
                EnsurePointerTargets(system, device, pen.PointerId.Value, touch: false);
                if (pen.Phase is PenPhase.Left or PenPhase.Canceled)
                {
                    UpdateTypedPhysical(system, device, DeviceTargetKind.PenPosition, pen.PointerId.Value, pen.Position, pen);
                    UpdateTypedPhysical(system, device, DeviceTargetKind.PenPressure, pen.PointerId.Value, new(pen.Pressure ?? -1, 0), pen);
                    InvalidatePointerTargets(system, device, pen.PointerId.Value, touch: false);
                    result = pen;
                    return true;
                }

                var penPosition = (PenData)ApplyTypedTarget(system, device, DeviceTargetKind.PenPosition, pen.PointerId.Value, pen.Position, pen, now);
                result = ApplyTypedTarget(system, device, DeviceTargetKind.PenPressure, pen.PointerId.Value, new(pen.Pressure ?? -1, 0), penPosition, now);
                return true;
            default:
                result = data;
                return false;
        }
    }

    private InputData ApplyTypedTarget(InputSystem system, InputDeviceId device, DeviceTargetKind kind, ulong pointer, Vector2 physical, InputData template, TimeSpan now)
    {
        InputOverrideTarget target = _deviceDataTargetLookup[(system, device, kind, pointer)];
        DeviceDataState state = _deviceDataStates[target];
        state.Physical = physical;
        state.Template = template;
        Vector2 desired = EffectiveDeviceValue(target, physical, now);
        state.Effective = desired;
        return desired == physical ? template : CreateTypedData(kind, template, desired);
    }

    private void UpdateTypedPhysical(InputSystem system, InputDeviceId device, DeviceTargetKind kind, ulong pointer, Vector2 physical, InputData template)
    {
        InputOverrideTarget target = _deviceDataTargetLookup[(system, device, kind, pointer)];
        DeviceDataState state = _deviceDataStates[target];
        state.Physical = physical;
        state.Effective = physical;
        state.Template = template;
    }

    private static InputData CreateTypedData(DeviceTargetKind kind, InputData template, Vector2 value, bool synthetic = false) => (kind, template) switch
    {
        (DeviceTargetKind.MousePosition, MouseMoveData) => new MouseMoveData(value),
        (DeviceTargetKind.MouseWheel, MouseWheelData) => new MouseWheelData(value),
        (DeviceTargetKind.TouchPosition, TouchData touch) => touch with { Phase = synthetic ? TouchPhase.Moved : touch.Phase, Position = value },
        (DeviceTargetKind.TouchPressure, TouchData touch) => touch with { Phase = synthetic ? TouchPhase.Moved : touch.Phase, Pressure = value.X < 0 ? null : Math.Clamp(value.X, 0, 1) },
        (DeviceTargetKind.PenPosition, PenData pen) => pen with { Phase = synthetic ? PenPhase.Moved : pen.Phase, Position = value },
        (DeviceTargetKind.PenPressure, PenData pen) => pen with { Phase = synthetic ? PenPhase.Moved : pen.Phase, Pressure = value.X < 0 ? null : Math.Clamp(value.X, 0, 1) },
        _ => throw new ArgumentException("Typed target and input data do not match.", nameof(template)),
    };

    private InputData CreateSyntheticTypedData(TargetEntry entry, InputData template, Vector2 value)
    {
        if (template is TouchData touch)
        {
            InputOverrideTarget positionTarget = _deviceDataTargetLookup[(entry.InputSystem!, entry.Device, DeviceTargetKind.TouchPosition, entry.Pointer)];
            InputOverrideTarget pressureTarget = _deviceDataTargetLookup[(entry.InputSystem!, entry.Device, DeviceTargetKind.TouchPressure, entry.Pointer)];
            Vector2 position = entry.DeviceKind == DeviceTargetKind.TouchPosition ? value : _deviceDataStates[positionTarget].Effective;
            Vector2 pressure = entry.DeviceKind == DeviceTargetKind.TouchPressure ? value : _deviceDataStates[pressureTarget].Effective;
            return touch with { Phase = TouchPhase.Moved, Position = position, Pressure = pressure.X < 0 ? null : Math.Clamp(pressure.X, 0, 1) };
        }

        if (template is PenData pen)
        {
            InputOverrideTarget positionTarget = _deviceDataTargetLookup[(entry.InputSystem!, entry.Device, DeviceTargetKind.PenPosition, entry.Pointer)];
            InputOverrideTarget pressureTarget = _deviceDataTargetLookup[(entry.InputSystem!, entry.Device, DeviceTargetKind.PenPressure, entry.Pointer)];
            Vector2 position = entry.DeviceKind == DeviceTargetKind.PenPosition ? value : _deviceDataStates[positionTarget].Effective;
            Vector2 pressure = entry.DeviceKind == DeviceTargetKind.PenPressure ? value : _deviceDataStates[pressureTarget].Effective;
            return pen with { Phase = PenPhase.Moved, Position = position, Pressure = pressure.X < 0 ? null : Math.Clamp(pressure.X, 0, 1) };
        }

        return CreateTypedData(entry.DeviceKind, template, value, synthetic: true);
    }

    private void EnsurePointerTargets(InputSystem system, InputDeviceId device, ulong pointer, bool touch)
    {
        DeviceTargetKind position = touch ? DeviceTargetKind.TouchPosition : DeviceTargetKind.PenPosition;
        if (_deviceDataTargetLookup.ContainsKey((system, device, position, pointer)))
        {
            return;
        }

        RegisterDataTarget(system, device, position, pointer, ActionValueKind.Axis2D);
        RegisterDataTarget(system, device, touch ? DeviceTargetKind.TouchPressure : DeviceTargetKind.PenPressure, pointer, ActionValueKind.Axis1D);
    }

    private void RegisterDataTarget(InputSystem system, InputDeviceId device, DeviceTargetKind kind, ulong pointer, ActionValueKind valueKind)
    {
        var target = new InputOverrideTarget(Guid.NewGuid());
        _deviceDataTargetLookup.Add((system, device, kind, pointer), target);
        _deviceDataStates.Add(target, new()
        {
            Template = kind switch
            {
                DeviceTargetKind.MousePosition => new MouseMoveData(Vector2.Zero),
                DeviceTargetKind.MouseWheel => new MouseWheelData(Vector2.Zero),
                _ => null,
            },
        });
        _targets.Add(target, TargetEntry.ForDeviceData(system, device, kind, pointer, valueKind));
    }

    private void InvalidatePointerTargets(InputSystem system, InputDeviceId device, ulong pointer, bool touch)
    {
        DeviceTargetKind[] kinds = touch ? [DeviceTargetKind.TouchPosition, DeviceTargetKind.TouchPressure] : [DeviceTargetKind.PenPosition, DeviceTargetKind.PenPressure];
        foreach (DeviceTargetKind kind in kinds)
        {
            (InputSystem, InputDeviceId, DeviceTargetKind, ulong) key = (system, device, kind, pointer);
            if (!_deviceDataTargetLookup.Remove(key, out InputOverrideTarget target))
            {
                continue;
            }

            InvalidateTarget(target, "pointer-ended");
            _activeDeviceTargets.Remove(target);
            _dirtyDeviceTargets.Remove(target);
            _deviceDataStates.Remove(target);
            _targets.Remove(target);
        }
    }

    private void InvalidateAllPointerTargets(InputSystem system, InputDeviceId device)
    {
        foreach ((DeviceTargetKind kind, ulong pointer) in _deviceDataTargetLookup.Keys
            .Where(key => ReferenceEquals(key.System, system) && key.Device == device && key.Kind is DeviceTargetKind.TouchPosition or DeviceTargetKind.PenPosition)
            .Select(key => (key.Kind, key.Pointer)).ToArray())
        {
            InvalidatePointerTargets(system, device, pointer, kind == DeviceTargetKind.TouchPosition);
        }
    }

    private void InvalidateDeviceTargets(InputSystem system, InputDeviceId? device, string reason)
    {
        foreach (InputOverrideTarget target in _targets.Where(pair => ReferenceEquals(pair.Value.InputSystem, system) && (device == null || pair.Value.Device == device)).Select(pair => pair.Key).ToArray())
        {
            InvalidateTarget(target, reason);
            _activeDeviceTargets.Remove(target);
            _dirtyDeviceTargets.Remove(target);
            _targets.Remove(target);
        }

        foreach ((InputSystem System, InputDeviceId Device, InputControl Control) key in _deviceTargetLookup.Keys.Where(key => ReferenceEquals(key.System, system) && (device == null || key.Device == device)).ToArray())
        {
            _deviceTargetLookup.Remove(key);
            _deviceControlStates.Remove(key);
        }

        foreach ((InputSystem System, InputDeviceId Device, DeviceTargetKind Kind, ulong Pointer) key in _deviceDataTargetLookup.Keys.Where(key => ReferenceEquals(key.System, system) && (device == null || key.Device == device)).ToArray())
        {
            InputOverrideTarget target = _deviceDataTargetLookup[key];
            _deviceDataTargetLookup.Remove(key);
            _deviceDataStates.Remove(target);
        }
    }

    private void InvalidateTarget(InputOverrideTarget target, string reason)
    {
        if (!_reservations.TryGetValue(target, out Guid reservation))
        {
            return;
        }

        if (_leases.TryGetValue(reservation, out LeaseEntry? lease))
        {
            ReleaseLease(lease, reason);
        }

        if (_playbacks.TryGetValue(reservation, out PlaybackEntry? playback))
        {
            StopTape(playback, reason);
        }

        if (_live.TryGetValue(reservation, out LiveEntry? live))
        {
            StopLive(live, reason);
        }
    }

    private void ResetDeviceControlStates(InputSystem system, InputDeviceId device)
    {
        foreach (((InputSystem targetSystem, InputDeviceId targetDevice, _), DeviceControlState state) in _deviceControlStates)
        {
            if (ReferenceEquals(targetSystem, system) && targetDevice == device)
            {
                state.Physical = Vector2.Zero;
                state.Effective = Vector2.Zero;
            }
        }
    }
}

#pragma warning restore SA1204
